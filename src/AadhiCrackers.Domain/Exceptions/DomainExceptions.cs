namespace AadhiCrackers.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}

public class InvalidOrderStateTransitionException : DomainException
{
    public InvalidOrderStateTransitionException(string fromStatus, string toStatus)
        : base($"Cannot transition order status from '{fromStatus}' to '{toStatus}'.") { }
}

public class InsufficientStockException : DomainException
{
    public string Sku { get; }
    public int AvailableQuantity { get; }
    public int RequestedQuantity { get; }

    public InsufficientStockException(string sku, int availableQuantity, int requestedQuantity)
        : base($"Insufficient stock for product SKU '{sku}'. Available: {availableQuantity}, Requested: {requestedQuantity}.")
    {
        Sku = sku;
        AvailableQuantity = availableQuantity;
        RequestedQuantity = requestedQuantity;
    }
}

public class ResourceNotFoundException : DomainException
{
    public ResourceNotFoundException(string resourceName, object key)
        : base($"Resource '{resourceName}' with key '{key}' was not found.") { }
}

public class ConcurrencyConflictException : DomainException
{
    public ConcurrencyConflictException(string message) : base(message) { }
}
