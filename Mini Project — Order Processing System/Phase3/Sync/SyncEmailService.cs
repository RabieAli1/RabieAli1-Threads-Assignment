using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3.Sync;

public class SyncEmailService
{
    private readonly JsonFileStore _store;

    public SyncEmailService(JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
    }

    public void SendConfirmation(Order order, Customer customer)
    {
        Log.Info($"SyncEmailService -> Rendering template for Order #{order.Id}...");
        Thread.Sleep(SimulatedDelays.EmailRenderTemplate);

        Log.Info($"SyncEmailService -> Connecting to SMTP server for Order #{order.Id}...");
        Thread.Sleep(SimulatedDelays.EmailConnectSmtp);

        Log.Info($"SyncEmailService -> Sending confirmation email to {customer.Email} for Order #{order.Id}...");
        Thread.Sleep(SimulatedDelays.EmailSend);

        var email = new EmailMessage
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            To = customer.Email,
            Subject = $"Order #{order.Id} Confirmation",
            Body = $"Thank you for your order! Total amount: {order.TotalAmount:F2}.",
            SentAt = DateTime.UtcNow,
            Attempts = 1,
            LastError = null
        };

        var emails = _store.ReadAll<EmailMessage>(DataFiles.Emails);
        emails.Add(email);
        _store.WriteAll(DataFiles.Emails, emails);

        Log.Success($"SyncEmailService -> Confirmation email sent for Order #{order.Id} to {customer.Email}.");
    }
}
