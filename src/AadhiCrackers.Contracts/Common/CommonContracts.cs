namespace AadhiCrackers.Contracts.Common;

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public PagedResult() { }

    public PagedResult(IReadOnlyList<T> items, int count, int pageNumber, int pageSize)
    {
        Items = items;
        TotalCount = count;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }
}

public class ApiResponse<T>
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public T? Data { get; set; }
    public string? CorrelationId { get; set; }

    public static ApiResponse<T> Ok(T data, string? message = null, string? correlationId = null) =>
        new() { Success = true, Data = data, Message = message, CorrelationId = correlationId };

    public static ApiResponse<T> Fail(string message, string? correlationId = null) =>
        new() { Success = false, Message = message, CorrelationId = correlationId };
}
