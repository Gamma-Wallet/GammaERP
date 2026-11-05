using AwesomeAssertions;
using Gamma.Erp.Seed;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>The activity catalogue: a tree chosen level by level; a profile is inherited from the nearest level that has one.</summary>
public sealed class ActivityCatalogueTests
{
    [Fact]
    public void An_activity_is_reached_level_by_level_and_without_a_profile_takes_the_full_configuration()
    {
        ActivityCatalogue.PathOf("BAR").Select(a => a.Code).Should().Equal("GENERAL", "SERVICE", "HOSPITALITY", "BAR");
        ActivityCatalogue.PathOf("FLOWER_SHOP").Select(a => a.Code).Should().Equal("GENERAL", "RETAIL", "FLOWER_SHOP");
        ActivityCatalogue.All.Should().OnlyContain(a => a.Name.ContainsKey("en") && a.Name.ContainsKey("sq") && a.Name.ContainsKey("it") && a.Name.ContainsKey("el"));
        ActivityCatalogue.ProfileFor("AL", "BAR").Should().Be("HOSPITALITY", "the nearest profile up its path");
        ActivityCatalogue.ProfileFor("AL", "GENERAL").Should().BeNull("General is the country's full configuration");
        var unknown = () => ActivityCatalogue.PathOf("SPACESHIP");
        unknown.Should().Throw<ArgumentException>();
        StarterPacks.Countries.Should().NotContain("_catalogue");
    }
}
