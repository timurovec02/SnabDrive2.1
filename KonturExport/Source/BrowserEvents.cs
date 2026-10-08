using Microsoft.Playwright;

namespace KonturExport;

internal sealed class BrowserLifecycle : IDisposable
{
    private readonly object gate = new();
    private readonly IBrowserContext context;
    private readonly RunLog log;
    private readonly Dictionary<IPage, (EventHandler<IPage> Close, EventHandler<IPage> Crash)> handlers = new();
    private int nextId;
    private bool disposed;
    public BrowserLifecycle(IBrowserContext context, RunLog log)
    {
        this.context = context; this.log = log;
        context.Page += OnPage;
        context.Close += OnClose;
        foreach (IPage p in context.Pages) Attach(p);
    }
    private void OnPage(object? sender, IPage page) => Attach(page);
    private void OnClose(object? sender, IBrowserContext c) => log.Warn("EVENT: контекст браузера закрыт.");
    private void Attach(IPage page)
    {
        lock (gate)
        {
            if (disposed || handlers.ContainsKey(page)) return;
            int id = ++nextId;
            EventHandler<IPage> close = (_, _) => log.Info($"EVENT: вкладка #{id} закрыта.");
            EventHandler<IPage> crash = (_, _) => log.Error($"EVENT: вкладка #{id} сообщила о сбое renderer.");
            handlers.Add(page, (close, crash));
            page.Close += close; page.Crash += crash;
            log.Info($"EVENT: открыта вкладка #{id}.");
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            context.Page -= OnPage; context.Close -= OnClose;
            foreach (var entry in handlers)
            { entry.Key.Close -= entry.Value.Close; entry.Key.Crash -= entry.Value.Crash; }
        }
    }
}

// Перехват запускается ДО любого клика выгрузки и охватывает все вкладки / popup.
// Не привязываем ожидание только к одной странице, которая может закрыться.
internal sealed class DownloadObserver : IDisposable
{
    private readonly object gate = new();
    private readonly IBrowserContext context;
    private readonly RunLog log;
    private readonly HashSet<IPage> pages = new();
    private readonly TaskCompletionSource<IDownload> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool disposed;
    public Task<IDownload> Task => completion.Task;
    public DownloadObserver(IBrowserContext context, RunLog log)
    {
        this.context = context; this.log = log;
        context.Page += OnPage;
        context.Close += OnContextClose;
        foreach (IPage p in context.Pages) Attach(p);
    }
    private void OnPage(object? sender, IPage page) => Attach(page);
    private void Attach(IPage page)
    {
        lock (gate)
            if (!disposed && pages.Add(page)) page.Download += OnDownload;
    }
    private void OnDownload(object? sender, IDownload download)
    {
        if (completion.TrySetResult(download))
            log.Info($"EVENT: началось скачивание; имя от сайта: {download.SuggestedFilename}");
    }
    private void OnContextClose(object? sender, IBrowserContext c) => completion.TrySetException(
        new InvalidOperationException("Контекст браузера закрылся до начала скачивания. Смотрите события закрытия в журнале."));
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            context.Page -= OnPage; context.Close -= OnContextClose;
            foreach (IPage p in pages) p.Download -= OnDownload;
        }
    }
}
