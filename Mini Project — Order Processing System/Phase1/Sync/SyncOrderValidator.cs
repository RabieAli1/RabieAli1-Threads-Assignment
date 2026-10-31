using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase1.Sync;

public class SyncOrderValidator
{
    private readonly SyncCustomerRepository _customerRepository;
    private readonly SyncProductRepository _productRepository;
    private readonly SyncCouponRepository _couponRepository;

    public SyncOrderValidator(
        SyncCustomerRepository? customerRepository = null,
        SyncProductRepository? productRepository = null,
        SyncCouponRepository? couponRepository = null)
    {
        _customerRepository = customerRepository ?? new SyncCustomerRepository();
        _productRepository = productRepository ?? new SyncProductRepository();
        _couponRepository = couponRepository ?? new SyncCouponRepository();
    }

    /// <summary>
    /// Validates an order synchronously according to the 6 sequential checks:
    /// 1. Customer exists
    /// 2. Customer is active
    /// 3. Every product exists
    /// 4. StockQuantity >= Quantity (checked, not reserved)
    /// 5. Coupon (if not null) exists
    /// 6. Coupon ExpiryDate >= DateTime.Today
    /// Stops at the first failure.
    /// </summary>
    public bool ValidateOrder(Order order)
    {
        Log.Info($"SyncOrderValidator -> Validating Order #{order.Id}...");

        // 1. Customer exists
        var customer = _customerRepository.GetById(order.CustomerId);
        if (customer == null)
        {
            order.Status = OrderStatus.Rejected;
            order.FailureReason = $"Customer #{order.CustomerId} not found";
            Log.Info($"SyncOrderValidator -> Order #{order.Id} Rejected: {order.FailureReason}");
            return false;
        }

        // 2. Customer is active
        if (!customer.IsActive)
        {
            order.Status = OrderStatus.Rejected;
            order.FailureReason = "Customer is inactive";
            Log.Info($"SyncOrderValidator -> Order #{order.Id} Rejected: {order.FailureReason}");
            return false;
        }

        // Fetch products for checks 3 and 4
        var loadedProducts = new List<(Product Product, OrderItem Item)>();
        foreach (var item in order.Items)
        {
            var product = _productRepository.GetById(item.ProductId);
            if (product == null)
            {
                // 3. Every product exists
                order.Status = OrderStatus.Rejected;
                order.FailureReason = $"Product #{item.ProductId} not found";
                Log.Info($"SyncOrderValidator -> Order #{order.Id} Rejected: {order.FailureReason}");
                return false;
            }
            loadedProducts.Add((product, item));
        }

        // 4. StockQuantity >= Quantity (checked, not reserved)
        foreach (var (product, item) in loadedProducts)
        {
            if (product.StockQuantity < item.Quantity)
            {
                order.Status = OrderStatus.Rejected;
                order.FailureReason = $"Insufficient stock for '{product.Name}'";
                Log.Info($"SyncOrderValidator -> Order #{order.Id} Rejected: {order.FailureReason}");
                return false;
            }
        }

        // 5. Coupon (if not null) exists & 6. Coupon ExpiryDate >= DateTime.Today
        if (!string.IsNullOrWhiteSpace(order.CouponCode))
        {
            var coupon = _couponRepository.GetByCode(order.CouponCode);
            if (coupon == null)
            {
                order.Status = OrderStatus.Rejected;
                order.FailureReason = $"Coupon '{order.CouponCode}' does not exist";
                Log.Info($"SyncOrderValidator -> Order #{order.Id} Rejected: {order.FailureReason}");
                return false;
            }

            if (coupon.ExpiryDate < DateTime.Today)
            {
                order.Status = OrderStatus.Rejected;
                order.FailureReason = $"Coupon '{order.CouponCode}' has expired";
                Log.Info($"SyncOrderValidator -> Order #{order.Id} Rejected: {order.FailureReason}");
                return false;
            }
        }

        // Passed all checks
        order.Status = OrderStatus.Validated;
        order.FailureReason = null;
        Log.Success($"SyncOrderValidator -> Order #{order.Id} Validated successfully.");
        return true;
    }
}

