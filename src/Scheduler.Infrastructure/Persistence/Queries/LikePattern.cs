namespace Scheduler.Infrastructure.Persistence.Queries;

internal static class LikePattern
{
    public const string Escape = "\\";

    /// <summary>A "contains" pattern for ILIKE, with the wildcard characters in the user's text escaped.</summary>
    public static string Contains(string text) =>
        "%" + text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
