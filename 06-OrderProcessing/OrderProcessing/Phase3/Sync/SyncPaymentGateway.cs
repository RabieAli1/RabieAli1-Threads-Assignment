using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3.Sync;

public class SyncPaymentGateway
{
    private readonly JsonFileStore _store;

    public SyncPaymentGateway(JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
    }

    public PaymentResult ProcessPayment(int orderId, decimal amount)
    {
        Log.Info($"SyncPaymentGateway -> Order #{orderId}: Authorizing payment of {amount:F2}...");
        Thread.Sleep(SimulatedDelays.PaymentAuthorize);

        Log.Info($"SyncPaymentGateway -> Order #{orderId}: Running fraud check...");
        Thread.Sleep(SimulatedDelays.PaymentFraudCheck);

        Log.Info($"SyncPaymentGateway -> Order #{orderId}: Capturing payment...");
        Thread.Sleep(SimulatedDelays.PaymentCapture);

        string txnId = "TXN-" + Guid.NewGuid().ToString("N")[..12].ToUpper();
        var record = new PaymentTransaction
        {
            OrderId = orderId,
            Amount = amount,
            Status = PaymentStatus.Captured,
            TransactionId = txnId,
            CreatedAt = DateTime.UtcNow
        };

        var payments = _store.ReadAll<PaymentTransaction>(DataFiles.Payments);
        payments.Add(record);
        _store.WriteAll(DataFiles.Payments, payments);

        Log.Success($"SyncPaymentGateway -> Order #{orderId}: Captured successfully ({txnId})");
        return new PaymentResult
        {
            Success = true,
            TransactionId = txnId,
            Status = PaymentStatus.Captured,
            Message = "Payment captured successfully"
        };
    }
}
