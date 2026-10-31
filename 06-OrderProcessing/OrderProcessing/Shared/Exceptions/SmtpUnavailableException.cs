namespace OrderProcessing.Shared.Exceptions;

public class SmtpUnavailableException : Exception
{
    public SmtpUnavailableException(string message) : base(message)
    {
    }

    public SmtpUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

