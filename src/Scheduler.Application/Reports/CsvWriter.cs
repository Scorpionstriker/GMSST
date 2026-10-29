using System.Globalization;
using System.Text;

namespace Scheduler.Application.Reports;

/// <summary>
/// Writes UTF-8 CSV with a header row (FR-66). Values that spreadsheet programs would treat as formulae
/// are prefixed with an apostrophe, so an exported file cannot run content when it is opened (NFR-16).
/// </summary>
public static class CsvWriter
{
    private static readonly char[] FormulaStarters = ['=', '+', '-', '@', '\t', '\r'];

    public static string Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        AppendRow(builder, header);
        foreach (var row in rows)
        {
            AppendRow(builder, row);
        }

        return builder.ToString();
    }

    public static byte[] ToUtf8Bytes(string csv) => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();

    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Plain numbers stay numbers; anything else that starts like a formula is neutralised.
        if (Array.IndexOf(FormulaStarters, value[0]) >= 0 && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string?> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(values[i]));
        }

        builder.Append("\r\n");
    }
}

/// <summary>A generated CSV file: its suggested name and content.</summary>
public sealed record CsvFile(string FileName, string Content);
