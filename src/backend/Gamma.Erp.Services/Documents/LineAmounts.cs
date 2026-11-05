namespace Gamma.Erp.Services.Documents;

/// <summary>
/// A transaction's amounts: value = (price − discount per unit) × quantity, VAT = value × the VAT group's rate / 100.
/// Moving the formula into each document type's configuration is the next step's plan (prices with or without VAT, discount per unit or %).
/// </summary>
public static class LineAmounts
{
    public static (decimal Value, decimal Vat) Of(decimal price, decimal discount, decimal quantity, decimal rate)
    {
        var value = (price - discount) * quantity;
        return (value, value * rate / 100);
    }

    /// <summary>
    /// From the value with VAT, when it is typed: VAT = with VAT / (100 + rate) × rate,
    /// value = the rest, unit price = value / quantity + discount.
    /// </summary>
    public static (decimal Price, decimal Value, decimal Vat) FromValueWithVat(decimal valueWithVat, decimal discount, decimal quantity, decimal rate)
    {
        var vat = valueWithVat * rate / (100 + rate);
        var value = valueWithVat - vat;
        return (quantity == 0 ? 0 : (value / quantity) + discount, value, vat);
    }
}
