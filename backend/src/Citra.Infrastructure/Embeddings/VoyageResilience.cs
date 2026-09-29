using Citra.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Citra.Infrastructure.Embeddings;

/// <summary>
/// How calls to Voyage survive hiccups: retry rate limits (429), timeouts, 408/5xx and network errors with
/// exponential backoff plus jitter (as Voyage recommends), honouring Retry-After. Client errors such as 400 or
/// 401 are not retried: sending a wrong request or a bad key again cannot succeed.
/// </summary>
internal static class VoyageResilience
{
    /// <summary>One attempt may take this long (a batch of 64 chunks normally takes 1–3 s).</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(60);

    /// <summary>All attempts together, including the waits between them.</summary>
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

    public static IHttpClientBuilder AddVoyageResilience(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler("voyage", (pipeline, context) =>
        {
            var voyage = context.ServiceProvider.GetRequiredService<IOptions<VoyageOptions>>().Value;

            // Outermost: the whole call, retries and waits included.
            pipeline.AddTimeout(TotalTimeout);

            if (voyage.MaxRetryAttempts > 0)
            {
                // Default ShouldHandle = transient HTTP failures: 408, 429, 5xx, HttpRequestException, attempt timeout.
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = voyage.MaxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(voyage.RetryBaseDelaySeconds),
                    MaxDelay = MaxRetryDelay,
                    ShouldRetryAfterHeader = true,
                });
            }

            // Innermost: each single attempt.
            pipeline.AddTimeout(AttemptTimeout);
        });

        return builder;
    }
}
