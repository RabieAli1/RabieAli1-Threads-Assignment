namespace OrderProcessing.Shared.Exceptions;

public class CustomerNotFoundException : Exception
{
    public CustomerNotFoundException(string message) : base(message)
    {
    }

    public CustomerNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
