using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Shared;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase2;

public class PricingService
{
    private readonly JsonFileStore _store;
    private readonly CustomerRepository _customerRepository;
    private readonly ProductRepository _productRepository;
    private readonly CouponRepository _couponRepository;
    private readonly HashSet<int> _failShippingForOrderIds;

    public PricingService(
        CustomerRepository? customerRepository = null,
        ProductRepository? productRepository = null,
        CouponRepository? couponRepository = null,
        JsonFileStore? store = null,
        IEnumerable<int>? failShippingForOrderIds = null)
    {
        _store = store ?? new JsonFileStore();
        _customerRepository = customerRepository ?? new CustomerRepository(_store);
        _productRepository = productRepository ?? new ProductRepository(_store);
        _couponRepository = couponRepository ?? new CouponRepository(_store);
        _failShippingForOrderIds = failShippingForOrderIds != null ? new HashSet<int>(failShippingForOrderIds) : new HashSet<int>();
    }

    /// <summary>
    /// Simulated as DB query: reads products.json, fills UnitPrice for each item, returns SubTotal.
    /// </summary>
    public async Task<decimal> CalculateSubTotalAsync(Order order, CancellationToken ct = default)
    {
        Log.Info($"PricingService -> [DB Query] Opening connection for Order #{order.Id} SubTotal...");
        await Task.Delay(SimulatedDelays.DbQueryOpenConnection, ct).ConfigureAwait(false);

        Log.Info($"PricingService -> [DB Query] Executing query for Order #{order.Id} products...");
        await Task.Delay(SimulatedDelays.DbQueryExecute(), ct).ConfigureAwait(false);

        var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
        var productMap = products.ToDictionary(p => p.Id);

        decimal subTotal = 0m;
        foreach (var item in order.Items)
        {
            if (productMap.TryGetValue(item.ProductId, out var product))
            {
                item.UnitPrice = product.Price;
                subTotal += item.UnitPrice * item.Quantity;
            }
        }

        order.SubTotal = PricingRules.RoundMoney(subTotal);
        Log.Info($"PricingService -> [DB Query] Order #{order.Id} SubTotal calculated: {order.SubTotal:F2}");
        return order.SubTotal;
    }

    /// <summary>
    /// Simulated as Discount API: loads customer and coupon via repositories, returns Discount.
    /// </summary>
    public async Task<decimal> GetDiscountAsync(Order order, CancellationToken ct = default)
    {
        Log.Info($"PricingService -> [Discount API] DNS/handshake for Order #{order.Id}...");
        await Task.Delay(SimulatedDelays.ExternalApiDnsHandshake, ct).ConfigureAwait(false);

        Log.Info($"PricingService -> [Discount API] Sending request for Order #{order.Id}...");
        await Task.Delay(SimulatedDelays.ExternalApiSendRequest, ct).ConfigureAwait(false);

        Log.Info($"PricingService -> [Discount API] Waiting for response for Order #{order.Id}...");
        await Task.Delay(SimulatedDelays.ExternalApiWaitForResponse(), ct).ConfigureAwait(false);

        var customer = await _customerRepository.GetByIdAsync(order.CustomerId, ct).ConfigureAwait(false);
        Coupon? coupon = null;
        if (!string.IsNullOrWhiteSpace(order.CouponCode))
        {
            coupon = await _couponRepository.GetByCodeAsync(order.CouponCode, ct).ConfigureAwait(false);
        }

        var tier = customer?.LoyaltyTier ?? LoyaltyTier.Bronze;
        int couponPercent = coupon?.DiscountPercent ?? 0;
        int totalDiscountPercent = PricingRules.CalculateDiscountPercent(tier, couponPercent);

        order.Discount = PricingRules.CalculateDiscount(order.SubTotal, totalDiscountPercent);
        Log.Info($"PricingService -> [Discount API] Order #{order.Id} Discount calculated: {order.Discount:F2} ({totalDiscountPercent}%)");
        return order.Discount;
    }

    /// <summary>
    /// Simulated as Shipping API: reads products.json for weights, returns ShippingCost.
    /// Throws ShippingProviderUnavailableException right after the handshake step for configured order IDs.
    /// </summary>
    public async Task<decimal> CalculateShippingCostAsync(Order order, CancellationToken ct = default)
    {
        Log.Info($"PricingService -> [Shipping API] DNS/handshake for Order #{order.Id}...");
        await Task.Delay(SimulatedDelays.ExternalApiDnsHandshake, ct).ConfigureAwait(false);

        if (_failShippingForOrderIds.Contains(order.Id))
        {
            Log.Error($"PricingService -> [Shipping API] Provider unavailable for Order #{order.Id}");
            throw new ShippingProviderUnavailableException("Shipping provider unavailable");
        }

        Log.Info($"PricingService -> [Shipping API] Sending request for Order #{order.Id}...");
        await Task.Delay(SimulatedDelays.ExternalApiSendRequest, ct).ConfigureAwait(false);

        Log.Info($"PricingService -> [Shipping API] Waiting for response for Order #{order.Id}...");
        await Task.Delay(SimulatedDelays.ExternalApiWaitForResponse(), ct).ConfigureAwait(false);

        var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
        var productMap = products.ToDictionary(p => p.Id);

        int totalQuantity = order.Items.Sum(i => i.Quantity);
        decimal totalWeightKg = 0m;
        foreach (var item in order.Items)
        {
            if (productMap.TryGetValue(item.ProductId, out var product))
            {
                totalWeightKg += product.WeightKg * item.Quantity;
            }
        }

        order.ShippingCost = PricingRules.CalculateShippingCost(order.SubTotal, totalQuantity, totalWeightKg);
        Log.Info($"PricingService -> [Shipping API] Order #{order.Id} ShippingCost calculated: {order.ShippingCost:F2} (Weight: {totalWeightKg:F1}kg, Qty: {totalQuantity})");
        return order.ShippingCost;
    }
}
