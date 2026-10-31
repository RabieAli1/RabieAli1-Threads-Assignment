using System.Diagnostics;
using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public class FireAndForgetDemo
{
    private readonly OrderRepository _orderRepository;
    private readonly IEmailService _emailService;

    public FireAndForgetDemo(OrderRepository orderRepository, IEmailService emailService)
    {
        _orderRepository = orderRepository;
        _emailService = emailService;
    }

#pragma warning disable CS4014
    public async Task ProcessOrderBrokenAsync(Order order, Customer customer)
    {
        await _orderRepository.SaveAsync(order);
        _emailService.SendConfirmationAsync(order, customer); // not awaited
        Console.WriteLine("[Broken] Order processing finished (email was not awaited!)");
    }
#pragma warning restore CS4014

    public async Task ProcessOrderFixedAsync(Order order, Customer customer)
    {
        await _orderRepository.SaveAsync(order);
        await _emailService.SendConfirmationAsync(order, customer); // properly awaited
        Console.WriteLine("[Fixed] Order processing finished (email successfully awaited)");
    }

    public static async Task RunDemoAsync(CancellationToken ct = default)
    {
        Console.WriteLine("\n========================================================");
        Console.WriteLine("=== Task 9.7: Fire-and-Forget Investigation Demo ===");
        Console.WriteLine("========================================================");

        var store = new JsonFileStore();
        var orderRepo = new OrderRepository(store);

        var testCustomer = new Customer
        {
            Id = 1,
            Name = "Ahmed Ali",
            Email = "ahmed.ali@example.com",
            IsActive = true,
            LoyaltyTier = LoyaltyTier.Gold
        };

        var testOrder = new Order
        {
            Id = 991,
            CustomerId = 1,
            Items = new List<OrderItem> { new OrderItem { ProductId = 1, Quantity = 1, UnitPrice = 1200m } },
            TotalAmount = 1200m,
            Status = OrderStatus.Completed
        };

        // Scenario 1: Normal email - broken method finishes before email finishes
        Console.WriteLine("\n--- Scenario 1: Unawaited Task (Method returns before background work completes) ---");
        var normalEmailService = new EmailService(store);
        var demoNormal = new FireAndForgetDemo(orderRepo, normalEmailService);

        var sw1 = Stopwatch.StartNew();
        await demoNormal.ProcessOrderBrokenAsync(testOrder, testCustomer);
        sw1.Stop();
        Console.WriteLine($"Caller regained control after only {sw1.ElapsedMilliseconds} ms (Email takes ~1000ms in background).");
        Console.WriteLine("Notice: If the process exits now, the background email task is abruptly killed!");

        // Wait a bit to let the background task finish before next scenario
        await Task.Delay(1200, ct);

        // Scenario 2: Unawaited Task throwing an exception (swallowed/unobserved)
        Console.WriteLine("\n--- Scenario 2: Unawaited Task throws an exception ---");
        var failingEmailService = new EmailService(store, failAttempts: new Dictionary<int, int> { [992] = int.MaxValue });
        var testOrder2 = new Order
        {
            Id = 992,
            CustomerId = 1,
            Items = new List<OrderItem> { new OrderItem { ProductId = 1, Quantity = 1, UnitPrice = 500m } },
            TotalAmount = 500m,
            Status = OrderStatus.Completed
        };
        var demoFailing = new FireAndForgetDemo(orderRepo, failingEmailService);

        try
        {
            await demoFailing.ProcessOrderBrokenAsync(testOrder2, testCustomer);
            Console.WriteLine("[Broken Call]: No exception was caught by caller! The unawaited exception was lost/unobserved.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Broken Call]: Caught exception: {ex.Message}");
        }

        // Wait for background error to show in logs
        await Task.Delay(1200, ct);

        // Scenario 3: Fixed method properly awaiting
        Console.WriteLine("\n--- Scenario 3: Fixed Method (Awaiting email service) ---");
        var testOrder3 = new Order
        {
            Id = 993,
            CustomerId = 1,
            Items = new List<OrderItem> { new OrderItem { ProductId = 1, Quantity = 1, UnitPrice = 800m } },
            TotalAmount = 800m,
            Status = OrderStatus.Completed
        };
        var demoFixed = new FireAndForgetDemo(orderRepo, normalEmailService);

        var sw3 = Stopwatch.StartNew();
        await demoFixed.ProcessOrderFixedAsync(testOrder3, testCustomer);
        sw3.Stop();
        Console.WriteLine($"[Fixed Call]: Completed reliably in {sw3.ElapsedMilliseconds} ms. Email was guaranteed sent.");

        // Scenario 4: Fixed method catching the exception
        Console.WriteLine("\n--- Scenario 4: Fixed Method with failure (Exception properly surfaced) ---");
        var demoFixedFailing = new FireAndForgetDemo(orderRepo, failingEmailService);
        try
        {
            await demoFixedFailing.ProcessOrderFixedAsync(testOrder2, testCustomer);
        }
        catch (SmtpUnavailableException ex)
        {
            Console.WriteLine($"[Fixed Call]: Caught expected exception in caller: {ex.Message}");
        }
    }
}

