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

    [Fact]
    public void HudWalkthroughIncludesTheNewPartyCooldownView()
    {
        Assert.Contains(OnboardingGuide.Steps, step => step.Section == GuideSection.Widgets &&
            step.Target == GuideTarget.PartyCooldowns);
        Assert.Equal(GuideTarget.PartyCooldowns, OnboardingGuide.Steps[
            OnboardingGuide.FirstStepFor(GuideTarget.PartyCooldowns)].Target);
    }

    [Fact]
    public void DeathWalkthroughUsesTheCurrentWindowName()
    {
        var step = OnboardingGuide.Steps[OnboardingGuide.FirstStepFor(GuideTarget.DeathWidget)];

        Assert.Equal("Set up the Death Window", step.Title);
        Assert.Contains("Death Window", step.Description);
        Assert.Contains("Death Window", step.TryThis);
    }

    [Fact]
    public void CooldownWalkthroughCoversSelectionLayoutAndPreviewInOrder()
    {
        var targets = new[] { GuideTarget.PartyCooldowns, GuideTarget.PartyCooldownLayout, GuideTarget.PartyCooldownPreview };
        var steps = OnboardingGuide.Steps.Where(step => OnboardingGuide.IsPartyCooldownTarget(step.Target)).ToArray();
        Assert.Equal(targets, steps.Select(step => step.Target));
        Assert.All(steps, step => Assert.Equal(GuideSection.Widgets, step.Section));
        foreach (var target in targets)
            Assert.Equal(target, OnboardingGuide.Steps[OnboardingGuide.FirstStepFor(target)].Target);
        Assert.False(OnboardingGuide.IsPartyCooldownTarget(null));
        Assert.False(OnboardingGuide.IsPartyCooldownTarget(GuideTarget.DeathWidget));
        Assert.False(OnboardingGuide.IsPartyCooldownTarget(GuideTarget.MeterPreview));
    }

    [Fact]
    public void CooldownPreviewIsExplicitlySampleDataAndLayoutDoesNotEnableTheOverlay()
    {
        var preview = OnboardingGuide.Steps[OnboardingGuide.FirstStepFor(GuideTarget.PartyCooldownPreview)];
        var layout = OnboardingGuide.Steps[OnboardingGuide.FirstStepFor(GuideTarget.PartyCooldownLayout)];
        Assert.Contains("example timers", preview.Description);
        Assert.Contains("Left and right", layout.Description);
        Assert.Contains("Enable Show party cooldowns", layout.Description);
    }

    private static RecordedPullSummary CreatePull(long number, int deaths) =>
        new(System.DateTime.UtcNow, "Combat ended", 1, "Duty", 60, deaths) { PullNumber = number };
}
