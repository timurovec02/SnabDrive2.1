using System.Diagnostics;
using Microsoft.Win32;
using Microsoft.Playwright;

namespace KonturExport;

internal static class YandexBrowser
{
    public static string Resolve(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string path = configuredPath.Trim().Trim('"');
            if (Directory.Exists(path)) path = Path.Combine(path, "browser.exe");
            if (!File.Exists(path)) throw new FileNotFoundException(
                "Не найден browser.exe по BrowserExecutablePath. Проверьте полный путь через Settings.cmd.", path);
            return Path.GetFullPath(path);
        }
        string? found = FirstExisting(StandardCandidates().Concat(RegistryCandidates()));
        return found ?? throw new FileNotFoundException(
            "Яндекс Браузер не найден. Установите его обычным способом либо укажите полный путь к browser.exe " +
            "в BrowserExecutablePath через Settings.cmd. Chrome for Testing вместо Яндекса автоматически не запускается.");
    }

    public static string? FirstExisting(IEnumerable<string> candidates) => candidates
        .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault(File.Exists);

    private static IEnumerable<string> StandardCandidates()
    {
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("LOCALAPPDATA"),
            Environment.GetEnvironmentVariable("PROGRAMFILES"),
            Environment.GetEnvironmentVariable("PROGRAMFILES(X86)"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string? root in roots)
            yield return Path.Combine(root!, "Yandex", "YandexBrowser", "Application", "browser.exe");
        // Отдельные корпоративные / ГОСТ-установки могут иметь другой каталог.
        foreach (string? root in roots)
        {
            string folder = Path.Combine(root!, "Yandex");
            string[] dirs;
            try { dirs = Directory.Exists(folder) ? Directory.GetDirectories(folder, "YandexBrowser*") : []; }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (string dir in dirs)
                yield return Path.Combine(dir, "Application", "browser.exe");
        }
    }

    private static IEnumerable<string> RegistryCandidates()
    {
        if (!OperatingSystem.IsWindows()) yield break;
        var found = new List<string>();
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\browser.exe");
                string? path = (key?.GetValue(null) as string)?.Trim().Trim('"');
                // browser.exe может принадлежать другому продукту: не подменяем Яндекс им.
                if (!string.IsNullOrEmpty(path) && path.Contains("Yandex", StringComparison.OrdinalIgnoreCase)) found.Add(path);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        foreach (string path in found) yield return path;
    }

    public static string Version(string executable)
    {
        try { return FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? "не определена"; }
        catch { return "не определена"; }
    }

    public static BrowserTypeLaunchPersistentContextOptions LaunchOptions(Settings settings, string executable, bool headless) => new()
    {
        // Канала "yandex" в Playwright нет. Используем явно установленный browser.exe.
        ExecutablePath = executable,
        Headless = headless,
        // Включаем песочницу: Playwright не должен добавлять --no-sandbox.
        ChromiumSandbox = true,
        AcceptDownloads = true,
        Locale = "ru-RU",
        ViewportSize = new ViewportSize { Width = settings.ViewportWidth, Height = settings.ViewportHeight },
        // Разрешаем установленные расширения и их фоновые компоненты, не убирая все остальные аргументы.
        IgnoreDefaultArgs = new[] { "--disable-extensions", "--disable-component-extensions-with-background-pages" }
    };
}
