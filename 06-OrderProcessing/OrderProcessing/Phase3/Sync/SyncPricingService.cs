using OrderProcessing.Phase1.Sync;
using OrderProcessing.Phase2;
using OrderProcessing.Shared;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3.Sync;

public class SyncPricingService
{
    private readonly SyncCustomerRepository _customerRepo;
    private readonly SyncProductRepository _productRepo;
    private readonly SyncCouponRepository _couponRepo;
    private readonly JsonFileStore _store;

    public SyncPricingService(
        SyncCustomerRepository? customerRepo = null,
        SyncProductRepository? productRepo = null,
        SyncCouponRepository? couponRepo = null,
        JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
        _customerRepo = customerRepo ?? new SyncCustomerRepository(_store);
        _productRepo = productRepo ?? new SyncProductRepository(_store);
        _couponRepo = couponRepo ?? new SyncCouponRepository(_store);
    }

    public decimal CalculateTotal(Order order)
    {
        Log.Info($"SyncPricingService -> Calculating pricing for Order #{order.Id}...");

        // 1. Subtotal (DB Query)
        Log.Info($"SyncPricingService -> Reading products for Order #{order.Id}...");
        Thread.Sleep(SimulatedDelays.DbQueryOpenConnection);
        Thread.Sleep(SimulatedDelays.DbQueryExecute());

        var products = _store.ReadAll<Product>(DataFiles.Products);
        var productMap = products.ToDictionary(p => p.Id);

        decimal subTotal = 0m;
        foreach (var item in order.Items)
        {
            if (productMap.TryGetValue(item.ProductId, out var product))
            {
                item.UnitPrice = product.Price;
                subTotal += product.Price * item.Quantity;
            }
        }
        order.SubTotal = PricingRules.RoundMoney(subTotal);

        // 2. Discount (External API)
        Log.Info($"SyncPricingService -> Fetching customer discount for Order #{order.Id}...");
        Thread.Sleep(SimulatedDelays.ExternalApiDnsHandshake);
        Thread.Sleep(SimulatedDelays.ExternalApiSendRequest);
        Thread.Sleep(SimulatedDelays.ExternalApiWaitForResponse());

        var customer = _customerRepo.GetById(order.CustomerId);
        Coupon? coupon = null;
        if (!string.IsNullOrEmpty(order.CouponCode))
        {
            coupon = _couponRepo.GetByCode(order.CouponCode);
            if (coupon == null)
            {
                throw new InvalidCouponException($"Coupon '{order.CouponCode}' does not exist");
            }
            if (coupon.ExpiryDate < DateTime.Today)
            {
                throw new InvalidCouponException($"Coupon '{order.CouponCode}' has expired");
            }
        }

        int couponPercent = coupon?.DiscountPercent ?? 0;
        int totalDiscountPercent = PricingRules.CalculateDiscountPercent(customer?.LoyaltyTier ?? LoyaltyTier.Bronze, couponPercent);
        order.Discount = PricingRules.CalculateDiscount(order.SubTotal, totalDiscountPercent);

        // 3. Shipping (External API)
        Log.Info($"SyncPricingService -> Fetching shipping rates for Order #{order.Id}...");
        Thread.Sleep(SimulatedDelays.ExternalApiDnsHandshake);
        Thread.Sleep(SimulatedDelays.ExternalApiSendRequest);
        Thread.Sleep(SimulatedDelays.ExternalApiWaitForResponse());

        int totalQty = order.Items.Sum(i => i.Quantity);
        decimal totalWeight = order.Items.Sum(i =>
        {
            return productMap.TryGetValue(i.ProductId, out var p) ? p.WeightKg * i.Quantity : 0m;
        });

        order.ShippingCost = PricingRules.CalculateShippingCost(order.SubTotal, totalQty, totalWeight);
        order.TotalAmount = PricingRules.CalculateTotalAmount(order.SubTotal, order.Discount, order.ShippingCost);

        Log.Success($"SyncPricingService -> Order #{order.Id} priced: SubTotal={order.SubTotal:F2}, Discount={order.Discount:F2}, Shipping={order.ShippingCost:F2}, Total={order.TotalAmount:F2}");
        return order.TotalAmount;
    }
}
