using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase2;

public class OrderProcessorV2
{
    private readonly ProductRepository _productRepository;
    private readonly OrderRepository _orderRepository;
    private readonly OrderPricer _orderPricer;
    private readonly InvoiceGenerator _invoiceGenerator;

    public OrderProcessorV2(
        ProductRepository? productRepository = null,
        OrderRepository? orderRepository = null,
        OrderPricer? orderPricer = null,
        InvoiceGenerator? invoiceGenerator = null)
    {
        _productRepository = productRepository ?? new ProductRepository();
        _orderRepository = orderRepository ?? new OrderRepository();
        _orderPricer = orderPricer ?? new OrderPricer();
        _invoiceGenerator = invoiceGenerator ?? new InvoiceGenerator();
    }

    /// <summary>
    /// Processes a validated order through stock reservation, pricing, and invoice generation.
    /// Does not catch exceptions so any failure transitions the task to Faulted state.
    /// </summary>
    public async Task<bool> ProcessValidatedOrderAsync(Order order, CancellationToken ct = default)
    {
        Log.Info($"OrderProcessorV2 -> Processing Order #{order.Id}...");

        // 1. Reserve stock for each item with safe ReserveStockAsync in item order
        var reservedItems = new List<(int ProductId, int Quantity)>();
        foreach (var item in order.Items)
        {
            bool reserved = await _productRepository.ReserveStockAsync(item.ProductId, item.Quantity, ct).ConfigureAwait(false);
            if (!reserved)
            {
                // On failure: release items already reserved
                foreach (var (productId, qty) in reservedItems)
                {
                    await _productRepository.ReleaseStockAsync(productId, qty, ct).ConfigureAwait(false);
                }

                // Set Rejected with Insufficient stock for '{name}'
                var product = await _productRepository.GetByIdAsync(item.ProductId, ct).ConfigureAwait(false);
                string productName = product?.Name ?? $"Product #{item.ProductId}";

                order.Status = OrderStatus.Rejected;
                order.FailureReason = $"Insufficient stock for '{productName}'";
                await _orderRepository.SaveAsync(order, ct).ConfigureAwait(false);

                Log.Info($"OrderProcessorV2 -> Order #{order.Id} Rejected: {order.FailureReason}");
                return false;
            }

            reservedItems.Add((item.ProductId, item.Quantity));
        }

        // 2. Pricing (may throw ShippingProviderUnavailableException for Order #7)
        await _orderPricer.PriceOrderAsync(order, ct).ConfigureAwait(false);

        // 3. Invoice generation (CPU-bound)
        await _invoiceGenerator.GenerateAsync(order, ct).ConfigureAwait(false);

        // 4. Set Completed, save, return true
        order.Status = OrderStatus.Completed;
        await _orderRepository.SaveAsync(order, ct).ConfigureAwait(false);

        Log.Success($"OrderProcessorV2 -> Order #{order.Id} Completed successfully!");
        return true;
    }
}
