using System.Diagnostics;
using OrderProcessing.Phase4.Carriers;
using OrderProcessing.Phase4.Models;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase4.Services;

public class ShippingQuoteService
{
    private readonly List<IShippingCarrier> _carriers;

    public ShippingQuoteService(IEnumerable<IShippingCarrier>? carriers = null)
    {
        _carriers = carriers != null
            ? carriers.ToList()
            : new List<IShippingCarrier>
            {
                new FastShip(),
                new CairoExpress(),
                new NileCourier()
            };
    }

    public async Task<ShippingQuote> GetFastestQuoteAsync(Order order, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // Linked token with caller token and 1.2s budget timeout
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(1.2));

        // Start all 3 carriers concurrently (R1)
        var carrierTasks = new Dictionary<Task<decimal>, IShippingCarrier>();
        foreach (var carrier in _carriers)
        {
            var task = carrier.GetQuoteAsync(order, linkedCts.Token);
            carrierTasks[task] = carrier;
        }

        var activeTasks = new List<Task<decimal>>(carrierTasks.Keys);
        var exceptions = new List<Exception>();

        while (activeTasks.Count > 0)
        {
            Task<decimal> finishedTask;
            try
            {
                finishedTask = await Task.WhenAny(activeTasks).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error($"Unexpected error while waiting for carriers: {ex.Message}");
                break;
            }

            activeTasks.Remove(finishedTask);

            if (finishedTask.IsCompletedSuccessfully)
            {
                var winnerCarrier = carrierTasks[finishedTask];
                decimal quotePrice = finishedTask.Result;
                sw.Stop();

                // R3: Cancel remaining carriers
                linkedCts.Cancel();

                return new ShippingQuote
                {
                    Carrier = winnerCarrier.Name,
                    Price = quotePrice,
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }
            else
            {
                // R2: Carrier failed, record failure and keep waiting for the others
                if (finishedTask.Exception != null)
                {
                    exceptions.AddRange(finishedTask.Exception.InnerExceptions);
                }
            }
        }

        sw.Stop();

        // Check if caller cancelled
        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        // R5: If all three carriers failed within the budget
        if (exceptions.Count == _carriers.Count)
        {
            throw new ShippingProviderUnavailableException("All carriers failed");
        }

        // R4: Time budget 1.2s exceeded: return flat rate
        linkedCts.Cancel();
        return new ShippingQuote
        {
            Carrier = "Flat rate",
            Price = 25.00m,
            ElapsedMs = sw.ElapsedMilliseconds
        };
    }
}

