using System.Diagnostics;
using Microsoft.Playwright;

namespace KonturExport;

// Подписывается ДО клика и учитывает только действительно новые вкладки этого шага.
internal sealed class TabCapture : IDisposable
{
    private readonly IBrowserContext context;
    private readonly object gate = new();
    private readonly List<IPage> pages = [];
    private bool disposed;
    public TabCapture(IBrowserContext context)
    {
        this.context = context;
        context.Page += OnPage;
    }
    private void OnPage(object? sender, IPage page)
    {
        lock (gate) { if (!disposed) pages.Add(page); }
    }
    public IPage[] Snapshot() { lock (gate) return pages.Where(p => !p.IsClosed).ToArray(); }
    public async Task<IPage> WaitFirstAsync(int seconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < seconds)
        {
            IPage? page = Snapshot().FirstOrDefault();
            if (page != null) return page;
            if (context.Browser?.IsConnected == false) throw new InvalidOperationException("Браузер отключился при ожидании новой вкладки.");
            await Task.Delay(100);
        }
        throw new LoginRequiredException("После «Войти» не появилась новая вкладка авторизации. Проверьте вкладки в видимом браузере.");
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        context.Page -= OnPage;
    }
}
