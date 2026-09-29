using System.Linq;
using BetterDeaths;
using Xunit;

namespace BetterDeaths.Tests;

public sealed class OnboardingGuideTests
{
    [Fact]
    public void WalkthroughUsesOneLabelForButtonAndSizing()
    {
        Assert.Equal("Walkthrough", OnboardingGuide.ButtonLabel);
    }

    [Fact]
    public void WalkthroughChoosesFirstSavedPullWithDeathsInVisibleOrder()
    {
        var empty = CreatePull(104, 0);
        var first = CreatePull(103, 2);
        var older = CreatePull(102, 8);

        Assert.Same(first, OnboardingGuide.SelectRecordedPull([empty, first, older]));
    }

    [Fact]
    public void WalkthroughRespectsFilteredListOrderRatherThanResortingIt()
    {
        var first = CreatePull(100, 1);
        var second = CreatePull(101, 3);

        Assert.Same(first, OnboardingGuide.SelectRecordedPull([first, second]));
    }

    [Fact]
    public void WalkthroughUsesSampleWhenNoSavedPullHasDeaths()
    {
        Assert.Null(OnboardingGuide.SelectRecordedPull([]));
        Assert.Null(OnboardingGuide.SelectRecordedPull([CreatePull(104, 0)]));
    }

    [Fact]
    public void GuideCoversEveryUserFacingSectionInNavigationOrder()
    {
        var expectedSections = new[]
        {
            GuideSection.Review,
            GuideSection.Replay,
            GuideSection.Widgets,
            GuideSection.Analyzer,
            GuideSection.Options,
            GuideSection.Data,
            GuideSection.Updates,
        };

        Assert.Equal(expectedSections, OnboardingGuide.Steps.Select(step => step.Section).Distinct());
        foreach (var section in expectedSections)
        {
            var index = OnboardingGuide.FirstStepFor(section);
            Assert.Equal(section, OnboardingGuide.Steps[index].Section);
            Assert.True(index == 0 || OnboardingGuide.Steps[index - 1].Section != section);
        }
    }

    [Fact]
    public void EveryStepHasAnExplanationAndAnAction()
    {
        Assert.True(OnboardingGuide.Steps.Count >= 14);
        Assert.Equal(GuideTarget.Navigation, OnboardingGuide.Steps[0].Target);
        foreach (var step in OnboardingGuide.Steps)
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Title));
            Assert.False(string.IsNullOrWhiteSpace(step.Description));
            Assert.False(string.IsNullOrWhiteSpace(step.TryThis));
        }
    }

    private static RecordedPullSummary CreatePull(long number, int deaths) =>
        new(System.DateTime.UtcNow, "Combat ended", 1, "Duty", 60, deaths) { PullNumber = number };
}
