using System.Globalization;

namespace Scheduler.Admin.Services;

/// <summary>
/// Formats values for the pages. Times are stored in UTC and shown in the computer's local time (FR-14);
/// durations are shown in the unit that reads best.
/// </summary>
public static class Display
{
    public const string Empty = "–";

    public static string Time(DateTime? utc) =>
        utc is { } value ? ToLocal(value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : Empty;

    public static string ShortTime(DateTime? utc) =>
        utc is { } value ? ToLocal(value).ToString("dd MMM HH:mm", CultureInfo.InvariantCulture) : Empty;

    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    /// <summary>Converts a local date-time picked in a form to UTC for a query.</summary>
    public static DateTime? ToUtc(DateTime? local) =>
        local is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime() : null;

    public static string Duration(double? milliseconds) => milliseconds switch
    {
        null => Empty,
        < 1000 => $"{milliseconds:0} ms",
        < 60_000 => $"{milliseconds / 1000:0.0} s",
        < 3_600_000 => $"{milliseconds / 60_000:0.0} min",
        _ => $"{milliseconds / 3_600_000:0.0} h"
    };

    public static string Relative(DateTime? utc, DateTime nowUtc)
    {
        if (utc is not { } value)
        {
            return Empty;
        }

        var span = value - nowUtc;
        var future = span > TimeSpan.Zero;
        var size = span.Duration();
        var text = size.TotalSeconds < 60 ? $"{size.TotalSeconds:0} s"
            : size.TotalMinutes < 60 ? $"{size.TotalMinutes:0} min"
            : size.TotalHours < 48 ? $"{size.TotalHours:0} h"
            : $"{size.TotalDays:0} days";
        return future ? $"in {text}" : $"{text} ago";
    }

    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB"
    };

    /// <summary>Splits an enum name into words, for example TimedOut becomes "Timed out".</summary>
    public static string Words(Enum value)
    {
        var name = value.ToString();
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(name[i]));
            }
            else
            {
                builder.Append(name[i]);
            }
        }

        return builder.ToString();
    }
}
