namespace AadhiCrackers.Application.Common.Interfaces;

public interface IBusinessNumberGenerator
{
    Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken = default);
    Task<string> GenerateInvoiceNumberAsync(CancellationToken cancellationToken = default);
    Task<string> GeneratePaymentNumberAsync(CancellationToken cancellationToken = default);
    Task<string> GenerateRefundNumberAsync(CancellationToken cancellationToken = default);
    Task<string> GenerateReturnNumberAsync(CancellationToken cancellationToken = default);
    Task<string> GeneratePurchaseOrderNumberAsync(CancellationToken cancellationToken = default);
    Task<string> GenerateGoodsReceiptNumberAsync(CancellationToken cancellationToken = default);
    Task<string> GenerateExpenseNumberAsync(CancellationToken cancellationToken = default);
    Task<string> GenerateSupplierBillNumberAsync(CancellationToken cancellationToken = default);
}
