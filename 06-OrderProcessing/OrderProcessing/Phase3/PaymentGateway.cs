using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public class PaymentGateway : IPaymentGateway
{
    private readonly JsonFileStore _store;
    private readonly HashSet<int> _timeoutOrderIds;
    private readonly HashSet<int> _declineOrderIds;

    public PaymentGateway(
        JsonFileStore? store = null,
        IEnumerable<int>? timeoutOrderIds = null,
        IEnumerable<int>? declineOrderIds = null)
    {
        _store = store ?? new JsonFileStore();
        _timeoutOrderIds = timeoutOrderIds != null ? new HashSet<int>(timeoutOrderIds) : new HashSet<int>();
        _declineOrderIds = declineOrderIds != null ? new HashSet<int>(declineOrderIds) : new HashSet<int>();
    }

    /// <summary>
    /// Simulated Payment Gateway workflow: Authorize 800ms -> Fraud check 500ms -> Capture 700ms.
    /// Passes ct to every Task.Delay.
    /// </summary>
    public async Task<PaymentResult> ProcessPaymentAsync(int orderId, decimal amount, CancellationToken ct)
    {
        Log.Info($"PaymentGateway -> [Authorize] Order #{orderId} amount {amount:F2} (800ms)...");
        await Task.Delay(SimulatedDelays.PaymentAuthorize, ct).ConfigureAwait(false);

        Log.Info($"PaymentGateway -> [Fraud Check] Order #{orderId} (500ms)...");
        await Task.Delay(SimulatedDelays.PaymentFraudCheck, ct).ConfigureAwait(false);

        // Controlled decline check (Order #8)
        if (_declineOrderIds.Contains(orderId))
        {
            Log.Info($"PaymentGateway -> Order #{orderId} payment declined: Card declined by issuer.");
            var declinedTxn = new PaymentTransaction
            {
                OrderId = orderId,
                Amount = amount,
                Status = PaymentStatus.Declined,
                TransactionId = null,
                CreatedAt = DateTime.Now
            };

            var payments = await _store.ReadAllAsync<PaymentTransaction>(DataFiles.Payments, ct).ConfigureAwait(false);
            payments.Add(declinedTxn);
            await _store.WriteAllAsync(DataFiles.Payments, payments, ct).ConfigureAwait(false);

            return new PaymentResult
            {
                Success = false,
                Status = PaymentStatus.Declined,
                Message = "Card declined by issuer",
                TransactionId = null
            };
        }

        // Capture step: timeout check for Order #5 (5000ms delay instead of 700ms)
        int captureDelay = _timeoutOrderIds.Contains(orderId) ? 5000 : SimulatedDelays.PaymentCapture;
        Log.Info($"PaymentGateway -> [Capture] Order #{orderId} ({captureDelay}ms)...");
        await Task.Delay(captureDelay, ct).ConfigureAwait(false);

        string transactionId = "TXN-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

        var capturedTxn = new PaymentTransaction
        {
            OrderId = orderId,
            Amount = amount,
            Status = PaymentStatus.Captured,
            TransactionId = transactionId,
            CreatedAt = DateTime.Now
        };

        var allPayments = await _store.ReadAllAsync<PaymentTransaction>(DataFiles.Payments, ct).ConfigureAwait(false);
        allPayments.Add(capturedTxn);
        await _store.WriteAllAsync(DataFiles.Payments, allPayments, ct).ConfigureAwait(false);

        Log.Success($"PaymentGateway -> Order #{orderId} Captured ({transactionId})");
        return new PaymentResult
        {
            Success = true,
            Status = PaymentStatus.Captured,
            TransactionId = transactionId,
            Message = "Payment captured successfully"
        };
    }

    /// <summary>
    /// Simulated External API: DNS/handshake 150ms -> Send request 300ms -> Wait for response 500-1500ms.
    /// Sets the transaction record in payments.json to Refunded.
    /// </summary>
    public async Task RefundAsync(string transactionId, CancellationToken ct = default)
    {
        Log.Info($"PaymentGateway -> [Refund API] DNS/handshake for {transactionId}...");
        await Task.Delay(SimulatedDelays.ExternalApiDnsHandshake, ct).ConfigureAwait(false);

        Log.Info($"PaymentGateway -> [Refund API] Sending refund request for {transactionId}...");
        await Task.Delay(SimulatedDelays.ExternalApiSendRequest, ct).ConfigureAwait(false);

        Log.Info($"PaymentGateway -> [Refund API] Waiting for refund response for {transactionId}...");
        await Task.Delay(SimulatedDelays.ExternalApiWaitForResponse(), ct).ConfigureAwait(false);

        var payments = await _store.ReadAllAsync<PaymentTransaction>(DataFiles.Payments, ct).ConfigureAwait(false);
        var txn = payments.FirstOrDefault(p => string.Equals(p.TransactionId, transactionId, StringComparison.OrdinalIgnoreCase));

        if (txn != null)
        {
            txn.Status = PaymentStatus.Refunded;
            await _store.WriteAllAsync(DataFiles.Payments, payments, ct).ConfigureAwait(false);
            Log.Success($"PaymentGateway -> Transaction {transactionId} refunded successfully in payments.json.");
        }
        else
        {
            Log.Error($"PaymentGateway -> Transaction {transactionId} not found in payments.json for refund.");
        }
    }
}

