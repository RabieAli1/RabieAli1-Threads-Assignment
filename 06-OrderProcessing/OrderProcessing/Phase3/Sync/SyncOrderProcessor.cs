using System.Diagnostics;
using OrderProcessing.Phase1.Sync;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3.Sync;

public class SyncOrderProcessor
{
    private readonly SyncCustomerRepository _customerRepo;
    private readonly SyncInventoryService _inventoryService;
    private readonly SyncPricingService _pricingService;
    private readonly SyncPaymentGateway _paymentGateway;
    private readonly SyncOrderRepository _orderRepo;
    private readonly SyncEmailService _emailService;
    private readonly SyncProductRepository _productRepo;

    public SyncOrderProcessor(JsonFileStore? store = null)
    {
        var fileStore = store ?? new JsonFileStore();
        _customerRepo = new SyncCustomerRepository(fileStore);
        _inventoryService = new SyncInventoryService(fileStore);
        _pricingService = new SyncPricingService(_customerRepo, store: fileStore);
        _paymentGateway = new SyncPaymentGateway(fileStore);
        _orderRepo = new SyncOrderRepository(fileStore);
        _emailService = new SyncEmailService(fileStore);
        _productRepo = new SyncProductRepository(fileStore);
    }

    public bool ProcessOrder(Order order)
    {
        Log.Info($"SyncOrderProcessor -> Starting checkout for Order #{order.Id} on Thread {Environment.CurrentManagedThreadId}...");
        var sw = Stopwatch.StartNew();

        // Step 1: Customer Check
        var customer = _customerRepo.GetById(order.CustomerId);
        if (customer == null)
        {
            order.Status = OrderStatus.Rejected;
            order.FailureReason = $"Customer #{order.CustomerId} not found";
            _orderRepo.Save(order);
            Log.Info($"SyncOrderProcessor -> Order #{order.Id}: REJECTED ({order.FailureReason})");
            return false;
        }

        if (!customer.IsActive)
        {
            order.Status = OrderStatus.Rejected;
            order.FailureReason = "Customer is inactive";
            _orderRepo.Save(order);
            Log.Info($"SyncOrderProcessor -> Order #{order.Id}: REJECTED (Customer is inactive)");
            return false;
        }

        // Step 2: Reserve Stock
        bool reserved = _inventoryService.ReserveStock(order);
        if (!reserved)
        {
            string failedProductName = "item";
            var products = _productRepo.GetAll();
            var prodMap = products.ToDictionary(p => p.Id);
            foreach (var item in order.Items)
            {
                if (!prodMap.TryGetValue(item.ProductId, out var p) || p.StockQuantity < item.Quantity)
                {
                    failedProductName = p?.Name ?? $"Product #{item.ProductId}";
                    break;
                }
            }

            order.Status = OrderStatus.Rejected;
            order.FailureReason = $"Insufficient stock for '{failedProductName}'";
            _orderRepo.Save(order);
            Log.Info($"SyncOrderProcessor -> Order #{order.Id}: REJECTED ({order.FailureReason})");
            return false;
        }

        // Step 3: Pricing
        try
        {
            _pricingService.CalculateTotal(order);
        }
        catch (InvalidCouponException ex)
        {
            _inventoryService.ReleaseStock(order);
            order.Status = OrderStatus.Rejected;
            order.FailureReason = ex.Message;
            _orderRepo.Save(order);
            Log.Info($"SyncOrderProcessor -> Order #{order.Id}: REJECTED ({ex.Message})");
            return false;
        }

        // Step 4: Payment
        var paymentResult = _paymentGateway.ProcessPayment(order.Id, order.TotalAmount);
        if (!paymentResult.Success)
        {
            _inventoryService.ReleaseStock(order);
            order.Status = OrderStatus.PaymentFailed;
            order.FailureReason = paymentResult.Message ?? "Payment failed";
            _orderRepo.Save(order);
            return false;
        }

        // Step 5: Save Completed Order
        order.TransactionId = paymentResult.TransactionId;
        order.Status = OrderStatus.Completed;
        _orderRepo.Save(order);

        // Step 6: Confirmation Email
        _emailService.SendConfirmation(order, customer);

        sw.Stop();
        Log.Success($"SyncOrderProcessor -> Order #{order.Id} COMPLETED successfully in {sw.ElapsedMilliseconds} ms");
        return true;
    }
}

