using System.Text.Json;
using Citra.Application.Common.Chat;
using Citra.Application.Common.Embeddings;
using Citra.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Citra.Api.ErrorHandling;

/// <summary>
/// Turns exceptions into RFC 7807 ProblemDetails JSON responses:
/// known application exceptions get their proper 4xx status, anything else becomes a 500
/// whose details are only exposed in Development.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            // Keys in camelCase ("companyName") to match the JSON field names the client sent.
            ValidationException validation => new ValidationProblemDetails(
                validation.Errors.ToDictionary(e => JsonNamingPolicy.CamelCase.ConvertName(e.Key), e => e.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            },

            NotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource not found.",
                Detail = notFound.Message,
            },

            UnauthorizedException unauthorized => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized.",
                Detail = unauthorized.Message,
            },

            ForbiddenException forbidden => new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden.",
                Detail = forbidden.Message,
            },

            // Raised by Kestrel itself, e.g. an upload over the request size limit (413) or a broken multipart body.
            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "The request is too large."
                    : "The request could not be read.",
            },

            ConflictException conflict => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict.",
                Detail = conflict.Message,
            },

            TooManyRequestsException tooMany => new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests.",
                Detail = tooMany.Message,
            },

            // An AI provider (embeddings or chat) is down or rate-limited even after retries: temporary, not a bug.
            EmbeddingException or ChatModelException => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "The AI service is not available right now.",
                Detail = "Please try again in a minute.",
            },

            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = environment.IsDevelopment() ? exception.Message : null,
            },
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception for {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else if (problem.Status == StatusCodes.Status503ServiceUnavailable)
        {
            logger.LogWarning(
                exception,
                "AI provider unavailable for {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
