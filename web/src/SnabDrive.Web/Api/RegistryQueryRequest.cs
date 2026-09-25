using System.Security.Claims;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Api;

/// <summary>Помощники для получения текущего пользователя из HTTP-контекста.</summary>
public static class ActorExtensions
{
    public static ChangeActor ToActor(this ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "-";
        var userName = principal.Identity?.Name ?? "неизвестный";
        return new ChangeActor(userId, userName);
    }
}

/// <summary>Параметры выборки реестра для Web API.</summary>
public sealed class RegistryQueryRequest
{
    public string? Search { get; set; }
    public string SortBy { get; set; } = RegistryQuery.DefaultSort;
    public bool SortDescending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    public int? TypeOfPurchaseId { get; set; }
    public int? B2BStatusId { get; set; }
    public int? ExecutionStatusId { get; set; }
    public bool? IsFinished { get; set; }

    /// <summary>Фильтры по колонкам: ?f[Customer]=газ&amp;f[NMCK]=&gt;=1000000.</summary>
    public Dictionary<string, string> F { get; set; } = new();

    public RegistryQuery ToQuery() => new()
    {
        Search = Search,
        SortBy = string.IsNullOrWhiteSpace(SortBy) ? RegistryQuery.DefaultSort : SortBy,
        SortDescending = SortDescending,
        Page = Page,
        PageSize = PageSize,
        TypeOfPurchaseId = TypeOfPurchaseId,
        B2BStatusId = B2BStatusId,
        ExecutionStatusId = ExecutionStatusId,
        IsFinished = IsFinished,
        ColumnFilters = F
    };
}
