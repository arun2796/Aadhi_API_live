using System.Diagnostics;
using System.Net;
using AadhiCrackers.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AadhiCrackers.Api.Middleware;

public class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ProblemDetailsExceptionHandler> _logger;
    private readonly IHostEnvironment _env;

    public ProblemDetailsExceptionHandler(ILogger<ProblemDetailsExceptionHandler> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString("N");
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        _logger.LogError(exception, "Unhandled exception occurred. CorrelationId: {CorrelationId}, TraceId: {TraceId}", correlationId, traceId);

        var (status, title, detail, errors) = exception switch
        {
            ValidationException valEx => (
                HttpStatusCode.BadRequest,
                "Validation Failed",
                "One or more validation errors occurred.",
                valEx.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())
            ),
            ResourceNotFoundException notFoundEx => (
                HttpStatusCode.NotFound,
                "Resource Not Found",
                notFoundEx.Message,
                null
            ),
            InvalidOrderStateTransitionException stateEx => (
                HttpStatusCode.Conflict,
                "Invalid Order State Transition",
                stateEx.Message,
                null
            ),
            InsufficientStockException stockEx => (
                HttpStatusCode.Conflict,
                "Insufficient Stock",
                stockEx.Message,
                null
            ),
            DbUpdateConcurrencyException or ConcurrencyConflictException => (
                HttpStatusCode.Conflict,
                "Concurrency Conflict",
                "The requested resource was modified by another concurrent operation. Please retry with the latest data.",
                null
            ),
            DomainException domainEx => (
                HttpStatusCode.BadRequest,
                "Business Rule Violation",
                domainEx.Message,
                null
            ),
            UnauthorizedAccessException => (
                HttpStatusCode.Unauthorized,
                "Unauthorized Access",
                "You are not authorized to perform this operation.",
                null
            ),
            _ => (
                HttpStatusCode.InternalServerError,
                "An unexpected server error occurred",
                _env.IsDevelopment() ? exception.Message : "Please contact system administrator with the correlation ID for assistance.",
                null
            )
        };

        var problemDetails = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
            Type = $"https://httpstatuses.com/{(int)status}"
        };

        problemDetails.Extensions["correlationId"] = correlationId;
        problemDetails.Extensions["traceId"] = traceId;
        problemDetails.Extensions["timestamp"] = DateTime.UtcNow;

        if (errors != null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        httpContext.Response.StatusCode = (int)status;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
