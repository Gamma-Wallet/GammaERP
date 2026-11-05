using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace Gamma.Erp.Services.Printing;

/// <summary>
/// A list or a report as an Excel sheet: one row per item, one column per field (its
/// name in words), numbers and dates as Excel values; nested lists are left out.
/// </summary>
public static partial class ExcelExport
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Write<T>(IEnumerable<T> rows, string sheet)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var columns = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && (p.PropertyType == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(p.PropertyType))).ToList();
        using var book = new XLWorkbook();
        var ws = book.Worksheets.Add(Safe(sheet));
        for (var c = 0; c < columns.Count; c++)
        {
            ws.Cell(1, c + 1).Value = Words().Replace(columns[c].Name, " $1").Trim();
            ws.Cell(1, c + 1).Style.Font.Bold = true;
        }

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (columns[c].GetValue(row))
                {
                    case null:
                        break;
                    case decimal d:
                        cell.Value = d;
                        cell.Style.NumberFormat.Format = "#,##0.00";
                        break;
                    case int or long or double:
                        cell.Value = Convert.ToDouble(columns[c].GetValue(row), System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case DateOnly day:
                        cell.Value = day.ToDateTime(TimeOnly.MinValue);
                        cell.Style.DateFormat.Format = "yyyy-mm-dd";
                        break;
                    case DateTimeOffset time:
                        cell.Value = time.UtcDateTime;
                        cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                        break;
                    case bool b:
                        cell.Value = b;
                        break;
                    case var other:
                        cell.Value = other.ToString();
                        break;
                }
            }

            r++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 200));
        using var output = new MemoryStream();
        book.SaveAs(output);
        return output.ToArray();
    }

    // A sheet name: at most 31 characters, none of : \ / ? * [ ].
    private static string Safe(string name) => new string([.. name.Where(ch => !":\\/?*[]".Contains(ch, StringComparison.Ordinal))])[..Math.Min(name.Length, 31)] is { Length: > 0 } s ? s : "Sheet";

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex Words();
}
