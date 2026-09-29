using System.Linq;
using BetterDeaths;
using Xunit;

namespace BetterDeaths.Tests;

public sealed class OnboardingGuideTests
{
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

}
