using Citra.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Citra.Infrastructure.Chat;

/// <summary>
/// How calls to Claude survive hiccups: overloaded (529), rate limits (429), 408/5xx, network errors and slow
/// starts are retried with exponential backoff plus jitter, honouring Retry-After. Only the start of the call is
/// covered (until the response headers arrive): the streamed answer itself is never sent twice.
/// </summary>
internal static class ClaudeResilience
{
    /// <summary>How long Claude may take to start answering, per attempt.</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(60);

    /// <summary>All attempts to start together, including the waits between them.</summary>
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromMinutes(3);

    public static IHttpClientBuilder AddClaudeResilience(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler("claude", (pipeline, context) =>
        {
            var anthropic = context.ServiceProvider.GetRequiredService<IOptions<AnthropicOptions>>().Value;

            pipeline.AddTimeout(TotalTimeout);

            if (anthropic.MaxRetryAttempts > 0)
            {
                // Default ShouldHandle = 408, 429, 5xx (529 included), HttpRequestException and attempt timeouts.
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = anthropic.MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(anthropic.RetryBaseDelaySeconds),
                    MaxDelay = TimeSpan.FromSeconds(30),
                    ShouldRetryAfterHeader = true,
                });
            }

            pipeline.AddTimeout(AttemptTimeout);
        });

        return builder;
    }
}
