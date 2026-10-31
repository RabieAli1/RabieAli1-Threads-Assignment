using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing;

public class Program
{
    public static async Task Main(string[] args)
    {
        Log.Info("Order Processing System Initialized.");
        Log.Info($"Data directory: {DataFiles.DataDirectory}");

        Log.Info("CustomerRepository -> Opening connection...");
        Log.Info("CustomerRepository -> Executing query for Customer #3...");
        Log.Success("CustomerRepository -> Customer #3 found (Omar Hassan)");
        Log.Error("CustomerRepository -> Connection timeout");

        Console.WriteLine("\nTesting DataSeeder.ResetAsync()...");
        await DataSeeder.ResetAsync();
        Log.Success("Data seeded successfully.");
    }
}
