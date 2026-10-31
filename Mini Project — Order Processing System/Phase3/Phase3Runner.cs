using System.Collections.Concurrent;
using System.Diagnostics;
using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Phase2;
using OrderProcessing.Phase3.Sync;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public class Phase3Runner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        Log.Info("=== Starting Phase 3 — Full Checkout Workflow ===");

        // 1. ResetAsync()
        Log.Info("[Phase 3] Resetting Data Store from seed...");
        await DataSeeder.ResetAsync(ct: ct).ConfigureAwait(false);

        // 2. Setup services with Task 9.2 Controlled Failures
        var jsonStore = new JsonFileStore();
        var customerRepo = new CustomerRepository(jsonStore);
        var productRepo = new ProductRepository(jsonStore);
        var couponRepo = new CouponRepository(jsonStore);
        var incomingRepo = new IncomingOrderRepository(jsonStore);

        // Controlled failures:
        // Order #5: timeout (Capture 5000ms)
        // Order #8: decline
        var paymentGateway = new PaymentGateway(jsonStore, timeoutOrderIds: new[] { 5 }, declineOrderIds: new[] { 8 });

        // Order #13: DB save fails after payment when Status = Completed
        var orderRepo = new OrderRepository(jsonStore, failSaveForOrderIds: new[] { 13 });

        // Order #3: email fails twice (attempt 3 succeeds)
        // Order #17: email always fails
        var emailService = new EmailService(jsonStore, failAttempts: new Dictionary<int, int> { [3] = 2, [17] = int.MaxValue });

        var customerService = new CustomerService(customerRepo, jsonStore);
        var inventoryService = new InventoryService(jsonStore);
        var pricingService = new PricingService(customerRepo, productRepo, couponRepo, jsonStore, failShippingForOrderIds: Array.Empty<int>());

        var orderProcessor = new OrderProcessor(
            customerService: customerService,
            inventoryService: inventoryService,
            pricingService: pricingService,
            paymentGateway: paymentGateway,
            orderRepository: orderRepo,
            emailService: emailService,
            productRepository: productRepo,
            store: jsonStore);

        // 3. Load all 20 incoming orders
        var incomingOrders = await incomingRepo.GetAllAsync(ct).ConfigureAwait(false);
        Log.Info($"[Phase 3] Loaded {incomingOrders.Count} incoming orders. Starting concurrent processing...");

        var sw = Stopwatch.StartNew();

        // 4. Start every ProcessOrderAsync into a List<Task<bool>>
        var tasks = new List<(Order Order, Task<bool> Task)>();
        foreach (var order in incomingOrders)
        {
            var task = orderProcessor.ProcessOrderAsync(order, ct);
            tasks.Add((order, task));
        }

        // 5. foreach + await
        foreach (var (order, task) in tasks)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error($"Unexpected uncaught exception for Order #{order.Id}: {ex.Message}");
            }
        }

        sw.Stop();
        Log.Info($"\n[Phase 3] All orders completed processing in {sw.Elapsed.TotalSeconds:F2} seconds.");

        if (ct.IsCancellationRequested)
        {
            var ordersAfterCancel = await orderRepo.GetAllAsync(CancellationToken.None).ConfigureAwait(false);
            int finishedCount = ordersAfterCancel.Count(o => o.Status != OrderStatus.Pending);
            int cancelledCount = ordersAfterCancel.Count(o => o.Status == OrderStatus.Pending && o.FailureReason == "Cancelled");
            Console.WriteLine($"\nRun cancelled — {finishedCount} orders finished, {cancelledCount} cancelled.");
            return;
        }

        // 6. Print final table (Order, Status, Total, Reason)
        var finalOrders = await orderRepo.GetAllAsync(ct).ConfigureAwait(false);
        var finalPayments = await jsonStore.ReadAllAsync<PaymentTransaction>(DataFiles.Payments, ct).ConfigureAwait(false);
        var finalEmails = await jsonStore.ReadAllAsync<EmailMessage>(DataFiles.Emails, ct).ConfigureAwait(false);
        var finalEmailRetries = await jsonStore.ReadAllAsync<EmailMessage>(DataFiles.EmailRetry, ct).ConfigureAwait(false);
        var finalProducts = await jsonStore.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);

        Console.WriteLine("\n================================= PHASE 3 CHECKOUT RESULTS =================================");
        Console.WriteLine(string.Format("{0,-8} | {1,-15} | {2,10} | {3,-45}", "Order", "Status", "Total", "Reason"));
        Console.WriteLine(new string('-', 85));

        foreach (var o in finalOrders.OrderBy(x => x.Id))
        {
            Console.WriteLine(string.Format("#{0,-7} | {1,-15} | {2,10:F2} | {3,-45}", o.Id, o.Status, o.TotalAmount, o.FailureReason ?? "—"));
        }
        Console.WriteLine(new string('=', 85));

        // 7. Verify Task 9.4 expected counts
        int completed = finalOrders.Count(o => o.Status == OrderStatus.Completed);
        int rejected = finalOrders.Count(o => o.Status == OrderStatus.Rejected);
        int paymentFailed = finalOrders.Count(o => o.Status == OrderStatus.PaymentFailed);
        int failed = finalOrders.Count(o => o.Status == OrderStatus.Failed);

        int captured = finalPayments.Count(p => p.Status == PaymentStatus.Captured);
        int declined = finalPayments.Count(p => p.Status == PaymentStatus.Declined);
        int refunded = finalPayments.Count(p => p.Status == PaymentStatus.Refunded);

        var p4 = finalProducts.FirstOrDefault(p => p.Id == 4);

        Console.WriteLine("\n===== Task 9.4 Verification Summary =====");
        Console.WriteLine($"orders.json:       {finalOrders.Count} records (Completed: {completed}, Rejected: {rejected}, PaymentFailed: {paymentFailed}, Failed: {failed})");
        Console.WriteLine($"payments.json:     {finalPayments.Count} records (Captured: {captured}, Declined: {declined}, Refunded: {refunded})");
        Console.WriteLine($"emails.json:       {finalEmails.Count} records");
        Console.WriteLine($"email_retry.json:  {finalEmailRetries.Count} records");
        Console.WriteLine($"P4 Keyboard Stock: {p4?.StockQuantity ?? -1}");

        if (finalOrders.Count == 20 && completed == 11 && rejected == 6 && paymentFailed == 2 && failed == 1 &&
            finalPayments.Count == 13 && captured == 11 && declined == 1 && refunded == 1 &&
            finalEmails.Count == 10 && finalEmailRetries.Count == 1 && p4?.StockQuantity == 0)
        {
            Log.Success("TASK 9.4 EXPECTED STATE VERIFIED 100% PERFECTLY!");
        }
        else
        {
            Log.Error("Mismatch with Task 9.4 expected end state!");
        }

        // 8. Task 9.6: Sync vs Async comparison
        await RunSyncVsAsyncComparisonAsync(ct).ConfigureAwait(false);

        // 9. Task 9.7: Fire-and-forget investigation
        await FireAndForgetDemo.RunDemoAsync(ct).ConfigureAwait(false);
    }

    private static async Task RunSyncVsAsyncComparisonAsync(CancellationToken ct)
    {
        Console.WriteLine("\n========================================================");
        Console.WriteLine("=== Task 9.6: Sync vs Async Comparison (Orders #1–#10) ===");
        Console.WriteLine("========================================================");

        var jsonStore = new JsonFileStore();
        var incomingRepo = new IncomingOrderRepository(jsonStore);
        var orderRepo = new OrderRepository(jsonStore);

        // --- Part 1: Sync Version ---
        Log.Info("[Task 9.6] Resetting data store for Sync run...");
        await DataSeeder.ResetAsync(ct: ct).ConfigureAwait(false);

        var allIncoming = await incomingRepo.GetAllAsync(ct).ConfigureAwait(false);
        var syncOrders = allIncoming.Where(o => o.Id >= 1 && o.Id <= 10).ToList();

        var syncProcessor = new SyncOrderProcessor(jsonStore);
        var syncDistinctThreads = new ConcurrentBag<int>();
        var syncThreads = new List<Thread>();

        int syncThreadPoolBefore = ThreadPool.ThreadCount;
        var syncSw = Stopwatch.StartNew();

        foreach (var order in syncOrders)
        {
            var t = new Thread(() =>
            {
                syncDistinctThreads.Add(Environment.CurrentManagedThreadId);
                syncProcessor.ProcessOrder(order);
            });
            syncThreads.Add(t);
            t.Start();
        }

        foreach (var t in syncThreads)
        {
            t.Join();
        }

        syncSw.Stop();
        int syncThreadPoolAfter = ThreadPool.ThreadCount;

        var syncFinalOrders = (await orderRepo.GetAllAsync(ct).ConfigureAwait(false)).Where(o => o.Id >= 1 && o.Id <= 10).ToList();
        int syncCompleted = syncFinalOrders.Count(o => o.Status == OrderStatus.Completed);
        int syncRejected = syncFinalOrders.Count(o => o.Status == OrderStatus.Rejected);

        // --- Part 2: Async Version ---
        Log.Info("[Task 9.6] Resetting data store for Async run...");
        await DataSeeder.ResetAsync(ct: ct).ConfigureAwait(false);

        var asyncOrders = (await incomingRepo.GetAllAsync(ct).ConfigureAwait(false)).Where(o => o.Id >= 1 && o.Id <= 10).ToList();
        var asyncProcessor = new OrderProcessor(store: jsonStore, disableFailures: true);
        var asyncDistinctThreads = new ConcurrentBag<int>();

        int asyncThreadPoolBefore = ThreadPool.ThreadCount;
        var asyncSw = Stopwatch.StartNew();

        var asyncTasks = new List<Task<bool>>();
        foreach (var order in asyncOrders)
        {
            asyncTasks.Add(Task.Run(async () =>
            {
                asyncDistinctThreads.Add(Environment.CurrentManagedThreadId);
                return await asyncProcessor.ProcessOrderAsync(order, ct).ConfigureAwait(false);
            }, ct));
        }

        foreach (var task in asyncTasks)
        {
            await task.ConfigureAwait(false);
        }

        asyncSw.Stop();
        int asyncThreadPoolAfter = ThreadPool.ThreadCount;

        var asyncFinalOrders = (await orderRepo.GetAllAsync(ct).ConfigureAwait(false)).Where(o => o.Id >= 1 && o.Id <= 10).ToList();
        int asyncCompleted = asyncFinalOrders.Count(o => o.Status == OrderStatus.Completed);
        int asyncRejected = asyncFinalOrders.Count(o => o.Status == OrderStatus.Rejected);

        Console.WriteLine("\n===== Task 9.6: Sync vs Async Comparison Results =====");
        Console.WriteLine(string.Format("{0,-20} | {1,-12} | {2,-12} | {3,-15} | {4,-18} | {5,-15}", "Version", "Completed", "Rejected", "Duration", "Distinct Threads", "ThreadPool Count"));
        Console.WriteLine(new string('-', 100));
        Console.WriteLine(string.Format("{0,-20} | {1,-12} | {2,-12} | {3,-15} | {4,-18} | {5,-15}",
            "Sync (10 Threads)", $"{syncCompleted}/10", $"{syncRejected}/10", $"{syncSw.Elapsed.TotalSeconds:F2}s", $"{syncDistinctThreads.Distinct().Count()}", $"{syncThreadPoolAfter}"));
        Console.WriteLine(string.Format("{0,-20} | {1,-12} | {2,-12} | {3,-15} | {4,-18} | {5,-15}",
            "Async (10 Tasks)", $"{asyncCompleted}/10", $"{asyncRejected}/10", $"{asyncSw.Elapsed.TotalSeconds:F2}s", $"{asyncDistinctThreads.Distinct().Count()}", $"{asyncThreadPoolAfter}"));
        Console.WriteLine(new string('=', 100));
    }
}
