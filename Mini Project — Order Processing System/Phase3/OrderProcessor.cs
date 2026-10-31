using System.Diagnostics;
using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Phase2;
using OrderProcessing.Shared;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public class OrderProcessor
{
    private readonly CustomerService _customerService;
    private readonly InventoryService _inventoryService;
    private readonly PricingService _pricingService;
    private readonly IPaymentGateway _paymentGateway;
    private readonly OrderRepository _orderRepository;
    private readonly IEmailService _emailService;
    private readonly ProductRepository _productRepository;
    private readonly JsonFileStore _store;

    public OrderProcessor(
        CustomerService? customerService = null,
        InventoryService? inventoryService = null,
        PricingService? pricingService = null,
        IPaymentGateway? paymentGateway = null,
        OrderRepository? orderRepository = null,
        IEmailService? emailService = null,
        ProductRepository? productRepository = null,
        JsonFileStore? store = null,
        bool disableFailures = false)
    {
        _store = store ?? new JsonFileStore();
        if (disableFailures)
        {
            _customerService = new CustomerService(store: _store);
            _inventoryService = new InventoryService(_store);
            _pricingService = new PricingService(store: _store, failShippingForOrderIds: Array.Empty<int>());
            _paymentGateway = new PaymentGateway(_store, timeoutOrderIds: Array.Empty<int>(), declineOrderIds: Array.Empty<int>());
            _orderRepository = new OrderRepository(_store, failSaveForOrderIds: Array.Empty<int>());
            _emailService = new EmailService(_store, failAttempts: new Dictionary<int, int>());
            _productRepository = new ProductRepository(_store);
        }
        else
        {
            _customerService = customerService ?? new CustomerService(store: _store);
            _inventoryService = inventoryService ?? new InventoryService(_store);
            _pricingService = pricingService ?? new PricingService(store: _store);
            _paymentGateway = paymentGateway ?? new PaymentGateway(_store);
            _orderRepository = orderRepository ?? new OrderRepository(_store);
            _emailService = emailService ?? new EmailService(_store);
            _productRepository = productRepository ?? new ProductRepository(_store);
        }
    }

    /// <summary>
    /// Executes the full 7-step checkout workflow for a single order.
    /// Every order that stops early is saved with its final status so orders.json always contains 20 records.
    /// </summary>
    public async Task<bool> ProcessOrderAsync(Order order, CancellationToken ct = default)
    {
        Log.Info($"Order #{order.Id} -> Starting checkout workflow...");
        var overallSw = Stopwatch.StartNew();
        bool stockWasReserved = false;
        bool paymentWasCaptured = false;

        try
        {
            // -------------------------------------------------------------
            // Step 1/6: Customer Check
            // -------------------------------------------------------------
            var sw1 = Stopwatch.StartNew();
            Customer customer;
            try
            {
                customer = await _customerService.GetCustomerAsync(order.CustomerId, ct).ConfigureAwait(false);
                sw1.Stop();

                if (!customer.IsActive)
                {
                    order.Status = OrderStatus.Rejected;
                    order.FailureReason = "Customer is inactive";
                    await _orderRepository.SaveAsync(order, ct).ConfigureAwait(false);
                    Log.Info($"Order #{order.Id} -> Step 1/6 Customer ... REJECTED (Customer is inactive) in {sw1.ElapsedMilliseconds} ms");
                    return false;
                }

                Log.Info($"Order #{order.Id} -> Step 1/6 Customer ... OK ({customer.Name}) in {sw1.ElapsedMilliseconds} ms");
            }
            catch (CustomerNotFoundException ex)
            {
                sw1.Stop();
                order.Status = OrderStatus.Rejected;
                order.FailureReason = ex.Message;
                await _orderRepository.SaveAsync(order, ct).ConfigureAwait(false);
                Log.Info($"Order #{order.Id} -> Step 1/6 Customer ... REJECTED ({ex.Message}) in {sw1.ElapsedMilliseconds} ms");
                return false;
            }

            // -------------------------------------------------------------
            // Step 2/6: Stock Reservation
            // -------------------------------------------------------------
            var sw2 = Stopwatch.StartNew();
            bool stockReserved = await _inventoryService.ReserveStockAsync(order, ct).ConfigureAwait(false);
            sw2.Stop();

            if (!stockReserved)
            {
                string failedProductName = "item";
                var products = await _productRepository.GetAllAsync(ct).ConfigureAwait(false);
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
                await _orderRepository.SaveAsync(order, ct).ConfigureAwait(false);
                Log.Info($"Order #{order.Id} -> Step 2/6 Stock ... REJECTED ({order.FailureReason}) in {sw2.ElapsedMilliseconds} ms");
                return false;
            }

            stockWasReserved = true;
            Log.Info($"Order #{order.Id} -> Step 2/6 Stock ... OK in {sw2.ElapsedMilliseconds} ms");

            // -------------------------------------------------------------
            // Step 3/6: Pricing Calculation
            // -------------------------------------------------------------
            var sw3 = Stopwatch.StartNew();
            try
            {
                await _pricingService.CalculateTotalAsync(order, ct).ConfigureAwait(false);
                sw3.Stop();
                Log.Info($"Order #{order.Id} -> Step 3/6 Pricing ... OK (Total: {order.TotalAmount:F2}) in {sw3.ElapsedMilliseconds} ms");
            }
            catch (InvalidCouponException ex)
            {
                sw3.Stop();
                await _inventoryService.ReleaseStockAsync(order, ct).ConfigureAwait(false);
                stockWasReserved = false;
                order.Status = OrderStatus.Rejected;
                order.FailureReason = ex.Message;
                await _orderRepository.SaveAsync(order, ct).ConfigureAwait(false);
                Log.Info($"Order #{order.Id} -> Step 3/6 Pricing ... REJECTED ({ex.Message}) in {sw3.ElapsedMilliseconds} ms, released");
                return false;
            }

            // -------------------------------------------------------------
            // Step 4/6: Payment Processing (3s timeout or run cancellation)
            // -------------------------------------------------------------
            var sw4 = Stopwatch.StartNew();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                var paymentResult = await _paymentGateway.ProcessPaymentAsync(order.Id, order.TotalAmount, linkedCts.Token).ConfigureAwait(false);
                sw4.Stop();

                if (!paymentResult.Success)
                {
                    await _inventoryService.ReleaseStockAsync(order, ct).ConfigureAwait(false);
                    stockWasReserved = false;
                    order.Status = OrderStatus.PaymentFailed;
                    order.FailureReason = paymentResult.Message ?? "Card declined by issuer";
                    await _orderRepository.SaveAsync(order, ct).ConfigureAwait(false);
                    Log.Info($"Order #{order.Id} -> Step 4/6 Payment ... DECLINED in {sw4.ElapsedMilliseconds} ms, released");
                    return false;
                }

                order.TransactionId = paymentResult.TransactionId;
                paymentWasCaptured = true;
                Log.Info($"Order #{order.Id} -> Step 4/6 Payment ... OK ({order.TransactionId}) in {sw4.ElapsedMilliseconds} ms");
            }
            catch (OperationCanceledException)
            {
                sw4.Stop();
                if (ct.IsCancellationRequested)
                {
                    // Propagate to outer catch
                    throw;
                }
                else
                {
                    // 3-second timeout: release stock, PaymentFailed, Payment gateway timeout (3s)
                    await _inventoryService.ReleaseStockAsync(order, ct).ConfigureAwait(false);
                    stockWasReserved = false;
                    order.Status = OrderStatus.PaymentFailed;
                    order.FailureReason = "Payment gateway timeout (3s)";
                    await _orderRepository.SaveAsync(order, ct).ConfigureAwait(false);
                    Log.Info($"Order #{order.Id} -> Step 4/6 Payment ... TIMEOUT after {sw4.ElapsedMilliseconds} ms, released");
                    return false;
                }
            }

            // -------------------------------------------------------------
            // Step 5/6: Set TransactionId, Status = Completed, SaveAsync
            // -------------------------------------------------------------
            var sw5 = Stopwatch.StartNew();
            order.Status = OrderStatus.Completed;

            try
            {
                await _orderRepository.SaveAsync(order, ct).ConfigureAwait(false);
                sw5.Stop();
                Log.Info($"Order #{order.Id} -> Step 5/6 Save ... OK in {sw5.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                sw5.Stop();
                Log.Error($"Order #{order.Id} -> Step 5/6 Save failed ({ex.Message})! Initiating compensation...");
                if (!string.IsNullOrEmpty(order.TransactionId))
                {
                    await _paymentGateway.RefundAsync(order.TransactionId, CancellationToken.None).ConfigureAwait(false);
                }
                await _inventoryService.ReleaseStockAsync(order, CancellationToken.None).ConfigureAwait(false);
                stockWasReserved = false;

                order.Status = OrderStatus.Failed;
                order.FailureReason = $"Database save failed after payment; payment {order.TransactionId} refunded";
                await _orderRepository.SaveAsync(order, CancellationToken.None).ConfigureAwait(false);

                Log.Info($"Order #{order.Id} -> Step 5/6 Compensated: Status=Failed in {sw5.ElapsedMilliseconds} ms");
                return false;
            }

            // -------------------------------------------------------------
            // Step 6/6: Send Email with Retry (Task 9.5)
            // -------------------------------------------------------------
            var sw6 = Stopwatch.StartNew();
            await SendConfirmationWithRetryAsync(order, customer, ct).ConfigureAwait(false);
            sw6.Stop();
            Log.Info($"Order #{order.Id} -> Step 6/6 Email ... completed in {sw6.ElapsedMilliseconds} ms");

            // -------------------------------------------------------------
            // Step 7/7: Return true only if Status is Completed
            // -------------------------------------------------------------
            overallSw.Stop();
            Log.Success($"Order #{order.Id} -> Checkout finished: {order.Status} in {overallSw.Elapsed.TotalSeconds:F2}s");
            return order.Status == OrderStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            if (paymentWasCaptured && !string.IsNullOrEmpty(order.TransactionId))
            {
                try { await _paymentGateway.RefundAsync(order.TransactionId, CancellationToken.None).ConfigureAwait(false); } catch { }
            }
            if (stockWasReserved)
            {
                try { await _inventoryService.ReleaseStockAsync(order, CancellationToken.None).ConfigureAwait(false); } catch { }
            }

            order.Status = OrderStatus.Pending;
            order.FailureReason = "Cancelled";
            try { await _orderRepository.SaveAsync(order, CancellationToken.None).ConfigureAwait(false); } catch { }
            Log.Info($"Order #{order.Id} -> CANCELLED gracefully, stock released and status set to Pending.");
            return false;
        }
    }

    /// <summary>
    /// Task 9.5: 1 initial attempt + up to 3 retries, waiting 500ms, 1000ms, 2000ms.
    /// If attempt 4 fails, append to email_retry.json and return normally.
    /// A failed email must NEVER make a paid order look failed.
    /// </summary>
    public async Task SendConfirmationWithRetryAsync(Order order, Customer customer, CancellationToken ct = default)
    {
        int[] delays = { 500, 1000, 2000 };
        Exception? lastError = null;

        for (int attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                await _emailService.SendConfirmationAsync(order, customer, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Log.Error($"Email attempt {attempt}/4 failed for Order #{order.Id}: {ex.Message}");

                if (attempt < 4)
                {
                    int delayMs = delays[attempt - 1];
                    Log.Info($"Waiting {delayMs} ms before email retry {attempt} for Order #{order.Id}...");
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }
            }
        }

        var retryEmail = new EmailMessage
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            To = customer.Email,
            Subject = $"Order #{order.Id} Confirmation",
            Body = $"Dear {customer.Name}, your order #{order.Id} with total {order.TotalAmount:F2} has been confirmed.",
            SentAt = DateTime.Now,
            Attempts = 4,
            LastError = lastError?.Message ?? "SMTP connection failed"
        };

        await _store.AppendAsync(DataFiles.EmailRetry, retryEmail, CancellationToken.None).ConfigureAwait(false);
        Log.Info($"Email for Order #{order.Id} queued for retry later");
    }
}
