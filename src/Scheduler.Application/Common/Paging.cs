namespace Scheduler.Application.Common;

/// <summary>A page of a list. Pages are numbered from 1.</summary>
public sealed record PageRequest(int Page = 1, int PageSize = 25)
{
    public const int MaxPageSize = 200;

    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);
    public int Skip => (SafePage - 1) * SafePageSize;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int PageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < PageCount;

    public static PagedResult<T> Empty(PageRequest page) => new([], 0, page.SafePage, page.SafePageSize);
}
