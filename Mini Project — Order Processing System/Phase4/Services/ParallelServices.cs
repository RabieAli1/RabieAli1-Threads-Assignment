using System.Collections.Concurrent;
using System.Diagnostics;
using OrderProcessing.Phase3;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase4.Services;

public class FraudScoringService
{
    public static int Score(int orderId)
    {
        long h = orderId;
        for (int i = 0; i < 100_000; i++)
        {
            h = (h * 31 + i) % 1_000_000_007;
        }
        return (int)(h % 100);
    }

    public static (List<int> Sequential, List<int> Parallel, double SeqSeconds, double ParSeconds, int DistinctThreads) RunBenchmark(CancellationToken ct = default)
    {
        var orderIds = Enumerable.Range(1, 10_000).ToList();

        // 1. Sequential run
        Log.Info("[Task 10.4 A] Starting sequential fraud scoring of 10,000 orders...");
        var swSeq = Stopwatch.StartNew();
        var seqFlagged = new List<int>();
        foreach (var id in orderIds)
        {
            if (ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();
            if (Score(id) >= 90)
            {
                seqFlagged.Add(id);
            }
        }
        swSeq.Stop();

        // 2. Parallel run (spread across all CPU cores, max = Environment.ProcessorCount)
        Log.Info($"[Task 10.4 A] Starting parallel fraud scoring (MaxDegreeOfParallelism = {Environment.ProcessorCount})...");
        var distinctThreads = new ConcurrentBag<int>();
        var parFlaggedBag = new ConcurrentBag<int>();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        };

        var swPar = Stopwatch.StartNew();
        Parallel.ForEach(orderIds, options, id =>
        {
            distinctThreads.Add(Environment.CurrentManagedThreadId);
            if (Score(id) >= 90)
            {
                parFlaggedBag.Add(id);
            }
        });
        swPar.Stop();

        var parFlagged = parFlaggedBag.OrderBy(x => x).ToList();

        return (seqFlagged, parFlagged, swSeq.Elapsed.TotalSeconds, swPar.Elapsed.TotalSeconds, distinctThreads.Distinct().Count());
    }
}

public class EmailResendService
{
    private readonly IEmailService _emailService;

    public EmailResendService(IEmailService? emailService = null)
    {
        _emailService = emailService ?? new EmailService();
    }

    public async Task<(int TotalSent, int MaxConcurrent, double ElapsedSeconds)> ResendEmailsAsync(CancellationToken ct = default)
    {
        var testOrders = Enumerable.Range(1001, 30).Select(id => new Order
        {
            Id = id,
            CustomerId = 1,
            TotalAmount = 100m,
            Status = OrderStatus.Completed
        }).ToList();

        var testCustomer = new Customer
        {
            Id = 1,
            Name = "Ahmed Ali",
            Email = "ahmed.ali@example.com",
            IsActive = true,
            LoyaltyTier = LoyaltyTier.Gold
        };

        int currentConcurrent = 0;
        int maxConcurrent = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 4,
            CancellationToken = ct
        };

        var sw = Stopwatch.StartNew();

        await Parallel.ForEachAsync(testOrders, parallelOptions, async (order, token) =>
        {
            int current = Interlocked.Increment(ref currentConcurrent);

            // Track peak concurrency atomically
            int initialMax;
            do
            {
                initialMax = maxConcurrent;
                if (current <= initialMax) break;
            } while (Interlocked.CompareExchange(ref maxConcurrent, current, initialMax) != initialMax);

            try
            {
                await _emailService.SendConfirmationAsync(order, testCustomer, token).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref currentConcurrent);
            }
        }).ConfigureAwait(false);

        sw.Stop();
        return (testOrders.Count, maxConcurrent, sw.Elapsed.TotalSeconds);
    }
}

