using OrderProcessing.Shared.Storage;

namespace OrderProcessing;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine($"Order Processing System Initialized.");
        Console.WriteLine($"Data directory: {DataFiles.DataDirectory}");
    }
}
