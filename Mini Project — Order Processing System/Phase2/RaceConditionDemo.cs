using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Shared.Logging;

namespace OrderProcessing.Phase2;

public class RaceConditionDemo
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        Log.Info("\n========== Task 8.3: Race Condition Demo on Mechanical Keyboard (P4) ==========");
        var productRepo = new ProductRepository();

        // ---------------- Part 1: Unsafe Reservation Demo ----------------
        Log.Info("\n--- Part 1: Unsafe Reservation (No Protection) ---");
        Log.Info("Setting Mechanical Keyboard (P4) stock to 1 in products.json...");
        await productRepo.SetStockAsync(4, 1, ct).ConfigureAwait(false);

        Log.Info("Starting concurrent unsafe reservations for Order #10 and Order #11...");
        var unsafeTask10 = productRepo.ReserveStockUnsafeAsync(4, 1, ct);
        var unsafeTask11 = productRepo.ReserveStockUnsafeAsync(4, 1, ct);

        bool unsafeResult10 = await unsafeTask10.ConfigureAwait(false);
        bool unsafeResult11 = await unsafeTask11.ConfigureAwait(false);

        var unsafeP4 = await productRepo.GetByIdAsync(4, ct).ConfigureAwait(false);

        Console.WriteLine("\n----- Unsafe Reservation Results -----");
        Console.WriteLine($"Order #10 Reservation: {unsafeResult10}");
        Console.WriteLine($"Order #11 Reservation: {unsafeResult11}");
        Console.WriteLine($"Final P4 Stock on Disk: {unsafeP4?.StockQuantity ?? -1}");

        if (unsafeResult10 && unsafeResult11)
        {
            Log.Error("RACE CONDITION DEMONSTRATED: Both orders #10 and #11 succeeded! 2 keyboards sold from 1 in stock.");
        }

        // ---------------- Part 2: Safe Reservation Demo ----------------
        Log.Info("\n--- Part 2: Safe Reservation (Protected by SemaphoreSlim) ---");
        Log.Info("Resetting Mechanical Keyboard (P4) stock to 1 in products.json...");
        await productRepo.SetStockAsync(4, 1, ct).ConfigureAwait(false);

        Log.Info("Starting concurrent safe reservations for Order #10 and Order #11...");
        var safeTask10 = productRepo.ReserveStockAsync(4, 1, ct);
        var safeTask11 = productRepo.ReserveStockAsync(4, 1, ct);

        bool safeResult10 = await safeTask10.ConfigureAwait(false);
        bool safeResult11 = await safeTask11.ConfigureAwait(false);

        var safeP4 = await productRepo.GetByIdAsync(4, ct).ConfigureAwait(false);

        Console.WriteLine("\n----- Safe Reservation Results -----");
        Console.WriteLine($"Order #10 Reservation: {safeResult10}");
        Console.WriteLine($"Order #11 Reservation: {safeResult11}");
        Console.WriteLine($"Final P4 Stock on Disk: {safeP4?.StockQuantity ?? -1}");

        if ((safeResult10 && !safeResult11) || (!safeResult10 && safeResult11))
        {
            Log.Success("RACE CONDITION PREVENTED: Exactly one order succeeded (True), one rejected (False), and stock is 0.");
        }
    }
}
