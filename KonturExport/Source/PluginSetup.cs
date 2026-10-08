using Microsoft.Playwright;

namespace KonturExport;

internal static class PluginSetup
{
    public static async Task RunAsync(IBrowserContext context, Settings settings, RunLog log)
    {
        if (Console.IsInputRedirected)
            throw new LoginRequiredException("Настройка расширения требует видимого браузера и интерактивной консоли. Используйте SetupPlugin.cmd.");
        IPage instructions = await context.NewPageAsync();
        await instructions.GotoAsync("https://support.kontur.ru/extern/51473-obyazatelnye_programmy#header_51473_2",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        IPage catalog = await context.NewPageAsync();
        await catalog.GotoAsync(settings.PluginSetupUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await catalog.BringToFrontAsync();
        log.Info("Открыта настройка расширения в отдельном профиле Яндекса. Выгрузка в этом режиме не выполняется.");
        Console.WriteLine("\nУстановите/включите Контур.Расширение из официального каталога в ЭТОМ окне Яндекса.");
        Console.WriteLine("Если расширение уже есть, повторная установка не нужна.");
        Console.WriteLine("Если каталог не открывается, используйте ссылку другого каталога на вкладке справки Контура.");
        Console.WriteLine("Системный Контур.Плагин установите в Windows отдельно по инструкции Контура через обычный браузер.");
        Console.WriteLine("Программа НЕ устанавливает системный плагин, не вводит PIN и не подписывает документы.");
        Console.WriteLine("После окончания настройки нажмите Enter здесь. Профиль будет штатно закрыт и сохранён.");
        if (Console.ReadLine() == null) throw new LoginRequiredException("Не удалось дождаться подтверждения настройки.");
        log.Info("Пользователь завершил настройку. Работу системного компонента проверяйте через диагностику Контура.");
    }
}
