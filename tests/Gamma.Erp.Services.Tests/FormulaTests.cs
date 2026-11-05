using AwesomeAssertions;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Depreciation;
using Xunit;

namespace Gamma.Erp.Services.Tests;

/// <summary>The depreciation methods' formulas from the database, evaluated: numbers, variables and operators, nothing else.</summary>
public sealed class FormulaTests
{
    private static readonly Dictionary<string, decimal> Asset = new() { ["VF"] = 12000, ["DT"] = 1826, ["DK"] = 30, ["YK"] = 0, ["PD"] = 0.2m, ["PL"] = 0 };

    [Fact]
    public void The_old_linear_formula_gives_the_periods_share() =>
        Math.Round(Formula.Evaluate("(VF/DT)*DK", Asset), 2).Should().Be(197.15m);

    [Fact]
    public void The_old_declining_formula_gives_the_years_amount_pro_rata() =>
        Math.Round(Formula.Evaluate("((VF*PD*((1-PD)^YK))/365)*DK", Asset), 2).Should().Be(197.26m);

    [Theory]
    [InlineData("VF*XX", "unknown variable XX")]
    [InlineData("System.IO.File.Delete(1)", "unknown variable System")]
    [InlineData("(VF/DT", "a bracket is not closed")]
    [InlineData("VF/0", "division by zero")]
    public void Anything_but_arithmetic_on_known_variables_is_refused(string formula, string why)
    {
        var evaluate = () => Formula.Evaluate(formula, Asset);
        evaluate.Should().Throw<ValidationException>().Which.Message.Should().Contain(why);
    }
}
