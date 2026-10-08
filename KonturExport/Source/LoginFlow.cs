using System.Diagnostics;
using Microsoft.Playwright;

namespace KonturExport;

internal sealed class LoginFlow(IBrowserContext context, Settings settings, RunLog log)
{
    private string ReadyMarker => settings.ProfileDirectory + ".ready";

    public async Task<IPage> GetAuthenticatedPageAsync(bool forceLogin, bool headless)
    {
        if (!forceLogin && File.Exists(ReadyMarker))
        {
            IPage saved = await OpenWorkPageAsync();
            if (await TryEnterFavoritesAsync(saved)) return saved;
            log.Warn("Сессия истекла. Выполняем вход по сертификату.");
        }
        if (headless) throw new LoginRequiredException("Для нового входа по сертификату нужен видимый Login.cmd.");

        log.Info("ШАГ 1/3: главная вкладка → zakupki.kontur.ru → Войти.");
        IPage homeTab = await PageReuse.BlankOrNewAsync(context);
        await homeTab.GotoAsync(settings.StartUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await homeTab.BringToFrontAsync();
        IPage? authTab = await OpenAuthenticationTabAsync(homeTab);
        if (authTab == null)
        {
            // При уже активной сессии кнопки входа может не быть. Не выходим из аккаунта.
            IPage saved = await OpenWorkPageAsync();
            if (await TryEnterFavoritesAsync(saved)) return MarkReady(saved);
            throw new LoginRequiredException("Не найдены «Войти» или новая вкладка входа. Проверьте главную страницу.");
        }
        await authTab.BringToFrontAsync();
        log.Info("ШАГ 2/3: новая вкладка авторизации → Сертификат → Титов.");

        // Наблюдатель создаётся ДО клика по сертификату. Старые вкладки сюда не попадут.
        using var workTabs = new TabCapture(context);
        if (settings.UseCertificateLogin)
        {
            CertificateSelection result = await CertificateLogin.SelectAsync(context, settings, log, authTab);
            log.Info("Вход по сертификату: " + result.Reason);
            if (result.Selected)
            {
                IPage? work = await WaitForWorkPageAsync(workTabs, authTab, result.AuthPage, homeTab);
                if (work != null) return MarkReady(work);
            }
        }
        if (Console.IsInputRedirected) throw new LoginRequiredException("Не завершён вход или не загрузилась рабочая страница. Используйте видимый Login.cmd.");
        Console.WriteLine($"Завершите вход сертификатом «{settings.CertificateOwnerName}» в вкладке авторизации.");
        Console.WriteLine("SMS не ожидается. PIN токена, если он нужен, вводится в системном окне.");
        Console.WriteLine("Когда загрузится кабинет закупок (в этой же или новой вкладке), нажмите Enter здесь.");
        if (Console.ReadLine() == null) throw new LoginRequiredException("Нужно интерактивное подтверждение.");
        IPage? manual = await WaitForWorkPageAsync(workTabs, authTab, null, homeTab);
        if (manual != null) return MarkReady(manual);
        throw new LoginRequiredException("Не загрузилась рабочая страница с Избранным. Проверьте авторизацию; посторонние старые вкладки не выбираются.");
    }

    private async Task<IPage?> OpenAuthenticationTabAsync(IPage home)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < settings.UiTimeoutSeconds && !home.IsClosed)
        {
            foreach (AriaRole role in new[] { AriaRole.Menuitem, AriaRole.Button, AriaRole.Link })
            {
                ILocator signIn = home.GetByRole(role, new() { Name = "Войти", Exact = true }).First;
                if (!await signIn.IsVisibleAsync()) continue;
                using var authTabs = new TabCapture(context);
                // Никакой дополнительной навигации: только прямой «Войти» в главной вкладке.
                await signIn.ClickAsync(new() { Timeout = settings.UiTimeoutSeconds * 1000 });
                IPage auth = await authTabs.WaitFirstAsync(settings.UiTimeoutSeconds);
                log.Info("Вкладка авторизации перехвачена после «Войти».");
                return auth;
            }
            if (await home.GetByRole(AriaRole.Link, new() { Name = settings.FavoritesName, Exact = true }).First.IsVisibleAsync()) return null;
            await Task.Delay(200);
        }
        return null;
    }

    private async Task<IPage?> WaitForWorkPageAsync(TabCapture openedAfterCertificate, IPage authTab, IPage? selectedAuthPage, IPage homeTab)
    {
        string host = new Uri(settings.ApplicationUrl).Host;
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < settings.CertificateLoginTimeoutSeconds)
        {
            // Навигация существующего IPage НЕ порождает событие context.Page.
            // Поэтому проверяем вкладку сертификата, её связанную страницу, главную
            // и новые вкладки шага — но не все старые страницы профиля.
            IEnumerable<IPage> candidates = new[] { selectedAuthPage, authTab, homeTab }
                .OfType<IPage>().Concat(openedAfterCertificate.Snapshot()).Distinct().Where(p => !p.IsClosed);
            foreach (IPage tab in candidates)
            {
                // Ждём реальный адрес и интерфейс независимо от появления новой вкладки.
                if (!Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri) ||
                    !uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (!string.IsNullOrWhiteSpace(settings.RightSidebarSelector))
                        await tab.Locator(settings.RightSidebarSelector).HoverAsync(new() { Timeout = 1000 });
                    if (!await tab.GetByRole(AriaRole.Link, new() { Name = settings.FavoritesName, Exact = true }).First.IsVisibleAsync()) continue;
                    await tab.BringToFrontAsync();
                    log.Info("ШАГ 3/3: рабочая страница загрузилась " + (tab == authTab || tab == selectedAuthPage ? "в вкладке авторизации" : tab == homeTab ? "в главной вкладке" : "в новой вкладке") + " → Избранное.");
                    if (await TryEnterFavoritesAsync(tab)) return tab;
                }
                catch (System.TimeoutException) { }
                catch (PlaywrightException ex) when (PlaywrightErrors.IsTargetClosed(ex)) { }
            }
            if (context.Browser?.IsConnected == false) throw new InvalidOperationException("Браузер отключился при ожидании рабочей страницы.");
            await Task.Delay(200);
        }
        return null;
    }

    private IPage MarkReady(IPage page)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReadyMarker)!);
        File.WriteAllText(ReadyMarker, DateTimeOffset.Now.ToString("O"));
        log.Info("Доступ к Избранному в рабочей вкладке подтверждён. Продолжаем выгрузку Excel.");
        return page;
    }
    private async Task<IPage> OpenWorkPageAsync()
    {
        IPage page = await PageReuse.BlankOrNewAsync(context);
        await page.GotoAsync(settings.ApplicationUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.BringToFrontAsync();
        return page;
    }
    private async Task<bool> TryEnterFavoritesAsync(IPage page)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.RightSidebarSelector))
                await page.Locator(settings.RightSidebarSelector).HoverAsync(new() { Timeout = settings.UiTimeoutSeconds * 1000 });
            await page.GetByRole(AriaRole.Link, new() { Name = settings.FavoritesName, Exact = true })
                .ClickAsync(new() { Timeout = settings.UiTimeoutSeconds * 1000 });
            await page.GetByRole(AriaRole.Button, new() { Name = settings.ExportButtonName, Exact = true })
                .First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = settings.UiTimeoutSeconds * 1000 });
            return true;
        }
        catch (System.TimeoutException) { return false; }
        catch (PlaywrightException ex) when (PlaywrightErrors.IsTargetClosed(ex)) { return false; }
    }
}
