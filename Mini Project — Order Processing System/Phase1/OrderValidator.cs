using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase1;

public class OrderValidator
{
    private readonly CustomerRepository _customerRepository;
    private readonly ProductRepository _productRepository;
    private readonly CouponRepository _couponRepository;

    public OrderValidator(
        CustomerRepository? customerRepository = null,
        ProductRepository? productRepository = null,
        CouponRepository? couponRepository = null)
    {
        _customerRepository = customerRepository ?? new CustomerRepository();
        _productRepository = productRepository ?? new ProductRepository();
        _couponRepository = couponRepository ?? new CouponRepository();
    }

    /// <summary>
    /// Validates an order according to the 6 sequential checks:
    /// 1. Customer exists
    /// 2. Customer is active
    /// 3. Every product exists
    /// 4. StockQuantity >= Quantity (checked, not reserved)
    /// 5. Coupon (if not null) exists
    /// 6. Coupon ExpiryDate >= DateTime.Today
    /// Stops at the first failure.
    /// </summary>
    public async Task<bool> ValidateOrderAsync(Order order, CancellationToken ct = default)
    {
        Log.Info($"OrderValidator -> Validating Order #{order.Id}...");

        // 1. Customer exists
        var customer = await _customerRepository.GetByIdAsync(order.CustomerId, ct).ConfigureAwait(false);
        if (customer == null)
        {
            order.Status = OrderStatus.Rejected;
            order.FailureReason = $"Customer #{order.CustomerId} not found";
            Log.Info($"OrderValidator -> Order #{order.Id} Rejected: {order.FailureReason}");
            return false;
        }

        // 2. Customer is active
        if (!customer.IsActive)
        {
            order.Status = OrderStatus.Rejected;
            order.FailureReason = "Customer is inactive";
            Log.Info($"OrderValidator -> Order #{order.Id} Rejected: {order.FailureReason}");
            return false;
        }

        // Fetch products for checks 3 and 4
        var loadedProducts = new List<(Product Product, OrderItem Item)>();
        foreach (var item in order.Items)
        {
            var product = await _productRepository.GetByIdAsync(item.ProductId, ct).ConfigureAwait(false);
            if (product == null)
            {
                // 3. Every product exists
                order.Status = OrderStatus.Rejected;
                order.FailureReason = $"Product #{item.ProductId} not found";
                Log.Info($"OrderValidator -> Order #{order.Id} Rejected: {order.FailureReason}");
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
                Log.Info($"OrderValidator -> Order #{order.Id} Rejected: {order.FailureReason}");
                return false;
            }
        }

        // 5. Coupon (if not null) exists & 6. Coupon ExpiryDate >= DateTime.Today
        if (!string.IsNullOrWhiteSpace(order.CouponCode))
        {
            var coupon = await _couponRepository.GetByCodeAsync(order.CouponCode, ct).ConfigureAwait(false);
            if (coupon == null)
            {
                order.Status = OrderStatus.Rejected;
                order.FailureReason = $"Coupon '{order.CouponCode}' does not exist";
                Log.Info($"OrderValidator -> Order #{order.Id} Rejected: {order.FailureReason}");
                return false;
            }

            if (coupon.ExpiryDate < DateTime.Today)
            {
                order.Status = OrderStatus.Rejected;
                order.FailureReason = $"Coupon '{order.CouponCode}' has expired";
                Log.Info($"OrderValidator -> Order #{order.Id} Rejected: {order.FailureReason}");
                return false;
            }
        }

        // Passed all checks
        order.Status = OrderStatus.Validated;
        order.FailureReason = null;
        Log.Success($"OrderValidator -> Order #{order.Id} Validated successfully.");
        return true;
    }
}
