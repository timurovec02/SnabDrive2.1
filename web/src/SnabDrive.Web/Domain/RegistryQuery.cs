namespace SnabDrive.Web.Domain;

/// <summary>Параметры выборки реестра: поиск, сортировка, фильтры по колонкам, пагинация.</summary>
public sealed record RegistryQuery
{
    public const int MaxPageSize = 500;

    public const string DefaultSort = "Id";

    /// <summary>Глобальный поиск по всем текстовым полям, суммам и датам (как в WPF-версии).</summary>
    public string? Search { get; init; }

    public string SortBy { get; init; } = DefaultSort;

    public bool SortDescending { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;

    /// <summary>Фильтры «колонка -> значение». Пустые значения игнорируются.</summary>
    public IReadOnlyDictionary<string, string> ColumnFilters { get; init; }
        = new Dictionary<string, string>();

    public int? TypeOfPurchaseId { get; init; }
    public int? B2BStatusId { get; init; }
    public int? ExecutionStatusId { get; init; }
    public bool? IsFinished { get; init; }

    public RegistryQuery Normalize()
    {
        var page = Page < 1 ? 1 : Page;
        var pageSize = PageSize switch
        {
            < 5 => 5,
            > MaxPageSize => MaxPageSize,
            _ => PageSize
        };

        return this with { Page = page, PageSize = pageSize };
    }
}
