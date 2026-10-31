using System.Collections.Concurrent;
using OrderProcessing.Shared;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public class EmailService : IEmailService
{
    private readonly JsonFileStore _store;
    private readonly Dictionary<int, int> _failAttempts;
    private readonly ConcurrentDictionary<int, int> _attempts = new();

    public EmailService(JsonFileStore? store = null, Dictionary<int, int>? failAttempts = null)
    {
        _store = store ?? new JsonFileStore();
        _failAttempts = failAttempts != null ? new Dictionary<int, int>(failAttempts) : new Dictionary<int, int>();
    }

    /// <summary>
    /// Simulated Email (SMTP): Render template 200ms -> Connect to SMTP 300ms -> Send 500ms.
    /// Appends confirmed email to emails.json.
    /// </summary>
    public async Task SendConfirmationAsync(Order order, Customer customer, CancellationToken ct = default)
    {
        int attempt = _attempts.AddOrUpdate(order.Id, 1, (_, c) => c + 1);

        Log.Info($"EmailService -> [SMTP] Rendering email template for Order #{order.Id} (Attempt {attempt})...");
        await Task.Delay(SimulatedDelays.EmailRenderTemplate, ct).ConfigureAwait(false);

        Log.Info($"EmailService -> [SMTP] Connecting to SMTP server for Order #{order.Id}...");
        await Task.Delay(SimulatedDelays.EmailConnectSmtp, ct).ConfigureAwait(false);

        // Check injected failure
        if (_failAttempts.TryGetValue(order.Id, out int maxFails) && attempt <= maxFails)
        {
            Log.Error($"EmailService -> [SMTP] Connection dropped during Send for Order #{order.Id} (Attempt {attempt}/{maxFails})");
            throw new SmtpUnavailableException($"SMTP server unavailable (attempt {attempt})");
        }

        Log.Info($"EmailService -> [SMTP] Sending email to {customer.Email} for Order #{order.Id}...");
        await Task.Delay(SimulatedDelays.EmailSend, ct).ConfigureAwait(false);

        var email = new EmailMessage
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            To = customer.Email,
            Subject = $"Order #{order.Id} Confirmation",
            Body = $"Dear {customer.Name}, your order #{order.Id} with total {order.TotalAmount:F2} has been confirmed.",
            SentAt = DateTime.Now,
            Attempts = attempt,
            LastError = null
        };

        await _store.AppendAsync(DataFiles.Emails, email, ct).ConfigureAwait(false);

        Log.Success($"EmailService -> Confirmation email sent to {customer.Email} for Order #{order.Id} (Attempts: {attempt})");
    }

    public int GetAttempts(int orderId) => _attempts.TryGetValue(orderId, out int c) ? c : 0;
}

