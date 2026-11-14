using Kazo.Deck.Documents;

namespace Kazo.Deck.Tests;

/// <summary>The files of a multi-file export follow the first one (doc/en/plan.md, work item 3).</summary>
public sealed class CopySeriesTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Plan1.png", "Plan2.png", true)]
    [InlineData("Plan9.png", "Plan10.png", true)]
    [InlineData("Presentation19.png", "Presentation110.png", true)]
    [InlineData("Plan1.png", "Plan3.png", false)]
    [InlineData("Plan1.png", "Plan2.jpg", false)]
    [InlineData("Plan1.png", "Other2.png", false)]
    [InlineData("Plan.png", "Plan1.png", false)]
    [InlineData("Plan1.png", "Plan.png", false)]
    [InlineData("Plan.pdf", "Plan.pdf", false)]
    public void Names_follow_each_other_by_their_number(string previous, string next, bool follows)
    {
        Assert.Equal(follows, CopySeries.Follows(previous, next));
    }

    [Fact]
    public void The_next_file_of_a_series_goes_to_the_first_one_s_folder()
    {
        CopySeries series = new();
        Assert.Null(series.FolderFor("Plan1.png", Start));

        series.Written("Plan1.png", "/home/neo/Pictures/cover.png", Start);

        Assert.Equal("/home/neo/Pictures", series.FolderFor("Plan2.png", Start.AddSeconds(1)));
    }

    [Fact]
    public void A_series_ends_after_its_window_or_on_another_name()
    {
        CopySeries series = new();
        series.Written("Plan1.png", "/home/neo/a/Plan1.png", Start);

        Assert.Null(series.FolderFor("Plan2.png", Start + CopySeries.Window + TimeSpan.FromSeconds(1)));
        Assert.Null(series.FolderFor("Plan.pdf", Start.AddSeconds(1)));

        series.Reset();
        Assert.Null(series.FolderFor("Plan2.png", Start.AddSeconds(1)));
    }
}
