namespace AadhiCrackers.Application.Common.Interfaces;

public interface IBusinessNumberGenerator
{
    Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken = default);
    Task<string> GenerateInvoiceNumberAsync(CancellationToken cancellationToken = default);
    Task<string> GeneratePaymentNumberAsync(CancellationToken cancellationToken = default);
    Task<string> GenerateExpenseNumberAsync(CancellationToken cancellationToken = default);
}
