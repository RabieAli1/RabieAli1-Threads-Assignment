namespace OrderProcessing.Shared.Storage;

public static class DataFiles
{
    public const string Customers = "customers.json";
    public const string Products = "products.json";
    public const string Coupons = "coupons.json";
    public const string IncomingOrders = "incoming_orders.json";
    public const string Orders = "orders.json";
    public const string Payments = "payments.json";
    public const string Emails = "emails.json";
    public const string EmailRetry = "email_retry.json";
    public const string InvoicesDirectory = "invoices";
    public const string ReportsDirectory = "reports";

    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "Data");
    public static string InvoicesPath => Path.Combine(DataDirectory, InvoicesDirectory);
    public static string ReportsPath => Path.Combine(DataDirectory, ReportsDirectory);

    public static string GetFilePath(string fileName) => Path.Combine(DataDirectory, fileName);
}
