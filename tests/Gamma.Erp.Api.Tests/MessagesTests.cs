using AwesomeAssertions;
using Xunit;

namespace Gamma.Erp.Api.Tests;

/// <summary>The services' refusals in the screens' language.</summary>
public sealed class MessagesTests
{
    [Fact]
    public void A_refusal_keeps_its_code_and_its_values_and_one_without_a_translation_stays_in_English()
    {
        Messages.Translate("The document does not exist.", "sq").Should().Be("Dokumenti nuk ekziston.");
        Messages.Translate("There is no cost centre WH-1 here.", "sq").Should().Be("Këtu nuk ka qendër kostoje WH-1.");
        Messages.Translate("NotAllowedError: this screen is not in your menu.", "sq").Should().Be("NotAllowedError: ky ekran nuk është në menunë tënde.");
        Messages.Translate("The place holds 5, of which 2 have a serial number: 9 more do not fit.", "sq").Should().Contain("5").And.Contain("2").And.Contain("9");
        Messages.Translate("Something nobody wrote down.", "sq").Should().Be("Something nobody wrote down.");
        Messages.Translate("The document does not exist.", "en").Should().Be("The document does not exist.");
        Messages.Translate("The document does not exist.", null).Should().Be("The document does not exist.");
        Messages.Translate("The document does not exist.", "xx").Should().Be("The document does not exist.", "a language without a file stays in English");
    }
}
