using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase2;

public class InvoiceGenerator
{
    /// <summary>
    /// Simulates rendering an invoice PDF using a CPU-bound loop of 20,000,000 iterations inside Task.Run(),
    /// then writes Data/invoices/invoice_{orderId}.txt.
    /// Fulfills Task 8.5 requirement.
    /// </summary>
    public async Task GenerateAsync(Order order, CancellationToken ct = default)
    {
        Log.Info($"InvoiceGenerator -> Rendering invoice PDF for Order #{order.Id} (CPU-bound Task.Run 20M iterations)...");

        // CPU-bound rendering simulated on a ThreadPool worker thread
        await Task.Run(() =>
        {
            long hash = order.Id;
            for (int i = 0; i < 20_000_000; i++)
            {
                ct.ThrowIfCancellationRequested();
                hash = (hash * 31 + i) % 1_000_000_007;
            }
        }, ct).ConfigureAwait(false);

        Directory.CreateDirectory(DataFiles.InvoicesPath);
        string invoiceFilePath = Path.Combine(DataFiles.InvoicesPath, $"invoice_{order.Id}.txt");

        string content = $"========================================\n" +
                         $"             INVOICE                    \n" +
                         $"========================================\n" +
                         $"Order ID:     #{order.Id}\n" +
                         $"Date:         {order.CreatedAt:yyyy-MM-dd HH:mm:ss}\n" +
                         $"Customer ID:  #{order.CustomerId}\n" +
                         $"Coupon:       {order.CouponCode ?? "None"}\n" +
                         $"----------------------------------------\n" +
                         $"SubTotal:     {order.SubTotal:F2}\n" +
                         $"Discount:     {order.Discount:F2}\n" +
                         $"Shipping:     {order.ShippingCost:F2}\n" +
                         $"Total Amount: {order.TotalAmount:F2}\n" +
                         $"Status:       {order.Status}\n" +
                         $"========================================\n";

        await File.WriteAllTextAsync(invoiceFilePath, content, ct).ConfigureAwait(false);
        Log.Success($"InvoiceGenerator -> Invoice saved to {Path.GetFileName(invoiceFilePath)}");
    }
}
