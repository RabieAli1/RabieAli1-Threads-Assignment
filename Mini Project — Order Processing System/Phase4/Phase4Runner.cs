using System.Diagnostics;
using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Phase2;
using OrderProcessing.Phase3;
using OrderProcessing.Phase4.Carriers;
using OrderProcessing.Phase4.Models;
using OrderProcessing.Phase4.Services;
using OrderProcessing.Shared;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase4;

public class Phase4Runner
{
    private class OrderRunResult
    {
        public Order Order { get; set; } = null!;
        public bool Success { get; set; }
        public Stopwatch Sw { get; set; } = null!;
    }

    public static async Task RunAsync(CancellationToken ct = default)
    {
        Log.Info("=== Starting Phase 4 — Scale-Up ===");

        // =========================================================================
        // Task 10.1 — Live Checkout Progress
        // =========================================================================
        Console.WriteLine("\n========================================================");
        Console.WriteLine("=== Task 10.1: Live Checkout Progress (Task.WhenEach) ===");
        Console.WriteLine("========================================================");

        Log.Info("[Task 10.1] Resetting data store from seed...");
        await DataSeeder.ResetAsync(ct: ct).ConfigureAwait(false);

        var jsonStore = new JsonFileStore();
        var customerRepo = new CustomerRepository(jsonStore);
        var productRepo = new ProductRepository(jsonStore);
        var couponRepo = new CouponRepository(jsonStore);
        var incomingRepo = new IncomingOrderRepository(jsonStore);

        // Same failure configuration as 9.2:
        var paymentGateway = new PaymentGateway(jsonStore, timeoutOrderIds: new[] { 5 }, declineOrderIds: new[] { 8 });
        var orderRepo = new OrderRepository(jsonStore, failSaveForOrderIds: new[] { 13 });
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

        var incomingOrders = await incomingRepo.GetAllAsync(ct).ConfigureAwait(false);
        Log.Info($"[Task 10.1] Launching all {incomingOrders.Count} orders concurrently...");

        var tasks = incomingOrders.Select(order =>
        {
            return Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                bool ok = await orderProcessor.ProcessOrderAsync(order, ct).ConfigureAwait(false);
                sw.Stop();
                return new OrderRunResult { Order = order, Success = ok, Sw = sw };
            }, ct);
        }).ToList();

        // Stream completions in real time using Task.WhenEach (no list rebuilding!)
        int finishedCount = 0;
        await foreach (var completedTask in Task.WhenEach(tasks))
        {
            finishedCount++;
            var result = await completedTask.ConfigureAwait(false);
            var o = result.Order;
            string reason = !string.IsNullOrEmpty(o.FailureReason) ? $" ({o.FailureReason})" : "";
            Console.WriteLine($"[ {finishedCount,2}/20] Order #{o.Id,-2} -> {o.Status}{reason} after {result.Sw.Elapsed.TotalSeconds:F1} s");
        }

        // Print final count per status (must match 9.4)
        var allProcessed = await orderRepo.GetAllAsync(ct).ConfigureAwait(false);
        int completed = allProcessed.Count(o => o.Status == OrderStatus.Completed);
        int rejected = allProcessed.Count(o => o.Status == OrderStatus.Rejected);
        int paymentFailed = allProcessed.Count(o => o.Status == OrderStatus.PaymentFailed);
        int failed = allProcessed.Count(o => o.Status == OrderStatus.Failed);

        Console.WriteLine("\nTask 10.1 Final Counts per Status:");
        Console.WriteLine($"Completed: {completed}, Rejected: {rejected}, PaymentFailed: {paymentFailed}, Failed: {failed} (Total: {allProcessed.Count})");

        // =========================================================================
        // Task 10.2 — Customer Dashboard (Task.WhenAll)
        // =========================================================================
        Console.WriteLine("\n========================================================");
        Console.WriteLine("=== Task 10.2: Customer Dashboard (Task.WhenAll) ===");
        Console.WriteLine("========================================================");

        var dashboardService = new CustomerDashboardService(failPaymentsForCustomerIds: new[] { 99 }, store: jsonStore);

        // Run for Customer #3
        Console.WriteLine("\n--- Customer #3 Dashboard ---");
        try
        {
            var d3 = await dashboardService.LoadAsync(3, ct).ConfigureAwait(false);
            Console.WriteLine($"Dashboard: {d3.Customer.Name} — {d3.Orders.Count} orders ({string.Join(", ", d3.Orders.Select(o => $"#{o.Id} {o.Status}"))}), {d3.Payments.Count} payments ({string.Join(", ", d3.Payments.Select(p => $"#{p.OrderId} {p.Status}"))}), {d3.Emails.Count} email (#{string.Join(", #", d3.Emails.Select(e => e.OrderId))})");
        }
        catch (Exception ex)
        {
            Log.Error($"Unexpected error loading Customer #3: {ex.Message}");
        }

        // Run for Customer #99 (Expect both Customer #99 not found AND Payments store timeout)
        Console.WriteLine("\n--- Customer #99 Dashboard (Multiple Failures Aggregated) ---");
        try
        {
            await dashboardService.LoadAsync(99, ct).ConfigureAwait(false);
        }
        catch (AggregateException ex)
        {
            Console.WriteLine("Reported failures from Task.WhenAll:");
            foreach (var inner in ex.InnerExceptions)
            {
                Console.WriteLine($" - {inner.GetType().Name}: {inner.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Reported failure: {ex.GetType().Name}: {ex.Message}");
        }

        // =========================================================================
        // Task 10.3 — Fastest Shipping Quote (Task.WhenAny)
        // =========================================================================
        Console.WriteLine("\n========================================================");
        Console.WriteLine("=== Task 10.3: Fastest Shipping Quote (Task.WhenAny) ===");
        Console.WriteLine("========================================================");

        var shippingService = new ShippingQuoteService();

        for (int orderId = 1; orderId <= 5; orderId++)
        {
            var targetOrder = incomingOrders.FirstOrDefault(o => o.Id == orderId) ?? new Order { Id = orderId, SubTotal = 500m };
            try
            {
                var quote = await shippingService.GetFastestQuoteAsync(targetOrder, ct).ConfigureAwait(false);
                Console.WriteLine($"Order #{targetOrder.Id} -> Winner: {quote.Carrier,-12} | Price: ${quote.Price,6:F2} | Time: {quote.ElapsedMs,4} ms");
            }
            catch (Exception ex)
            {
                Log.Error($"Order #{targetOrder.Id} quote failed: {ex.Message}");
            }
        }

        // Demonstration of R5: All carriers fail for Order #6
        Console.WriteLine("\n--- Task 10.3 R5: All Carriers Failing Scenario (Order #6) ---");
        var failingCarriers = new List<IShippingCarrier>
        {
            new FastShip(failForOrderIds: new[] { 6 }),
            new CairoExpress(failForOrderIds: new[] { 6 }),
            new NileCourier(failForOrderIds: new[] { 6 })
        };
        var failingQuoteService = new ShippingQuoteService(failingCarriers);
        var order6 = incomingOrders.FirstOrDefault(o => o.Id == 6) ?? new Order { Id = 6 };
        try
        {
            await failingQuoteService.GetFastestQuoteAsync(order6, ct).ConfigureAwait(false);
        }
        catch (ShippingProviderUnavailableException ex)
        {
            Console.WriteLine($"Expected exception caught: {ex.Message}");
        }

        // =========================================================================
        // Task 10.4 — Fraud Scoring & Email Re-send
        // =========================================================================
        Console.WriteLine("\n========================================================");
        Console.WriteLine("=== Task 10.4 A: CPU Fraud Scoring (Parallel vs Sequential) ===");
        Console.WriteLine("========================================================");

        var (seqFlagged, parFlagged, seqSec, parSec, distinctThreads) = FraudScoringService.RunBenchmark(ct);
        double speedup = seqSec / (parSec > 0 ? parSec : 0.001);

        Console.WriteLine($"Sequential Time:       {seqSec:F3} s (Flagged: {seqFlagged.Count})");
        Console.WriteLine($"Parallel Time:         {parSec:F3} s (Flagged: {parFlagged.Count})");
        Console.WriteLine($"Speed-up:              {speedup:F2}x");
        Console.WriteLine($"Distinct Threads Used: {distinctThreads}");
        Console.WriteLine($"ProcessorCount:        {Environment.ProcessorCount}");
        Console.WriteLine($"First 5 Flagged IDs:   {string.Join(", ", parFlagged.Take(5))}");

        Console.WriteLine("\n========================================================");
        Console.WriteLine("=== Task 10.4 B: Email Re-Send (Parallel.ForEachAsync) ===");
        Console.WriteLine("========================================================");

        var resendService = new EmailResendService();
        var (totalSent, maxConcurrent, totalTime) = await resendService.ResendEmailsAsync(ct).ConfigureAwait(false);

        Console.WriteLine($"Total Emails Sent:     {totalSent}");
        Console.WriteLine($"Max Concurrent Sends:  {maxConcurrent} (Required: exactly 4)");
        Console.WriteLine($"Total Duration:        {totalTime:F2} s (Expected ~8 s)");

        var finalEmails = await jsonStore.ReadAllAsync<EmailMessage>(DataFiles.Emails, ct).ConfigureAwait(false);
        var resendEmails = finalEmails.Where(e => e.OrderId >= 1001 && e.OrderId <= 1030).ToList();
        Console.WriteLine($"Saved in emails.json:  {resendEmails.Count} records (IDs 1001–1030)");
    }
}

