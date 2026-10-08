using System.Diagnostics;
using Microsoft.Playwright;

namespace KonturExport;

internal static class ExportFlow
{
    public static async Task<OutputResult> RunAsync(IBrowserContext context, IPage workPage, Settings settings, RunLog log)
    {
        using var observer = new DownloadObserver(context, log);
        bool confirmationClicked = false;
        var timer = Stopwatch.StartNew();
        TimeSpan timeout = TimeSpan.FromSeconds(settings.ExportTimeoutSeconds);

        // Если диалог уже открыт, НЕ нажимаем первую кнопку повторно.
        var existing = await FindConfirmationAsync(context, settings.ExportButtonName, workPage);
        if (existing != null)
        {
            confirmationClicked = true;
            await ClickWithDownloadTrackingAsync(existing, observer, log, settings.UiTimeoutSeconds);
        }
        else
        {
            log.Info("Клик #1: открыть выгрузку (или начать файл, если диалога нет).");
            ILocator mainButton = workPage.GetByRole(AriaRole.Button,
                new() { Name = settings.ExportButtonName, Exact = true }).First;
            await ClickWithDownloadTrackingAsync(mainButton, observer, log, settings.UiTimeoutSeconds);
        }

        // Реагируем на диалог ИЛИ на скачивание. Главную кнопку повторно не нажимаем.
        while (!observer.Task.IsCompleted)
        {
            if (timer.Elapsed >= timeout)
                throw new System.TimeoutException(
                    $"За {settings.ExportTimeoutSeconds} сек. сайт не начал скачивание. " +
                    $"Подтверждение диалога нажато: {confirmationClicked}. " +
                    "Проверьте окно выгрузки, доступ по тарифу и названия кнопок в settings.json.");
            if (!confirmationClicked)
            {
                ILocator? confirm = await FindConfirmationAsync(context, settings.ExportButtonName, workPage);
                if (confirm != null)
                {
                    confirmationClicked = true;
                    log.Info("Клик #2: подтверждение внутри диалога, ровно один раз.");
                    await ClickWithDownloadTrackingAsync(confirm, observer, log, settings.UiTimeoutSeconds);
                }
            }
            await Task.WhenAny(observer.Task, Task.Delay(200));
        }
        IDownload download = await observer.Task;
        log.Info("Скачивание перехвачено. Дожидаемся файла и сохраняем постоянную копию.");
        return await OutputFiles.SaveAsync(download, settings, log);
    }

    private static async Task<ILocator?> FindConfirmationAsync(IBrowserContext context, string name, IPage preferred)
    {
        var candidates = new[] { preferred }.Concat(context.Pages).Distinct().Where(p => !p.IsClosed).ToArray();
        foreach (IPage page in candidates)
        {
            try
            {
                ILocator button = page.GetByRole(AriaRole.Dialog)
                    .GetByRole(AriaRole.Button, new() { Name = name, Exact = true }).First;
                if (await button.IsVisibleAsync() && await button.IsEnabledAsync()) return button;
            }
            catch (PlaywrightException ex) when (PlaywrightErrors.IsTargetClosed(ex)) { /* Popup мог закрыться; проверим оставшиеся страницы. */ }
        }
        return null;
    }

    private static async Task ClickWithDownloadTrackingAsync(
        ILocator button, DownloadObserver observer, RunLog log, int timeoutSeconds)
    {
        try { await button.ClickAsync(new() { Timeout = timeoutSeconds * 1000 }); }
        catch (PlaywrightException ex) when (PlaywrightErrors.IsTargetClosed(ex))
        {
            // Экспортный popup иногда закрывается сразу после начала файла.
            // Если событие Download уже пришло, закрытие вкладки не мешает SaveAsAsync.
            await Task.WhenAny(observer.Task, Task.Delay(1000));
            if (observer.Task.IsCompletedSuccessfully)
                log.Warn("Вкладка закрылась после начала скачивания. Файл перехвачен, продолжаем сохранение.");
            else
                throw;
        }
    }
}
