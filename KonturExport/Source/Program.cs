using System.Diagnostics;
using System.Text;
using Microsoft.Playwright;

namespace KonturExport;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = Encoding.UTF8;
        CliOptions options;
        try { options = CliOptions.Parse(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }

        if (options.Help) { PrintHelp(); return 0; }
        if (options.SelfTest) return SelfTests.RunUnitTests();
        if (options.BrowserTest) return await SelfTests.RunBrowserTestsAsync();
        if (options.InstallBrowser) return InstallTestBrowser();

        using var log = new RunLog(Path.Combine(Settings.DataDirectory, "logs"));
        IBrowserContext? context = null;
        IPlaywright? playwright = null;
        FileStream? profileLock = null;
        Settings? settings = null;
        bool headless = false, failed = false, closedNormally = false;
        int exitCode = 0;
        try
        {
            settings = Settings.Load(options.SettingsFile);
            if (options.BrowserExecutablePath != null) settings.BrowserExecutablePath = options.BrowserExecutablePath;
            settings.Normalize(options.SettingsFile, options.Output);
            headless = options.Login || options.SetupPlugin ? false : options.Headless ?? settings.Headless;
            string executable = YandexBrowser.Resolve(settings.BrowserExecutablePath);
            log.Info("Контур: выгрузка Избранного. Версия 1.3.1 — Яндекс Браузер.");
            log.Info($"Яндекс: {executable}; версия {YandexBrowser.Version(executable)}.");
            log.Info($"Настройки: {options.SettingsFile}");
            log.Info($"Папка выгрузки: {settings.OutputDirectory}");
            log.Info($"Отдельный профиль: {settings.ProfileDirectory}");
            log.Info($"Режим: {(headless ? "скрытый" : "видимый")}; viewport {settings.ViewportWidth}x{settings.ViewportHeight}. Расширения разрешены.");
            if (options.CheckBrowser)
            {
                log.Info("Файл Яндекса найден. Это проверка пути, а не проверка криптоплагина или совместимости CDP.");
                return 0;
            }
            Directory.CreateDirectory(settings.OutputDirectory);
            Directory.CreateDirectory(settings.ProfileDirectory);
            try
            {
                profileLock = new FileStream(settings.ProfileDirectory + ".app.lock",
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException("Этот профиль уже используется другим экземпляром программы. Закройте его и повторите запуск.", ex);
            }

            playwright = await Playwright.CreateAsync();
            var launch = YandexBrowser.LaunchOptions(settings, executable, headless);
            // Яндекс уже должен быть установлен. Автоматической подмены Chromium нет.
            context = await playwright.Chromium.LaunchPersistentContextAsync(settings.ProfileDirectory, launch);
            context.SetDefaultTimeout(settings.UiTimeoutSeconds * 1000);
            context.SetDefaultNavigationTimeout(Math.Max(60, settings.UiTimeoutSeconds) * 1000);
            using var lifecycle = new BrowserLifecycle(context, log);
            if (options.SetupPlugin)
            {
                await PluginSetup.RunAsync(context, settings, log);
            }
            else
            {
                var login = new LoginFlow(context, settings, log);
                IPage page = await login.GetAuthenticatedPageAsync(options.Login, headless);
                OutputResult result = await ExportFlow.RunAsync(context, page, settings, log);
                log.Info($"ГОТОВО. Сохранён файл: {result.Path}");
                log.Info($"Размер: {result.Bytes:N0} байт. Формат: {result.Format}.");
                Console.WriteLine("Открывайте эту копию из папки, а не GUID в списке загрузок браузера.");
                if (settings.OpenOutputFolder && !headless && OperatingSystem.IsWindows())
                {
                    try { Process.Start(new ProcessStartInfo(settings.OutputDirectory) { UseShellExecute = true }); }
                    catch (Exception ex) { log.Warn("Не удалось открыть Проводник: " + ex.Message); }
                }
            }
        }
        catch (Exception ex)
        {
            failed = true;
            exitCode = ex is LoginRequiredException ? 2 : 1;
            log.Error(ex.ToString());
            if (context != null)
            {
                log.Info($"До освобождения ресурсов: вкладок {context.Pages.Count}; подключение браузера {context.Browser?.IsConnected.ToString() ?? "неизвестно"}.");
                foreach (IPage p in context.Pages)
                    log.Info($"Вкладка: закрыта={p.IsClosed}; адрес={RunLog.SafeUrl(p.Url)}");
                if (settings?.CaptureScreenshotOnError == true)
                {
                    IPage? alive = context.Pages.FirstOrDefault(p => !p.IsClosed);
                    if (alive != null)
                    {
                        try
                        {
                            string image = Path.ChangeExtension(log.FilePath, ".png");
                            await alive.ScreenshotAsync(new() { Path = image, Timeout = 5000 });
                            log.Warn($"Скриншот ошибки (может содержать личные данные): {image}");
                        }
                        catch { }
                    }
                }
            }
            Console.WriteLine($"\nЖурнал ошибки: {log.FilePath}");
            if (!options.NoPause && !headless && !Console.IsInputRedirected)
            {
                Console.WriteLine("Нажмите Enter, чтобы завершить программу и закрыть оставшиеся окна отдельного профиля.");
                Console.ReadLine();
            }
        }
        finally
        {
            if (context != null)
            {
                try { await context.CloseAsync(); closedNormally = true; }
                catch (Exception ex) { log.Warn("При закрытии контекста: " + ex.Message); }
            }
            playwright?.Dispose();
            profileLock?.Dispose();
        }
        if (!failed && closedNormally) log.Info("Яндекс штатно закрыт, изменения отдельного профиля сохранены.");
        return exitCode;
    }

    private static int InstallTestBrowser()
    {
        Console.WriteLine("Установка Chromium ТОЛЬКО для локальных тестов разработчика. Обычные запуски используют установленный Яндекс.");
        return Microsoft.Playwright.Program.Main(new[] { "install", "chromium", "--no-shell" });
    }

    private static void PrintHelp() => Console.WriteLine("""
        KonturExport 1.3.1 — Яндекс: Избранное zakupki.kontur.ru → Excel
        Без параметров: найти установленный Яндекс, восстановить отдельный профиль и выгрузить Excel.
        --browser "file"      полный путь к browser.exe (или BrowserExecutablePath в settings.json)
        --check-browser       показать найденный путь и версию без запуска браузера
        --setup-plugin        открыть отдельный профиль для ручной установки Контур.Расширения
        --login               вход: Сертификат → указанный владелец; PIN токена вручную
        --output "D:\Excel"   папка сохранения для этого запуска
        --settings "file"     другой settings.json
        --headless            скрытый режим: сначала проверьте видимый запуск; криптоплагины могут требовать UI
        --visible             принудительно показать браузер
        --no-pause            не ждать Enter при ошибке
        --install-test-browser установить Chromium только для локальных тестов разработчика
        --self-test           проверка путей, запуска Яндекса и распознавания форматов
        --self-test-browser   локальные тесты на Chromium, не тест Яндекса/Контура
        --help                эта справка
        """);
}
