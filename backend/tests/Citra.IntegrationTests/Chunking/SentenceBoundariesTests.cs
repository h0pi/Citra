using Citra.Application.Common.Chunking;

namespace Citra.IntegrationTests.Chunking;

/// <summary>Where sentences start. "|" in the expected text marks each boundary.</summary>
public sealed class SentenceBoundariesTests
{
    [Theory]
    // Plain sentence ends.
    [InlineData("One. Two! Three? Four.", "One. |Two! |Three? |Four.")]
    [InlineData("Wait... What?! Really.", "Wait... |What?! |Really.")]
    [InlineData("He said \"Yes.\" Then left.", "He said \"Yes.\" |Then left.")]
    [InlineData("(See below.) Next part.", "(See below.) |Next part.")]
    [InlineData("Ends with ellipsis… Next one.", "Ends with ellipsis… |Next one.")]
    [InlineData("Price is 30. 25 are left.", "Price is 30. |25 are left.")]
    [InlineData("Radno vrijeme. Čak i subotom.", "Radno vrijeme. |Čak i subotom.")]
    // Not sentence ends.
    [InlineData("Version 3.5 is out. Update now.", "Version 3.5 is out. |Update now.")]
    [InlineData("Visit acme.com today. Thanks.", "Visit acme.com today. |Thanks.")]
    [InlineData("Bring documents, e.g. Your passport. Done.", "Bring documents, e.g. Your passport. |Done.")]
    [InlineData("Ask Dr. Smith or Mrs. Jones. They help.", "Ask Dr. Smith or Mrs. Jones. |They help.")]
    [InlineData("Signed by J. Smith today. Next.", "Signed by J. Smith today. |Next.")]
    [InlineData("See No. 5 and Fig. 2 above. Next.", "See No. 5 and Fig. 2 above. |Next.")]
    [InlineData("Donesite npr. Pasoš ili ličnu kartu. Hvala.", "Donesite npr. Pasoš ili ličnu kartu. |Hvala.")]
    [InlineData("Vidi čl. 5 i str. 12. Kraj.", "Vidi čl. 5 i str. 12. |Kraj.")]
    [InlineData("This ends. and continues lowercase.", "This ends. and continues lowercase.")]
    // Line breaks: wrapped lines are not boundaries, structure is.
    [InlineData("Employees must read\nthe handbook carefully. Then sign.", "Employees must read\nthe handbook carefully. |Then sign.")]
    [InlineData("Steps:\nOpen the app\nClick return", "Steps:\n|Open the app\nClick return")]
    [InlineData("Intro\n- First item\n- Second item", "Intro\n|- First item\n|- Second item")]
    [InlineData("Intro\n1. First\n2) Second", "Intro\n|1. First\n|2) Second")]
    [InlineData("1. First step.\n2. Second step.", "1. First step.\n|2. Second step.")]
    [InlineData("Total was 12. 5 people came.", "Total was 12. |5 people came.")]
    [InlineData("Table:\nRole | Days\nStaff | 25", "Table:\n|Role | Days\n|Staff | 25")]
    [InlineData("Text\n## Heading", "Text\n|## Heading")]
    [InlineData("Line one.\nLine two.", "Line one.\n|Line two.")]
    public void Finds_sentence_starts(string text, string expected)
    {
        var starts = SentenceBoundaries.Starts(text, 0, text.Length);

        var marked = text;
        foreach (var position in starts.OrderDescending())
        {
            marked = marked.Insert(position, "|");
        }

        marked.ShouldBe(expected);
    }

    [Fact]
    public void Returns_increasing_positions_at_non_whitespace_inside_the_range()
    {
        const string text = "Alpha one. Beta two.   Gamma three!\n\n- Delta\n- Epsilon. Zeta.";

        var starts = SentenceBoundaries.Starts(text, 0, text.Length);

        starts.ShouldBe(starts.Order().Distinct().ToList());
        starts.ShouldAllBe(p => p > 0 && p < text.Length && !char.IsWhiteSpace(text[p]));
    }

    [Fact]
    public void Only_looks_inside_the_given_range()
    {
        const string text = "First. Second. Third. Fourth.";
        var secondStart = text.IndexOf("Second", StringComparison.Ordinal);
        var fourthStart = text.IndexOf("Fourth", StringComparison.Ordinal);

        var starts = SentenceBoundaries.Starts(text, secondStart, fourthStart - 1);

        starts.ShouldBe([text.IndexOf("Third", StringComparison.Ordinal)]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("No punctuation at all")]
    [InlineData("Ends at the very end.")]
    [InlineData("   ")]
    public void Texts_without_inner_boundaries_have_none(string text) =>
        SentenceBoundaries.Starts(text, 0, text.Length).ShouldBeEmpty();
}
