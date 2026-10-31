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

        // Test Task 7.1 Repositories
        Log.Info("\n--- Testing Task 7.1 Repositories ---");
        var custRepo = new CustomerRepository();
        var prodRepo = new ProductRepository();
        var couponRepo = new CouponRepository();
        var orderRepo = new OrderRepository();
        var incomingRepo = new IncomingOrderRepository();

        var customer = await custRepo.GetByIdAsync(3);
        var product = await prodRepo.GetByIdAsync(1);
        var coupon = await couponRepo.GetByCodeAsync("SAVE10");
        var incomingOrders = await incomingRepo.GetAllAsync();

        Log.Success($"Repositories verified successfully. Loaded {incomingOrders.Count} incoming orders.");
    }
}
