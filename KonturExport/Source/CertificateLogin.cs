using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace KonturExport;

internal sealed record CertificateSelection(bool Selected, IPage? AuthPage, string Reason);

internal static class CertificateLogin
{
    public static Regex OwnerTextRegex(string name, bool exact = true)
    {
        string words = string.Join(@"\s+", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
        return new Regex(exact ? @"^\s*" + words + @"\s*$" : @"(?:^|\s)" + words + @"(?:$|\s|[,;(])", RegexOptions.IgnoreCase);
    }

    public static async Task<CertificateSelection> SelectAsync(IBrowserContext context, Settings settings, RunLog log, IPage? authenticationTab = null)
    {
        IPage? authPage = null;
        HashSet<IPage>? pagesBeforeCertificate = null;
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < settings.UiTimeoutSeconds)
        {
            foreach (IPage page in (authenticationTab == null ? context.Pages.Reverse() : new[] { authenticationTab }).Where(p => !p.IsClosed))
            {
                try
                {
                    ILocator[] controls =
                    [
                        page.GetByRole(AriaRole.Button, new() { Name = settings.CertificateButtonName, Exact = true }),
                        page.GetByRole(AriaRole.Tab, new() { Name = settings.CertificateButtonName, Exact = true }),
                        page.GetByRole(AriaRole.Link, new() { Name = settings.CertificateButtonName, Exact = true }),
                        page.GetByText(settings.CertificateButtonName, new() { Exact = true })
                    ];
                    foreach (ILocator control in controls)
                    {
                        var match = await SingleVisibleAsync(control);
                        if (match.Ambiguous) return new(false, page, "На странице несколько видимых элементов «Сертификат»; автоматический выбор остановлен.");
                        if (match.Locator == null) continue;
                        pagesBeforeCertificate = context.Pages.ToHashSet();
                        bool ownerAlreadyVisible = false;
                        foreach (ILocator ownerChoice in OwnerChoices(page, settings))
                        {
                            if ((await SingleVisibleAsync(ownerChoice, firstVisible: true)).Locator != null)
                            {
                                ownerAlreadyVisible = true;
                                break;
                            }
                        }
                        if (ownerAlreadyVisible)
                            log.Info("Список сертификатов уже открыт. Сразу выбираем владельца, вкладку повторно не нажимаем.");
                        else
                        {
                            log.Info($"Авторизация: нажимаем «{settings.CertificateButtonName}».");
                            await match.Locator.ClickAsync(new() { Timeout = settings.UiTimeoutSeconds * 1000 });
                        }
                        authPage = page;
                        break;
                    }
                    if (authPage != null) break;
                }
                catch (PlaywrightException ex) when (PlaywrightErrors.IsTargetClosed(ex)) { }
            }
            if (authPage != null) break;
            await Task.Delay(200);
        }
        if (authPage == null) return new(false, null, "Не найдена кнопка/вкладка «Сертификат».");

        // Владелец выбирается только в окне авторизации или новом popup после его открытия.
        var existingPages = pagesBeforeCertificate ?? context.Pages.ToHashSet();
        timer.Restart();
        Regex ownerWithDetails = OwnerTextRegex(settings.CertificateOwnerName, exact: false);
        while (timer.Elapsed.TotalSeconds < settings.UiTimeoutSeconds)
        {
            var pages = context.Pages.Where(p => !p.IsClosed && (p == authPage || !existingPages.Contains(p)))
                .Reverse().ToArray();
            foreach (IPage page in pages)
            {
                try
                {
                    ILocator[] choices = OwnerChoices(page, settings);
                    foreach (ILocator choice in choices)
                    {
                        var match = await SingleVisibleAsync(choice, firstVisible: settings.SingleCertificateExpected && string.IsNullOrWhiteSpace(settings.CertificateOwnerSelector));
                        if (match.Ambiguous)
                            return new(false, page, "Найдено несколько сертификатов с указанным ФИО. Выберите нужный вручную или задайте точный CertificateOwnerSelector.");
                        if (match.Locator == null) continue;
                        if (!string.IsNullOrWhiteSpace(settings.CertificateOwnerSelector) &&
                            !ownerWithDetails.IsMatch(await match.Locator.InnerTextAsync()))
                            return new(false, page, "CertificateOwnerSelector указывает на другой текст; другой владелец не выбран.");
                        log.Info($"Авторизация: выбираем сертификат «{settings.CertificateOwnerName}».");
                        Console.WriteLine("Вход по сертификату: SMS не ожидается. Если криптокомпонент запросит PIN токена, введите его вручную в системном окне.");
                        try { await match.Locator.ClickAsync(new() { Timeout = settings.UiTimeoutSeconds * 1000 }); }
                        catch (PlaywrightException ex) when (PlaywrightErrors.IsTargetClosed(ex))
                        {
                            return new(true, page, "Окно закрылось после попытки выбора владельца; кабинет проверяется отдельно.");
                        }
                        return new(true, page, "Указанный владелец выбран.");
                    }
                }
                catch (PlaywrightException ex) when (PlaywrightErrors.IsTargetClosed(ex))
                {
                    // Закрытие popup может быть результатом входа; вызывающий код проверит кабинет.
                    return new(false, authPage, "Окно выбора закрылось; проверяем результат авторизации.");
                }
            }
            await Task.Delay(200);
        }
        return new(false, authPage, "Указанный владелец не найден среди доступных элементов. Другой сертификат не выбирался.");
    }

    private static ILocator[] OwnerChoices(IPage page, Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CertificateOwnerSelector)) return [page.Locator(settings.CertificateOwnerSelector)];
        Regex exact = OwnerTextRegex(settings.CertificateOwnerName);
        Regex withDetails = OwnerTextRegex(settings.CertificateOwnerName, exact: false);
        return [page.GetByText(exact),
                page.GetByRole(AriaRole.Button, new() { NameRegex = withDetails }),
                page.GetByRole(AriaRole.Link, new() { NameRegex = withDetails }),
                page.GetByRole(AriaRole.Option, new() { NameRegex = withDetails })];
    }

    private static async Task<(ILocator? Locator, bool Ambiguous)> SingleVisibleAsync(ILocator locator, bool firstVisible = false)
    {
        ILocator? single = null;
        int count = await locator.CountAsync();
        for (int i = 0; i < count; i++)
        {
            ILocator item = locator.Nth(i);
            if (!await item.IsVisibleAsync() || !await item.IsEnabledAsync()) continue;
            // При одном сертификате ФИО может присутствовать дважды на той же карточке.
            if (firstVisible) return (item, false);
            if (single != null) return (null, true);
            single = item;
        }
        return (single, false);
    }
}
