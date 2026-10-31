namespace OrderProcessing.Shared.Exceptions;

public class DatabaseWriteException : Exception
{
    public DatabaseWriteException(string message) : base(message)
    {
    }

    public DatabaseWriteException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
