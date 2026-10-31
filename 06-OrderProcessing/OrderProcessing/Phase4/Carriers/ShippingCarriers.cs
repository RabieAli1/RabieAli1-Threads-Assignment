using OrderProcessing.Phase2;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase4.Carriers;

public interface IShippingCarrier
{
    string Name { get; }
    decimal PriceFactor { get; }
    Task<decimal> GetQuoteAsync(Order order, CancellationToken ct);
}

public abstract class BaseShippingCarrier : IShippingCarrier
{
    public abstract string Name { get; }
    public abstract decimal PriceFactor { get; }
    protected abstract (int Min, int Max) ResponseRangeMs { get; }
    protected readonly HashSet<int> _failForOrderIds;
    protected readonly JsonFileStore _store;

    public BaseShippingCarrier(IEnumerable<int>? failForOrderIds = null, JsonFileStore? store = null)
    {
        _failForOrderIds = failForOrderIds != null ? new HashSet<int>(failForOrderIds) : new HashSet<int>();
        _store = store ?? new JsonFileStore();
    }

    protected virtual void CheckBuiltInFailure(Order order)
    {
        if (_failForOrderIds.Contains(order.Id))
        {
            throw new ShippingProviderUnavailableException($"{Name} is unavailable for Order #{order.Id}");
        }
    }

    public async Task<decimal> GetQuoteAsync(Order order, CancellationToken ct)
    {
        try
        {
            Log.Info($"[{Name}] Starting quote calculation for Order #{order.Id}...");

            // Handshake 150ms
            await Task.Delay(150, ct).ConfigureAwait(false);

            CheckBuiltInFailure(order);

            // Carrier response time
            int latency = Random.Shared.Next(ResponseRangeMs.Min, ResponseRangeMs.Max + 1);
            await Task.Delay(latency, ct).ConfigureAwait(false);

            // Compute base shipping using Task 8.1 formula
            var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
            var prodMap = products.ToDictionary(p => p.Id);

            int totalQty = order.Items.Sum(i => i.Quantity);
            decimal totalWeight = order.Items.Sum(i => prodMap.TryGetValue(i.ProductId, out var p) ? p.WeightKg * i.Quantity : 0m);

            decimal baseShipping = PricingRules.CalculateShippingCost(order.SubTotal, totalQty, totalWeight);
            decimal carrierPrice = PricingRules.RoundMoney(baseShipping * PriceFactor);

            Log.Success($"[{Name}] Quote ready for Order #{order.Id}: {carrierPrice:F2} (after {150 + latency} ms)");
            return carrierPrice;
        }
        catch (OperationCanceledException)
        {
            Log.Info($"[{Name}] cancelled");
            throw;
        }
    }
}

public class FastShip : BaseShippingCarrier
{
    public override string Name => "FastShip";
    public override decimal PriceFactor => 1.00m;
    protected override (int Min, int Max) ResponseRangeMs => (500, 1500);

    public FastShip(IEnumerable<int>? failForOrderIds = null, JsonFileStore? store = null)
        : base(failForOrderIds, store) { }
}

public class CairoExpress : BaseShippingCarrier
{
    public override string Name => "CairoExpress";
    public override decimal PriceFactor => 0.95m;
    protected override (int Min, int Max) ResponseRangeMs => (300, 2000);

    public CairoExpress(IEnumerable<int>? failForOrderIds = null, JsonFileStore? store = null)
        : base(failForOrderIds, store) { }

    protected override void CheckBuiltInFailure(Order order)
    {
        base.CheckBuiltInFailure(order);
        // Built-in failure: throws for even order ids
        if (order.Id % 2 == 0)
        {
            throw new ShippingProviderUnavailableException($"CairoExpress unavailable for Order #{order.Id} (even order id)");
        }
    }
}

public class NileCourier : BaseShippingCarrier
{
    public override string Name => "NileCourier";
    public override decimal PriceFactor => 1.10m;
    protected override (int Min, int Max) ResponseRangeMs => (800, 1200);

    public NileCourier(IEnumerable<int>? failForOrderIds = null, JsonFileStore? store = null)
        : base(failForOrderIds, store) { }
}

