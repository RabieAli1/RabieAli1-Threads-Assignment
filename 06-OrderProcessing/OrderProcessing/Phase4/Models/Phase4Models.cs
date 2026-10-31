using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase4.Models;

public class CustomerDashboard
{
    public Customer Customer { get; set; } = null!;
    public List<Order> Orders { get; set; } = new();
    public List<PaymentTransaction> Payments { get; set; } = new();
    public List<EmailMessage> Emails { get; set; } = new();
}

public class ShippingQuote
{
    public string Carrier { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public long ElapsedMs { get; set; }
}

