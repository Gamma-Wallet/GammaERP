using AwesomeAssertions;
using Gamma.Erp.Models.Base;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>Every name in English and the company's language (the founder, 2026-11-02).</summary>
public sealed class TranslatedTextTests
{
    [Fact]
    public void A_name_in_one_language_takes_the_other_and_one_in_both_stays()
    {
        new TranslatedText { Values = { ["sq"] = "Kabull" } }.Completed("sq").Values.Should().Contain(new Dictionary<string, string> { ["en"] = "Kabull", ["sq"] = "Kabull" });
        new TranslatedText { Values = { ["en"] = "Cable" } }.Completed("sq").Values.Should().Contain(new Dictionary<string, string> { ["en"] = "Cable", ["sq"] = "Cable" });
        new TranslatedText { Values = { ["en"] = "Cable", ["sq"] = "Kabull" } }.Completed("sq").Values.Should().Contain(new Dictionary<string, string> { ["en"] = "Cable", ["sq"] = "Kabull" });
        new TranslatedText().Completed("sq").Values.Should().BeEmpty("no name stays no name");
        TranslatedText.Both("Arka", "en").Values.Should().ContainSingle();
    }
}
