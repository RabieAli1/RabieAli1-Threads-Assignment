using System.Text.Json;
using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase2;

public class ReportGenerator
{
    private readonly OrderRepository _orderRepository;
    private readonly ProductRepository _productRepository;

    public ReportGenerator(OrderRepository? orderRepository = null, ProductRepository? productRepository = null)
    {
        _orderRepository = orderRepository ?? new OrderRepository();
        _productRepository = productRepository ?? new ProductRepository();
    }

    /// <summary>
    /// Loads data through OrderRepository and ProductRepository (with simulated delays),
    /// computes metrics, and saves to Data/reports/report_{yyyyMMdd_HHmmss}.json.
    /// </summary>
    public async Task<Report> GenerateAsync(CancellationToken ct = default)
    {
        Log.Info("ReportGenerator -> Loading all orders from repository...");
        var orders = await _orderRepository.GetAllAsync(ct).ConfigureAwait(false);

        Log.Info("ReportGenerator -> Loading all products from repository...");
        var products = await _productRepository.GetAllAsync(ct).ConfigureAwait(false);
        var productMap = products.ToDictionary(p => p.Id);

        var completedOrders = orders.Where(o => o.Status == OrderStatus.Completed).ToList();

        // 1. Order counts
        int totalOrders = orders.Count;
        int completedCount = completedOrders.Count;
        int rejectedCount = orders.Count(o => o.Status == OrderStatus.Rejected);
        int failedCount = orders.Count(o => o.Status == OrderStatus.Failed);
        int paymentFailedCount = orders.Count(o => o.Status == OrderStatus.PaymentFailed);

        // 2. Revenue & Average Order Value
        decimal totalRevenue = PricingRules.RoundMoney(completedOrders.Sum(o => o.TotalAmount));
        decimal averageOrderValue = completedCount > 0
            ? PricingRules.RoundMoney(totalRevenue / completedCount)
            : 0m;

        // 3. Top selling product across Completed orders (ties -> lowest ProductId)
        var productQuantities = new Dictionary<int, int>();
        foreach (var order in completedOrders)
        {
            foreach (var item in order.Items)
            {
                productQuantities.TryGetValue(item.ProductId, out int current);
                productQuantities[item.ProductId] = current + item.Quantity;
            }
        }

        int topSellingId = 0;
        int topSellingQty = 0;
        string topSellingName = "None";

        if (productQuantities.Count > 0)
        {
            var topGroup = productQuantities
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .First();

            topSellingId = topGroup.Key;
            topSellingQty = topGroup.Value;
            if (productMap.TryGetValue(topSellingId, out var prod))
            {
                topSellingName = prod.Name;
            }
        }

        var report = new Report
        {
            TotalOrders = totalOrders,
            TotalRevenue = totalRevenue,
            CompletedCount = completedCount,
            RejectedCount = rejectedCount,
            FailedCount = failedCount,
            PaymentFailedCount = paymentFailedCount,
            TopSellingProduct = topSellingName,
            TopSellingQuantity = topSellingQty,
            AverageOrderValue = averageOrderValue,
            GeneratedAt = DateTime.Now
        };

        // 4. Save to Data/reports/report_{yyyyMMdd_HHmmss}.json
        Directory.CreateDirectory(DataFiles.ReportsPath);
        string reportFileName = $"report_{report.GeneratedAt:yyyyMMdd_HHmmss}.json";
        string reportFilePath = Path.Combine(DataFiles.ReportsPath, reportFileName);

        await using (var stream = new FileStream(reportFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, report, DataFiles.SerializerOptions, ct).ConfigureAwait(false);
        }

        Log.Success($"ReportGenerator -> Report saved to {reportFileName}");
        return report;
    }
}

