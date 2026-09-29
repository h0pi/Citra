using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Citra.Application.Common.Chunking;
using Citra.Application.Common.Embeddings;
using Citra.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Citra.Infrastructure.Embeddings;

/// <summary>
/// Embeddings from Voyage AI (POST {BaseUrl}embeddings), used through a typed HttpClient.
/// Many chunks are sent per request, in batches limited by count and by tokens, one batch after another.
/// See https://docs.voyageai.com/reference/embeddings-api.
/// </summary>
internal sealed class VoyageEmbeddingService(HttpClient http, IOptions<VoyageOptions> options, ITokenCounter tokens)
    : IEmbeddingService
{
    /// <summary>
    /// Voyage's input types: it adds a matching instruction to each text, so a question lands near the passages that
    /// answer it rather than near similar-sounding text. Chunks are "document", user questions are "query".
    /// </summary>
    internal const string DocumentInputType = "document";

    internal const string QueryInputType = "query";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly VoyageOptions _options = options.Value;

    public string Model => _options.EmbeddingModel;

    public int Dimensions => _options.EmbeddingDimensions;

    public decimal PricePerMillionTokensUsd => _options.PricePerMillionTokensUsd;

    public async Task<DocumentEmbeddings> EmbedDocumentsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return DocumentEmbeddings.Empty;
        }

        EnsureNotBlank(texts);

        // Batches go out one after another (gentler on rate limits); results are joined in the original order.
        // If any batch fails, the whole call fails: a partial set of vectors is never returned.
        var vectors = new List<float[]>(texts.Count);
        var totalTokens = 0;
        foreach (var batch in Batches(texts))
        {
            var response = await EmbedAsync(batch, DocumentInputType, cancellationToken);
            var embedded = new DocumentEmbeddings(response.Vectors, response.TotalTokens).Validate(batch.Count, Dimensions);
            vectors.AddRange(embedded.Vectors);
            totalTokens += embedded.TotalTokens;
        }

        return new DocumentEmbeddings(vectors, totalTokens).Validate(texts.Count, Dimensions);
    }

    /// <summary>
    /// Consecutive groups of texts, each with at most <see cref="VoyageOptions.BatchSize"/> texts and about
    /// <see cref="VoyageOptions.MaxTokensPerRequest"/> tokens. A single text bigger than the token budget goes alone
    /// (Voyage truncates it to the model's context if needed).
    /// </summary>
    internal IEnumerable<IReadOnlyList<string>> Batches(IReadOnlyList<string> texts)
    {
        var batch = new List<string>();
        var batchTokens = 0;
        foreach (var text in texts)
        {
            var textTokens = tokens.Count(text);
            if (batch.Count > 0
                && (batch.Count >= _options.BatchSize || batchTokens + textTokens > _options.MaxTokensPerRequest))
            {
                yield return batch;
                batch = [];
                batchTokens = 0;
            }

            batch.Add(text);
            batchTokens += textTokens;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    public async Task<QueryEmbedding> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        EnsureNotBlank([text]);
        var response = await EmbedAsync([text], QueryInputType, cancellationToken);
        var result = new DocumentEmbeddings(response.Vectors, response.TotalTokens).Validate(1, Dimensions);
        return new QueryEmbedding(result.Vectors[0], result.TotalTokens);
    }

    private async Task<(float[][] Vectors, int TotalTokens)> EmbedAsync(
        IReadOnlyList<string> texts, string inputType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new EmbeddingException(
                "The Voyage API key is not configured. Set it with: dotnet user-secrets set \"Voyage:ApiKey\" \"<key>\"");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = JsonContent.Create(
                new VoyageRequest(texts, _options.EmbeddingModel, inputType, Dimensions, Truncation: true), options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new EmbeddingException("Voyage AI could not be reached.", ex);
        }
        catch (Exception ex) when (ex is TaskCanceledException or Polly.Timeout.TimeoutRejectedException
            && !cancellationToken.IsCancellationRequested)
        {
            throw new EmbeddingException("Voyage AI did not respond in time.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new EmbeddingException(await ErrorMessageAsync(response, cancellationToken));
            }

            VoyageResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<VoyageResponse>(Json, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new EmbeddingException("Voyage AI returned a response that could not be read.", ex);
            }

            if (body?.Data is null)
            {
                throw new EmbeddingException("Voyage AI returned an empty response.");
            }

            // Voyage lists results with their input index; order by it rather than trusting the array order.
            var vectors = body.Data.OrderBy(d => d.Index).Select(d => d.Embedding!).ToArray();
            if (body.Data.Select(d => d.Index).Order().SequenceEqual(Enumerable.Range(0, texts.Count)) is false)
            {
                throw new EmbeddingException(
                    $"Voyage AI returned results for the wrong inputs ({body.Data.Count} results for {texts.Count} texts).");
            }

            return (vectors, body.Usage?.TotalTokens ?? 0);
        }
    }

    /// <summary>A readable reason for a failed call; Voyage puts details in {"detail": "..."}.</summary>
    private static async Task<string> ErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var detail = string.Empty;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<VoyageError>(Json, cancellationToken);
            detail = error?.Detail ?? string.Empty;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Not JSON (e.g. an HTML error page from a proxy): the status code is enough.
        }

        var status = (int)response.StatusCode;
        var reason = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Voyage AI rejected the API key",
            HttpStatusCode.TooManyRequests => "Voyage AI rate limit reached",
            >= HttpStatusCode.InternalServerError => "Voyage AI had a server error",
            _ => "Voyage AI refused the request",
        };

        return string.IsNullOrWhiteSpace(detail) ? $"{reason} ({status})." : $"{reason} ({status}): {detail}";
    }

    private static void EnsureNotBlank(IReadOnlyList<string> texts)
    {
        for (var i = 0; i < texts.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(texts[i]))
            {
                throw new ArgumentException($"Text {i} is empty; only non-blank texts can be embedded.", nameof(texts));
            }
        }
    }

    private sealed record VoyageRequest(
        IReadOnlyList<string> Input,
        string Model,
        [property: JsonPropertyName("input_type")] string InputType,
        [property: JsonPropertyName("output_dimension")] int OutputDimension,
        bool Truncation);

    private sealed record VoyageResponse(List<VoyageEmbedding>? Data, VoyageUsage? Usage);

    private sealed record VoyageEmbedding(float[]? Embedding, int Index);

    private sealed record VoyageUsage([property: JsonPropertyName("total_tokens")] int TotalTokens);

    private sealed record VoyageError(string? Detail);
}
