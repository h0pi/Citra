using Citra.Application.Common.Chat;
using static Citra.IntegrationTests.Chat.ChatPromptsTests;

namespace Citra.IntegrationTests.Chat;

/// <summary>C-180/C-181: reading an answer's citation markers, and hiding the "not found" marker while streaming.</summary>
public sealed class AnswerMarkersTests
{
    private const string Marker = ChatPrompts.NotFoundMarker;

    private static readonly NumberedSources Sources = SourceList.Number([Chunk("One."), Chunk("Two."), Chunk("Three.")]);

    // ---------- citations ----------

    [Theory]
    [InlineData("Vacation is 25 days [2].", new[] { 2 })]
    [InlineData("A [3]. B [1][2]. C [2].", new[] { 1, 2, 3 })]
    [InlineData("Both [1, 3] and [2,1].", new[] { 1, 2, 3 })]
    [InlineData("Made up [7], zero [0], huge [123456].", new int[0])]
    [InlineData("No markers at all.", new int[0])]
    [InlineData("A list: [a] and [ 2 ] and [2a].", new int[0])]
    public void Finds_the_cited_sources_once_each_in_number_order(string answer, int[] expected)
    {
        CitationMarkers.Cited(answer, Sources).Select(s => s.Number).ShouldBe(expected);
    }

    [Fact]
    public void Gives_back_the_cited_passages_themselves()
    {
        CitationMarkers.Cited("See [3].", Sources).ShouldHaveSingleItem().ShouldBe(Sources.Sources[2]);
    }

    // ---------- the "not found" marker ----------

    [Fact]
    public void An_ordinary_answer_streams_without_delay()
    {
        var filter = new NotFoundFilter();

        filter.Push("Employees ").ShouldBe("Employees ");
        filter.Push("get 25 days.").ShouldBe("get 25 days.");
        filter.Finish().ShouldBeEmpty();
        filter.Found.ShouldBeFalse();
    }

    [Fact]
    public void The_marker_is_hidden_even_when_it_arrives_in_pieces()
    {
        var filter = new NotFoundFilter();

        filter.Push("  [[NOT").ShouldBeEmpty();
        filter.Push("_FOU").ShouldBeEmpty();
        filter.Push("ND]] ").ShouldBeEmpty();
        filter.Push("  Sorry,").ShouldBe("Sorry,");
        filter.Push(" not found.").ShouldBe(" not found.", "only the space right after the marker is dropped");
        filter.Finish().ShouldBeEmpty();
        filter.Found.ShouldBeTrue();
    }

    [Fact]
    public void Text_after_the_marker_in_the_same_piece_is_shown()
    {
        var filter = new NotFoundFilter();

        filter.Push($"{Marker} Nicht gefunden.").ShouldBe("Nicht gefunden.");
        filter.Found.ShouldBeTrue();
    }

    [Fact]
    public void A_start_that_only_looked_like_the_marker_is_shown_in_full()
    {
        var filter = new NotFoundFilter();

        filter.Push("[[NOT").ShouldBeEmpty();
        filter.Push("E]] see below").ShouldBe("[[NOTE]] see below");
        filter.Push(" more").ShouldBe(" more");
        filter.Found.ShouldBeFalse();
    }

    [Fact]
    public void An_answer_ending_while_still_held_back_is_shown_at_the_end()
    {
        var filter = new NotFoundFilter();

        filter.Push("[[").ShouldBeEmpty();
        filter.Finish().ShouldBe("[[");
        filter.Found.ShouldBeFalse();
    }

    [Fact]
    public void A_marker_later_in_the_answer_is_not_held_back()
    {
        var filter = new NotFoundFilter();

        filter.Push("Partly: ").ShouldBe("Partly: ");
        filter.Push(Marker).ShouldBe(Marker); // too late to hide; removed when saved (see Clean)
    }

    [Theory]
    [InlineData($"{Marker} Not in the documents.", "Not in the documents.", true)]
    [InlineData($"Partly answered. {Marker}", "Partly answered.", true)]
    [InlineData("  A normal answer [1].  ", "A normal answer [1].", false)]
    [InlineData(Marker, "", true)]
    public void Saved_answers_never_contain_the_marker(string answer, string saved, bool found)
    {
        NotFoundFilter.Clean(answer, out var wasFound).ShouldBe(saved);
        wasFound.ShouldBe(found);
    }
}
