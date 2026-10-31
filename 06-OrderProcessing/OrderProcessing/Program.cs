using OrderProcessing.Phase1;
using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing;

public class Program
{
    public static async Task Main(string[] args)
    {
        Log.Info("Order Processing System Initialized.");

        // Reset data first
        await DataSeeder.ResetAsync();

        var incomingRepo = new IncomingOrderRepository();
        var orderRepo = new OrderRepository();
        var validator = new OrderValidator();

        var orders = await incomingRepo.GetAllAsync();
        int validatedCount = 0;
        int rejectedCount = 0;

        Log.Info("\n--- Testing Task 7.2 ValidateOrderAsync on 20 Orders ---");
        foreach (var order in orders)
        {
            await validator.ValidateOrderAsync(order);
            await orderRepo.SaveAsync(order);

            if (order.Status == OrderStatus.Validated)
                validatedCount++;
            else if (order.Status == OrderStatus.Rejected)
                rejectedCount++;
        }

        Console.WriteLine("\n===== Validation Summary =====");
        Console.WriteLine($"Total Orders: {orders.Count}");
        Console.WriteLine($"Validated:    {validatedCount}");
        Console.WriteLine($"Rejected:     {rejectedCount}");

        var rejectedOrders = orders.Where(o => o.Status == OrderStatus.Rejected).ToList();
        foreach (var r in rejectedOrders)
        {
            Console.WriteLine($" - Order #{r.Id}: {r.FailureReason}");
        }
    }
}
