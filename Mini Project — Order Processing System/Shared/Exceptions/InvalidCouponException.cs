namespace OrderProcessing.Shared.Exceptions;

public class InvalidCouponException : Exception
{
    public InvalidCouponException(string message) : base(message)
    {
    }

    public InvalidCouponException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

