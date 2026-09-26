using System.Diagnostics;
using Citra.Application.Common.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Citra.Application.Common.Behaviors;

/// <summary>
/// Logs every request's name and duration. Slow requests are logged as warnings,
/// failed requests as errors (the exception is re-thrown unchanged).
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int SlowRequestThresholdMs = 500;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        logger.LogDebug("Handling {RequestName}", requestName);

        try
        {
            var response = await next();
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs)
            {
                logger.LogWarning(
                    "Slow request {RequestName} took {ElapsedMs} ms",
                    requestName, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                logger.LogInformation(
                    "Handled {RequestName} in {ElapsedMs} ms",
                    requestName, stopwatch.ElapsedMilliseconds);
            }

            return response;
        }
        catch (ValidationException ex)
        {
            // Bad input from the client, not a server error: no stack trace needed.
            stopwatch.Stop();
            logger.LogWarning(
                "Request {RequestName} failed validation after {ElapsedMs} ms: {Errors}",
                requestName, stopwatch.ElapsedMilliseconds, ex.Errors.Keys);
            throw;
        }
        catch (Exception ex) when (ex is UnauthorizedException or NotFoundException or ForbiddenException)
        {
            // Expected outcomes (wrong password, missing record, not allowed) — normal user situations,
            // not server faults. Logged briefly, so real errors aren't buried in noise.
            stopwatch.Stop();
            logger.LogWarning(
                "Request {RequestName} was rejected after {ElapsedMs} ms: {Reason}",
                requestName, stopwatch.ElapsedMilliseconds, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(
                ex,
                "Request {RequestName} failed after {ElapsedMs} ms",
                requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
