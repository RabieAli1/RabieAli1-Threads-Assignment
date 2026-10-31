using System.Diagnostics;
using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Phase1.Sync;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public class Phase1Runner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        Log.Info("=== Starting Phase 1 — Async Data Access Layer ===");

        // 1. Reset
        Log.Info("[Phase 1] Resetting data store...");
        await DataSeeder.ResetAsync(ct: ct).ConfigureAwait(false);

        // 2. Load the 20 orders for sync run
        var incomingRepo = new IncomingOrderRepository();
        var syncOrders = await incomingRepo.GetAllAsync(ct).ConfigureAwait(false);

        // 3. Sync run (validate all 20 one by one, Stopwatch, results not saved)
        Log.Info("\n[Phase 1] Starting Synchronous Run (sequential, results not saved)...");
        var syncValidator = new SyncOrderValidator();
        var syncStopwatch = Stopwatch.StartNew();

        foreach (var order in syncOrders)
        {
            ct.ThrowIfCancellationRequested();
            syncValidator.ValidateOrder(order);
            // results not saved
        }

        syncStopwatch.Stop();
        Log.Info($"[Phase 1] Synchronous run completed in {syncStopwatch.Elapsed.TotalSeconds:F1} seconds.");

        // 4. Reset again
        Log.Info("\n[Phase 1] Resetting data store before async run...");
        await DataSeeder.ResetAsync(ct: ct).ConfigureAwait(false);

        // 5. Async run (foreach -> await ValidateOrderAsync -> await SaveAsync, Stopwatch)
        var asyncOrders = await incomingRepo.GetAllAsync(ct).ConfigureAwait(false);
        var asyncValidator = new OrderValidator();
        var orderRepo = new OrderRepository();

        Log.Info("\n[Phase 1] Starting Asynchronous Run (sequential with awaits, results saved)...");
        var asyncStopwatch = Stopwatch.StartNew();

        foreach (var order in asyncOrders)
        {
            ct.ThrowIfCancellationRequested();
            await asyncValidator.ValidateOrderAsync(order, ct).ConfigureAwait(false);
            await orderRepo.SaveAsync(order, ct).ConfigureAwait(false);
        }

        asyncStopwatch.Stop();
        Log.Info($"[Phase 1] Asynchronous run completed in {asyncStopwatch.Elapsed.TotalSeconds:F1} seconds.");

        // 6. Print summary, reading counts back from orders.json (GetByStatusAsync), not from memory
        var validatedOrders = await orderRepo.GetByStatusAsync(OrderStatus.Validated, ct).ConfigureAwait(false);
        var rejectedOrders = await orderRepo.GetByStatusAsync(OrderStatus.Rejected, ct).ConfigureAwait(false);
        var allSavedOrders = await orderRepo.GetAllAsync(ct).ConfigureAwait(false);

        Console.WriteLine("\n===== Validation Summary =====");
        Console.WriteLine($"Total Orders: {allSavedOrders.Count}");
        Console.WriteLine($"Validated:    {validatedOrders.Count}");
        Console.WriteLine($"Rejected:     {rejectedOrders.Count}");

        foreach (var r in rejectedOrders.OrderBy(o => o.Id))
        {
            Console.WriteLine($" - Order #{r.Id}: {r.FailureReason}");
        }

        Console.WriteLine($"Sync Time:  {syncStopwatch.Elapsed.TotalSeconds:F1} seconds");
        Console.WriteLine($"Async Time: {asyncStopwatch.Elapsed.TotalSeconds:F1} seconds");
        Console.WriteLine($"orders.json now contains {allSavedOrders.Count} records.");
    }
}
