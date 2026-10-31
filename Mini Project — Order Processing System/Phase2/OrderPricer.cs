using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase2;

public class OrderPricer
{
    private readonly PricingService _pricingService;

    public OrderPricer(PricingService? pricingService = null)
    {
        _pricingService = pricingService ?? new PricingService();
    }

    /// <summary>
    /// Calculates SubTotal first, then executes the discount and shipping calls concurrently.
    /// In accordance with Part 00 rules: tasks are started concurrently and awaited one by one.
    /// </summary>
    public async Task<decimal> PriceOrderAsync(Order order, CancellationToken ct = default)
    {
        Log.Info($"OrderPricer -> Pricing Order #{order.Id} (Concurrent)...");

        // 1. Calculate SubTotal first
        await _pricingService.CalculateSubTotalAsync(order, ct).ConfigureAwait(false);

        // 2. Start discount and shipping API calls at the same time
        var discountTask = _pricingService.GetDiscountAsync(order, ct);
        var shippingTask = _pricingService.CalculateShippingCostAsync(order, ct);

        // 3. Await tasks one by one
        decimal discount = await discountTask.ConfigureAwait(false);
        decimal shipping = await shippingTask.ConfigureAwait(false);

        // 4. Calculate final total
        order.TotalAmount = PricingRules.CalculateTotalAmount(order.SubTotal, discount, shipping);
        Log.Success($"OrderPricer -> Order #{order.Id} priced (Concurrent): SubTotal={order.SubTotal:F2}, Discount={order.Discount:F2}, Shipping={order.ShippingCost:F2}, Total={order.TotalAmount:F2}");
        return order.TotalAmount;
    }

    /// <summary>
    /// Sequential pricing: awaits each call immediately after starting it.
    /// Used for performance comparison against PriceOrderAsync.
    /// </summary>
    public async Task<decimal> PriceOrderSequentialAsync(Order order, CancellationToken ct = default)
    {
        Log.Info($"OrderPricer -> Pricing Order #{order.Id} (Sequential)...");

        // 1. Calculate SubTotal
        await _pricingService.CalculateSubTotalAsync(order, ct).ConfigureAwait(false);

        // 2. Start and await discount
        decimal discount = await _pricingService.GetDiscountAsync(order, ct).ConfigureAwait(false);

        // 3. Start and await shipping
        decimal shipping = await _pricingService.CalculateShippingCostAsync(order, ct).ConfigureAwait(false);

        // 4. Calculate final total
        order.TotalAmount = PricingRules.CalculateTotalAmount(order.SubTotal, discount, shipping);
        Log.Success($"OrderPricer -> Order #{order.Id} priced (Sequential): SubTotal={order.SubTotal:F2}, Discount={order.Discount:F2}, Shipping={order.ShippingCost:F2}, Total={order.TotalAmount:F2}");
        return order.TotalAmount;
    }
}
