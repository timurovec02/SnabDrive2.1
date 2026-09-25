namespace SnabDrive.Web.Domain;

/// <summary>Страница данных с общим количеством записей.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    public int TotalCount { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    public int FirstItemIndex => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;

    public int LastItemIndex => Math.Min(TotalCount, Page * PageSize);

    public static PagedResult<T> Empty(int page, int pageSize) =>
        new() { Items = Array.Empty<T>(), TotalCount = 0, Page = page, PageSize = pageSize };
}
