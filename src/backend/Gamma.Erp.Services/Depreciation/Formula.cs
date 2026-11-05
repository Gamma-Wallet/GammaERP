using System.Globalization;

namespace Gamma.Erp.Services.Depreciation;

/// <summary>
/// Evaluates a depreciation method's formula (tbl_depreciation_method.formula, e.g. "(VF/DT)*DK"):
/// numbers, the variables it is given, + − * / ^ and brackets. Anything else is refused, so a formula can never run code.
/// </summary>
public static class Formula
{
    /// <summary>Sample values of the variables a depreciation formula may use: a method's formula is tried on them when it is saved.</summary>
    public static readonly IReadOnlyDictionary<string, decimal> Sample = new Dictionary<string, decimal>
    {
        ["VF"] = 1000, ["DT"] = 365, ["DK"] = 30, ["YK"] = 1, ["PD"] = 0.2m, ["PL"] = 0.2m,
    };

    public static decimal Evaluate(string formula, IReadOnlyDictionary<string, decimal> variables)
    {
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(variables);
        var parser = new Parser(formula, variables);
        var value = parser.Sum();
        return parser.AtEnd ? value : throw parser.Error("unexpected text");
    }

    private sealed class Parser(string text, IReadOnlyDictionary<string, decimal> variables)
    {
        private int _at;

        public bool AtEnd
        {
            get
            {
                Skip();
                return _at == text.Length;
            }
        }

        public decimal Sum()
        {
            var value = Product();
            while (Next('+', '-') is { } op)
            {
                var right = Product();
                value = op == '+' ? value + right : value - right;
            }

            return value;
        }

        private decimal Product()
        {
            var value = Power();
            while (Next('*', '/') is { } op)
            {
                var right = Power();
                value = op == '*' ? value * right : right == 0 ? throw Error("division by zero") : value / right;
            }

            return value;
        }

        private decimal Power()
        {
            var value = Unary();
            if (Next('^') is not null)
            {
                var exponent = Power();
                value = (decimal)Math.Pow((double)value, (double)exponent);
            }

            return value;
        }

        private decimal Unary() => Next('-') is not null ? -Unary() : Next('+') is not null ? Unary() : Atom();

        private decimal Atom()
        {
            Skip();
            if (Next('(') is not null)
            {
                var value = Sum();
                return Next(')') is not null ? value : throw Error("a bracket is not closed");
            }

            var start = _at;
            if (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] == '.'))
            {
                while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] == '.'))
                {
                    _at++;
                }

                return decimal.Parse(text[start.._at], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            }

            while (_at < text.Length && char.IsLetter(text[_at]))
            {
                _at++;
            }

            var name = text[start.._at];
            return name.Length == 0 ? throw Error("a number or a variable is missing")
                : variables.TryGetValue(name, out var v) ? v : throw Error($"unknown variable {name}");
        }

        private char? Next(params char[] ops)
        {
            Skip();
            if (_at < text.Length && ops.Contains(text[_at]))
            {
                return text[_at++];
            }

            return null;
        }

        private void Skip()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at]))
            {
                _at++;
            }
        }

        public ValidationException Error(string what) =>
            ValidationException.For("formula", $"{ErrorCodes.NotConfiguredError}: the formula \"{text}\" can't be worked out at {_at + 1}: {what}.");
    }
}
