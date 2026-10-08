using Microsoft.Playwright;

namespace KonturExport;

internal static class PageReuse
{
    public static async Task<IPage> BlankOrNewAsync(IBrowserContext context)
    {
        // Persistent context обычно уже содержит стартовую about:blank.
        // Используем её, а не создаём вторую вкладку и оставляем первую пустой.
        IPage? blank = context.Pages.FirstOrDefault(p => !p.IsClosed &&
            (p.Url == "about:blank" || string.IsNullOrWhiteSpace(p.Url)));
        return blank ?? await context.NewPageAsync();
    }
}
