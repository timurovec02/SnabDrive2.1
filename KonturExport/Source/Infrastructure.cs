using System.Text.Json;
using System.Text.RegularExpressions;

namespace KonturExport;

internal sealed class Settings
{
    public string OutputDirectory { get; set; } = "";
    public string ProfileDirectory { get; set; } = "";
    public string BrowserExecutablePath { get; set; } = "";
    public string PluginSetupUrl { get; set; } = "https://addons.opera.com/ru/extensions/details/konturrasshirenie/";
    public bool UseCertificateLogin { get; set; } = true;
    public bool SingleCertificateExpected { get; set; } = true;
    public string CertificateButtonName { get; set; } = "Сертификат";
    public string CertificateOwnerName { get; set; } = "Титов Андрей Николаевич";
    public string CertificateOwnerSelector { get; set; } = "";
    public int CertificateLoginTimeoutSeconds { get; set; } = 90;
    public bool Headless { get; set; }
    public int ViewportWidth { get; set; } = 1920;
    public int ViewportHeight { get; set; } = 1080;
    public bool OpenOutputFolder { get; set; } = true;
    public int UiTimeoutSeconds { get; set; } = 30;
    public int ExportTimeoutSeconds { get; set; } = 180;
    public int TransferTimeoutSeconds { get; set; } = 180;
    public string StartUrl { get; set; } = "https://zakupki.kontur.ru/";
    public string ApplicationUrl { get; set; } = "https://zakupki.kontur.ru/Grid";
    public string FavoritesName { get; set; } = "Избранное";
    public string ExportButtonName { get; set; } = "Выгрузить в Excel";
    public string RightSidebarSelector { get; set; } = "";
    public string FileNamePrefix { get; set; } = "Избранное";
    public bool CaptureScreenshotOnError { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KonturExport");

    public static Settings Load(string file)
    {
        if (!File.Exists(file))
            throw new FileNotFoundException("Не найден settings.json. Распакуйте архив целиком.", file);
        return JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), JsonOptions)
            ?? throw new InvalidDataException("Не удалось прочитать settings.json.");
    }

    public void Normalize(string settingsFile, string? outputOverride)
    {
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(settingsFile))!;
        OutputDirectory = ExpandPath(outputOverride ?? OutputDirectory, baseDir,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "KonturExports"));
        ProfileDirectory = ExpandPath(ProfileDirectory, baseDir, Path.Combine(DataDirectory, "yandex-profile"));
        if (!string.IsNullOrWhiteSpace(BrowserExecutablePath))
            BrowserExecutablePath = ExpandPath(BrowserExecutablePath.Trim().Trim('"'), baseDir, baseDir);
        if (UseCertificateLogin && (string.IsNullOrWhiteSpace(CertificateButtonName) || string.IsNullOrWhiteSpace(CertificateOwnerName)))
            throw new InvalidDataException("Для входа по сертификату нужны CertificateButtonName и CertificateOwnerName.");
        if (CertificateLoginTimeoutSeconds <= 0)
            throw new InvalidDataException("CertificateLoginTimeoutSeconds должен быть положительным.");
        if (ViewportWidth < 800 || ViewportHeight < 600)
            throw new InvalidDataException("ViewportWidth должен быть >= 800, ViewportHeight >= 600.");
        if (UiTimeoutSeconds <= 0 || ExportTimeoutSeconds <= 0 || TransferTimeoutSeconds <= 0)
            throw new InvalidDataException("Таймауты в settings.json должны быть положительными.");
        foreach (string url in new[] { StartUrl, ApplicationUrl, PluginSetupUrl })
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new InvalidDataException("StartUrl, ApplicationUrl и PluginSetupUrl должны быть абсолютными HTTPS-адресами.");
        if (string.IsNullOrWhiteSpace(FavoritesName) || string.IsNullOrWhiteSpace(ExportButtonName))
            throw new InvalidDataException("Названия кнопок не должны быть пустыми.");
    }

    public static string ExpandPath(string? value, string baseDir, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return Path.GetFullPath(fallback);
        string expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (Regex.IsMatch(expanded, @"%[A-Za-z_][A-Za-z_0-9]*%"))
            throw new InvalidDataException($"Не удалось раскрыть переменную окружения в пути: {value}");
        return Path.GetFullPath(expanded, baseDir);
    }
}

internal sealed record CliOptions(
    string SettingsFile, string? Output, bool Login, bool? Headless,
    bool NoPause, bool InstallBrowser, bool Help, bool SelfTest, bool BrowserTest,
    string? BrowserExecutablePath, bool SetupPlugin, bool CheckBrowser)
{
    public static CliOptions Parse(string[] args)
    {
        string settings = Path.Combine(AppContext.BaseDirectory, "settings.json");
        string? output = null, browserPath = null;
        bool setupPlugin = false, checkBrowser = false;
        bool login = false, noPause = false, install = false, help = false, test = false, browserTest = false;
        bool? headless = null;
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException($"Нет значения для {args[i - 1]}.");
            switch (args[i].ToLowerInvariant())
            {
                case "--settings": settings = Path.GetFullPath(Value()); break;
                case "--output": output = Value(); break;
                case "--browser": browserPath = Value(); break;
                case "--setup-plugin": setupPlugin = true; break;
                case "--check-browser": checkBrowser = true; break;
                case "--login": login = true; break;
                case "--headless": headless = true; break;
                case "--visible": headless = false; break;
                case "--no-pause": noPause = true; break;
                case "--install-test-browser": case "--install-browser": install = true; break;
                case "--help": case "-h": help = true; break;
                case "--self-test": test = true; break;
                case "--self-test-browser": browserTest = true; break;
                default: throw new ArgumentException($"Неизвестный параметр: {args[i]}");
            }
        }
        return new(settings, output, login, headless, noPause, install, help, test, browserTest, browserPath, setupPlugin, checkBrowser);
    }
}

internal sealed class RunLog : IDisposable
{
    private readonly object gate = new();
    private readonly StreamWriter writer;
    private bool disposed;
    private bool fileUnavailable;
    public string FilePath { get; }

    public RunLog(string folder)
    {
        Directory.CreateDirectory(folder);
        FilePath = Path.Combine(folder, $"run-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}.log");
        writer = new StreamWriter(FilePath, append: false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);
    private void Write(string level, string message)
    {
        // Не сохраняем query string / fragment с кодами авторизации в ссылках.
        message = Regex.Replace(message, @"(https?://[^\s?#]+)[?#][^\s]*", "$1[parameters omitted]");
        lock (gate)
        {
            if (disposed) return;
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {level} {message}";
            Console.WriteLine(line);
            if (!fileUnavailable)
            {
                try { writer.WriteLine(line); }
                catch (IOException) { fileUnavailable = true; Console.Error.WriteLine("Журнал больше не доступен для записи."); }
            }
        }
    }
    public static string SafeUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        ? uri.GetLeftPart(UriPartial.Path) : "about:blank";
    public void Dispose() { lock (gate) { disposed = true; writer.Dispose(); } }
}

internal sealed class LoginRequiredException(string message) : Exception(message);

internal static class PlaywrightErrors
{
    // TargetClosedException — внутренний тип библиотеки; публично ловим PlaywrightException.
    public static bool IsTargetClosed(Exception ex) =>
        ex.GetType().Name == "TargetClosedException" ||
        ex.Message.Contains("Target page, context or browser has been closed", StringComparison.OrdinalIgnoreCase);
}
