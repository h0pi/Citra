using Citra.Application.Common.Chat;
using Citra.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Citra.IntegrationTests.Chat;

/// <summary>C-182: what an answer cost, from the price list in the settings.</summary>
public sealed class ChatPricingTests
{
    [Fact]
    public async Task Prices_tokens_by_the_models_price_per_million()
    {
        await using var services = Services([]);
        var pricing = services.GetRequiredService<IChatPricing>();

        pricing.Cost(new ChatUsage("claude-sonnet-5-5", 1_000_000, 1_000_000)).ShouldBe(3m + 15m);
        pricing.Cost(new ChatUsage("claude-opus-5-5", 2000, 500)).ShouldBe(((2000m * 5) + (500m * 25)) / 1_000_000m);
        pricing.Cost(new ChatUsage("claude-haiku-4-5", 7, 3)).ShouldBe(0.000022m, "tiny amounts are kept exactly");
        pricing.Cost(new ChatUsage("citra-extractive", 5000, 300)).ShouldBe(0m);
    }

    [Fact]
    public async Task A_dated_model_id_uses_the_price_of_its_name()
    {
        await using var services = Services([
            ("Chat:Prices:claude-x:InputPerMillionUsd", "1"), ("Chat:Prices:claude-x:OutputPerMillionUsd", "1"),
            ("Chat:Prices:claude-x-2:InputPerMillionUsd", "9"), ("Chat:Prices:claude-x-2:OutputPerMillionUsd", "9")]);
        var pricing = services.GetRequiredService<IChatPricing>();

        pricing.Cost(new ChatUsage("claude-haiku-4-5-20251001", 1_000_000, 0)).ShouldBe(1m);
        pricing.Cost(new ChatUsage("claude-x-2-20260101", 1_000_000, 0)).ShouldBe(9m, "the longest matching name wins");
        pricing.Cost(new ChatUsage("claude-x2", 1_000_000, 0)).ShouldBe(0m, "only a dash starts a date");
    }

    [Fact]
    public async Task An_unknown_model_costs_nothing_rather_than_failing_the_answer()
    {
        await using var services = Services([]);

        services.GetRequiredService<IChatPricing>().Cost(new ChatUsage("gpt-5", 1000, 1000)).ShouldBe(0m);
    }

    [Fact]
    public async Task Prices_can_be_changed_in_the_settings()
    {
        await using var services = Services([("Chat:Prices:claude-sonnet-5-5:InputPerMillionUsd", "4")]);

        services.GetRequiredService<IChatPricing>().Cost(new ChatUsage("claude-sonnet-5-5", 1_000_000, 0)).ShouldBe(4m);
    }

    [Fact]
    public void Negative_token_counts_are_refused()
    {
        var price = new ModelPrice(1, 1);

        Should.Throw<ArgumentOutOfRangeException>(() => price.Cost(-1, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => price.Cost(0, -1));
    }

    [Fact]
    public async Task A_negative_price_is_refused_at_startup()
    {
        await using var services = Services([("Chat:Prices:claude-haiku-4-5:OutputPerMillionUsd", "-1")]);

        Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IChatPricing>())
            .Message.ShouldContain("The price of claude-haiku-4-5 cannot be negative.");
    }

    [Fact]
    public void Every_model_a_bot_can_choose_needs_a_price()
    {
        var options = new Citra.Infrastructure.Options.ChatOptions();
        options.Prices.Remove("claude-opus-5-5");
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options, new System.ComponentModel.DataAnnotations.ValidationContext(options), errors, validateAllProperties: true).ShouldBeFalse();
        errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe("Prices has no price for claude-opus-5-5.");
    }

    private static ServiceProvider Services((string Key, string Value)[] values)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=unused;Database=unused",
            ["Anthropic:ChatModel"] = "claude-default",
            ["Anthropic:MaxOutputTokens"] = "1024",
        };
        foreach (var (key, value) in values)
        {
            settings[key] = value;
        }

        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }
}
