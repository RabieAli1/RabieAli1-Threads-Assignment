namespace OrderProcessing.Shared;

public static class SimulatedDelays
{
    // DB query (all Get methods): Open connection 200 -> Execute query 300-600
    public const int DbQueryOpenConnection = 200;
    public static int DbQueryExecute() => Random.Shared.Next(300, 600 + 1);

    // DB write (Save, Reserve, Release): Open connection 200 -> Begin transaction 100 -> Insert/Update 400 -> Commit 200
    public const int DbWriteOpenConnection = 200;
    public const int DbWriteBeginTransaction = 100;
    public const int DbWriteInsertUpdate = 400;
    public const int DbWriteCommit = 200;

    // External API (Discount, Shipping): DNS/handshake 150 -> Send request 300 -> Wait for response 500-1500
    public const int ExternalApiDnsHandshake = 150;
    public const int ExternalApiSendRequest = 300;
    public static int ExternalApiWaitForResponse() => Random.Shared.Next(500, 1500 + 1);

    // Payment gateway: Authorize 800 -> Fraud check 500 -> Capture 700
    public const int PaymentAuthorize = 800;
    public const int PaymentFraudCheck = 500;
    public const int PaymentCapture = 700;

    // Email (SMTP): Render template 200 -> Connect to SMTP 300 -> Send 500
    public const int EmailRenderTemplate = 200;
    public const int EmailConnectSmtp = 300;
    public const int EmailSend = 500;
}

