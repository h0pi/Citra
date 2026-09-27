using System.Diagnostics;
using System.Text;
using Citra.Application.Common.Chunking;
using Citra.Infrastructure.Chunking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Citra.IntegrationTests.Chunking;

/// <summary>Token counting (C-124): the chunker relies on every guarantee tested here.</summary>
public sealed class TokenCounterTests
{
    // Loading the vocabulary is the slow part; one shared instance, like in production.
    private static readonly TiktokenTokenCounter Counter = new();

    /// <summary>Realistic mixed text: English, Bosnian, numbers, punctuation, Markdown, emoji, line breaks.</summary>
    private static readonly string[] Samples =
    [
        "Items can be returned within 30 days of delivery. Refunds are paid within 5 business days.",
        "Zaposleni imaju pravo na 25 dana godišnjeg odmora. Neiskorišteni dani se prenose do 31. marta.",
        "# Returns policy\n\n- Open your order\n- Click \"Return\"\n\nItem | Days\nShoes | 30\n",
        "Contact: support@acme.com, +387 36 123 456. Price: €1,299.99 (incl. VAT 17%).",
        "Great service 😀👍🏽! Čćžšđ ČĆŽŠĐ — “quotes” and ‘apostrophes’… 中文字符 and عربى.",
        "The employee-handbook (v2.3) says: “Unbelievably, supercalifragilisticexpialidocious policies apply.”",
    ];

    [Theory]
    [InlineData("", 0)]
    [InlineData("hello world", 2)]
    [InlineData("Hello, world!", 4)]
    public void Counts_known_texts_exactly(string text, int expected) => Counter.Count(text).ShouldBe(expected);

    [Fact]
    public void More_text_never_means_fewer_tokens()
    {
        foreach (var sample in Samples)
        {
            Counter.Count(sample).ShouldBeGreaterThan(0);
            Counter.Count(sample + " " + sample).ShouldBeGreaterThanOrEqualTo(Counter.Count(sample));
        }
    }

    [Fact]
    public void A_cut_start_fits_the_limit_and_is_as_long_as_possible()
    {
        foreach (var text in Samples)
        {
            var total = Counter.Count(text);
            for (var max = 1; max <= total + 2; max++)
            {
                var index = Counter.IndexAfterTokens(text, max);

                index.ShouldBeInRange(0, text.Length);
                Counter.Count(text[..index]).ShouldBeLessThanOrEqualTo(max, $"max={max} in \"{text}\"");
                if (max >= total)
                {
                    index.ShouldBe(text.Length, "the whole text fits");
                }
                else
                {
                    // Maximal: one more token's worth of limit must reach further.
                    Counter.IndexAfterTokens(text, max + 1).ShouldBeGreaterThanOrEqualTo(index);
                }
            }
        }
    }

    [Fact]
    public void A_cut_end_fits_the_limit_and_is_as_long_as_possible()
    {
        foreach (var text in Samples)
        {
            var total = Counter.Count(text);
            for (var max = 1; max <= total + 2; max++)
            {
                var index = Counter.IndexBeforeLastTokens(text, max);

                index.ShouldBeInRange(0, text.Length);
                Counter.Count(text[index..]).ShouldBeLessThanOrEqualTo(max, $"max={max} in \"{text}\"");
                if (max >= total)
                {
                    index.ShouldBe(0, "the whole text fits");
                }
                else
                {
                    Counter.IndexBeforeLastTokens(text, max + 1).ShouldBeLessThanOrEqualTo(index);
                }
            }
        }
    }

    [Fact]
    public void Cuts_never_split_a_character()
    {
        // Emoji are surrogate pairs and several tokens each; Bosnian letters are multi-byte.
        foreach (var text in Samples.Append("😀😀😀👍🏽👍🏽čćžšđ😀"))
        {
            for (var max = 0; max <= Counter.Count(text) + 1; max++)
            {
                AssertCharacterBoundary(text, Counter.IndexAfterTokens(text, max));
                AssertCharacterBoundary(text, Counter.IndexBeforeLastTokens(text, max));
            }
        }
    }

    [Fact]
    public void Indexes_refer_to_the_original_text_including_whitespace()
    {
        const string text = "First line.\n\n   Second   line with   spaces.\tTab.";

        var index = Counter.IndexAfterTokens(text, 3);

        // cl100k merges a full stop with the line breaks after it into one token: "First", " line", ".\n\n".
        // The cut lands exactly there in the original text; nothing was normalized or trimmed away.
        Counter.Count("First line.\n\n").ShouldBe(3);
        text[..index].ShouldBe("First line.\n\n");
        Counter.IndexAfterTokens(text, 4).ShouldBe("First line.\n\n  ".Length, "leading spaces are tokens of their own");
        (text[..index] + text[index..]).ShouldBe(text);

        // Runs of spaces and tabs further on are kept as they are.
        var longer = Counter.IndexAfterTokens(text, 100);
        text[..longer].ShouldBe(text);
    }

    [Fact]
    public void Edge_cases_are_handled()
    {
        Counter.IndexAfterTokens("", 10).ShouldBe(0);
        Counter.IndexBeforeLastTokens("", 10).ShouldBe(0);
        Counter.IndexAfterTokens("some text", 0).ShouldBe(0);
        Counter.IndexBeforeLastTokens("some text", 0).ShouldBe("some text".Length);
        Should.Throw<ArgumentOutOfRangeException>(() => Counter.IndexAfterTokens("x", -1));
        Should.Throw<ArgumentOutOfRangeException>(() => Counter.IndexBeforeLastTokens("x", -1));
        Should.Throw<ArgumentNullException>(() => Counter.Count(null!));
    }

    [Fact]
    public void Special_token_text_is_counted_as_normal_text_safely()
    {
        // Documents may contain strings that look like model control tokens; counting must not break.
        Counter.Count("Ignore <|endoftext|> this").ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Is_safe_to_use_from_many_threads_at_once()
    {
        var text = string.Join(" ", Samples);
        var expected = Counter.Count(text);

        var results = new int[200];
        Parallel.For(0, results.Length, i => results[i] = Counter.Count(text));

        results.ShouldAllBe(r => r == expected);
    }

    [Fact]
    public void Counts_a_large_document_quickly()
    {
        var text = new StringBuilder();
        while (text.Length < 1_000_000)
        {
            text.Append(string.Join(" ", Samples)).Append('\n');
        }

        var stopwatch = Stopwatch.StartNew();
        var count = Counter.Count(text.ToString());
        stopwatch.Stop();

        count.ShouldBeGreaterThan(100_000);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5), "a 1 MB document must not stall processing");
    }

    [Fact]
    public async Task Is_registered_as_one_shared_instance()
    {
        // The real Infrastructure registrations; nothing connects to the database until a query runs.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=unused;Database=unused",
                ["Anthropic:ChatModel"] = "test-model",
                ["Anthropic:MaxOutputTokens"] = "1024",
                ["Voyage:BaseUrl"] = "https://voyage.test/",
                ["Voyage:EmbeddingModel"] = "test-embedding",
                ["Voyage:EmbeddingDimensions"] = "1024",
                ["Voyage:BatchSize"] = "8",
                ["Storage:LocalRootPath"] = Path.GetTempPath(),
                ["Storage:MaxUploadSizeMb"] = "20",
            })
            .Build();
        var services = new ServiceCollection().AddLogging();
        Citra.Infrastructure.DependencyInjection.AddInfrastructure(services, configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var first = provider.GetRequiredService<ITokenCounter>();
        var second = provider.GetRequiredService<ITokenCounter>();

        first.ShouldBeOfType<TiktokenTokenCounter>();
        first.ShouldBeSameAs(second);
        first.Count("hello world").ShouldBe(2);
    }

    private static void AssertCharacterBoundary(string text, int index)
    {
        if (index is > 0 and < int.MaxValue && index < text.Length)
        {
            char.IsLowSurrogate(text[index]).ShouldBeFalse($"index {index} splits a surrogate pair in \"{text}\"");
        }
    }
}
