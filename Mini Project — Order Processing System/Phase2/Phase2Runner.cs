using System.Diagnostics;
using OrderProcessing.Phase1;
using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase2;

public class Phase2Runner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        Log.Info("=== Starting Phase 2 — Concurrent Processing ===");

        // Step 1: ResetAsync() -> race demo -> ResetAsync() again -> pricing comparison on order #1 (not saved)
        Log.Info("\n[Step 1.1] Resetting Data Store for Race Demo...");
        await DataSeeder.ResetAsync(ct: ct).ConfigureAwait(false);

        await RaceConditionDemo.RunAsync(ct).ConfigureAwait(false);

        Log.Info("\n[Step 1.2] Resetting Data Store for Pricing Comparison...");
        await DataSeeder.ResetAsync(ct: ct).ConfigureAwait(false);

        var incomingRepo = new IncomingOrderRepository();
        var initialOrders = await incomingRepo.GetAllAsync(ct).ConfigureAwait(false);
        var order1 = initialOrders.First(o => o.Id == 1);

        var pricingService = new PricingService(failShippingForOrderIds: new[] { 7 });
        var orderPricer = new OrderPricer(pricingService);

        var swSeq = Stopwatch.StartNew();
        await orderPricer.PriceOrderSequentialAsync(order1, ct).ConfigureAwait(false);
        swSeq.Stop();

        var swConc = Stopwatch.StartNew();
        await orderPricer.PriceOrderAsync(order1, ct).ConfigureAwait(false);
        swConc.Stop();

        Console.WriteLine("\n===== Pricing Comparison on Order #1 =====");
        Console.WriteLine($"Sequential Pricing: {swSeq.Elapsed.TotalSeconds:F2} s");
        Console.WriteLine($"Concurrent Pricing: {swConc.Elapsed.TotalSeconds:F2} s");
        Console.WriteLine($"Speedup:            {(swSeq.Elapsed.TotalSeconds / swConc.Elapsed.TotalSeconds):F2}x faster");

        // Step 2: Validate all 20 orders concurrently and save each one
        Log.Info("\n[Step 2] Validating all 20 orders concurrently and saving each one...");
        var validator = new OrderValidator();
        var orderRepo = new OrderRepository();
        var productRepo = new ProductRepository();

        var ordersToValidate = await incomingRepo.GetAllAsync(ct).ConfigureAwait(false);
        var validationTasks = ordersToValidate.Select(async order =>
        {
            await validator.ValidateOrderAsync(order, ct).ConfigureAwait(false);
            await orderRepo.SaveAsync(order, ct).ConfigureAwait(false);
        }).ToList();

        // Await validation tasks one by one
        foreach (var vt in validationTasks)
        {
            await vt.ConfigureAwait(false);
        }

        // Step 3: Start ProcessValidatedOrderAsync for every Validated order into a List<Task<bool>>
        Log.Info("\n[Step 3] Starting ProcessValidatedOrderAsync for all Validated orders concurrently...");
        var validatedOrders = ordersToValidate.Where(o => o.Status == OrderStatus.Validated).ToList();
        var processor = new OrderProcessorV2(
            productRepository: productRepo,
            orderRepository: orderRepo,
            orderPricer: orderPricer,
            invoiceGenerator: new InvoiceGenerator());

        var processingTasks = new List<(Order Order, Task<bool> Task)>();
        var totalStopwatch = Stopwatch.StartNew();

        foreach (var order in validatedOrders)
        {
            var task = processor.ProcessValidatedOrderAsync(order, ct);
            processingTasks.Add((order, task));
        }

        // Print Status of task[0] immediately (after creation)
        if (processingTasks.Count > 0)
        {
            Console.WriteLine($"\n[Immediate] Status of task[0] (Order #{processingTasks[0].Order.Id}): {processingTasks[0].Task.Status}");
        }

        // Step 4: await Task.Delay(1000) and print the Status of every task (while running)
        Log.Info("\n[Step 4] Waiting 1000ms and checking running status of all tasks...");
        await Task.Delay(1000, ct).ConfigureAwait(false);

        Console.WriteLine("\n--- Task Statuses at 1000 ms ---");
        foreach (var (order, task) in processingTasks)
        {
            Console.WriteLine($"Order #{order.Id,-2} Task Status: {task.Status}");
        }

        // Step 5: Await each task in a foreach. A failing order must not stop the others.
        Log.Info("\n[Step 5] Awaiting each processing task...");
        foreach (var (order, task) in processingTasks)
        {
            try
            {
                bool success = await task.ConfigureAwait(false);
                if (!success)
                {
                    Log.Info($"Order #{order.Id} completed processing with result: False (Status: {order.Status})");
                }
            }
            catch (Exception ex)
            {
                // Print task status, mark Failed with error message, release stock, save it, continue
                Log.Error($"Task for Order #{order.Id} threw exception! Task Status: {task.Status}, Error: {ex.Message}");
                order.Status = OrderStatus.Failed;
                order.FailureReason = ex.Message;

                foreach (var item in order.Items)
                {
                    await productRepo.ReleaseStockAsync(item.ProductId, item.Quantity, ct).ConfigureAwait(false);
                }

                await orderRepo.SaveAsync(order, ct).ConfigureAwait(false);
            }
        }

        totalStopwatch.Stop();

        // Step 6: Print final Status of every task and the total time
        Console.WriteLine("\n--- Final Status of Processing Tasks ---");
        foreach (var (order, task) in processingTasks)
        {
            Console.WriteLine($"Order #{order.Id,-2} -> Final Task Status: {task.Status,-9} | Order Status: {order.Status,-12} | Total: {order.TotalAmount,7:F2} | Reason: {order.FailureReason ?? "None"}");
        }

        Console.WriteLine($"\nPhase 2 Total Processing Time: {totalStopwatch.Elapsed.TotalSeconds:F2} seconds");

        // Step 7: Read orders.json and confirm it has exactly 20 records (proves no lost writes)
        var allFinalOrders = await orderRepo.GetAllAsync(ct).ConfigureAwait(false);
        var completedCount = allFinalOrders.Count(o => o.Status == OrderStatus.Completed);
        var rejectedCount = allFinalOrders.Count(o => o.Status == OrderStatus.Rejected);
        var failedCount = allFinalOrders.Count(o => o.Status == OrderStatus.Failed);

        Console.WriteLine("\n===== Phase 2 Final orders.json Summary =====");
        Console.WriteLine($"Total Records in orders.json: {allFinalOrders.Count} (Expected: 20)");
        Console.WriteLine($"Completed: {completedCount} (Expected: 13)");
        Console.WriteLine($"Rejected:  {rejectedCount} (Expected: 6)");
        Console.WriteLine($"Failed:    {failedCount} (Expected: 1 - Order #7)");

        if (allFinalOrders.Count == 20 && completedCount == 13 && rejectedCount == 6 && failedCount == 1)
        {
            Log.Success("PHASE 2 BATCH PROCESSED PERFECTLY: Exactly 13 Completed, 6 Rejected, 1 Failed (#7)!");
        }
        else
        {
            Log.Error($"Discrepancy detected: Completed={completedCount}, Rejected={rejectedCount}, Failed={failedCount}, Total={allFinalOrders.Count}");
        }
    }
}
