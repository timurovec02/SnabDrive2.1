using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

/// <summary>Настройки внешнего экспортера KonturExport (из appsettings, секция «Kontur»).</summary>
public sealed class KonturExporterOptions
{
    /// <summary>Путь к KonturExport.exe (или RunUnattended.cmd).</summary>
    public string ExporterPath { get; set; } = string.Empty;

    /// <summary>Папка, куда экспортер кладёт Excel. Пусто — ~/Downloads/KonturExports.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Таймаут одного запуска экспортера, минут.</summary>
    public int TimeoutMinutes { get; set; } = 15;
}

/// <summary>Редактируемое на сайте расписание (хранится в kontur-schedule.json).</summary>
public sealed class KonturSchedule
{
    public bool Enabled { get; set; } = true;

    /// <summary>Времена «ЧЧ:ММ» ежедневного запуска Избранного (--favorites).</summary>
    public List<string> Times { get; set; } = new() { "08:00", "20:00" };

    /// <summary>Времена «ЧЧ:ММ» запуска Контур ловушки (--templates): обычно начало и конец рабочего дня.</summary>
    public List<string> TemplatesTimes { get; set; } = new() { "09:00", "18:00" };

    public string? LastImportedFile { get; set; }
}

/// <summary>Режим выгрузки: Избранное (--favorites) или Контур ловушка (--templates).</summary>
public enum ExportMode
{
    Favorites,
    Templates
}

/// <summary>Читает/пишет расписание в JSON рядом с приложением.</summary>
public sealed class KonturScheduleStore
{
    private readonly string _path;

    public KonturSchedule Value { get; private set; } = new();

    public KonturScheduleStore(IHostEnvironment env)
    {
        _path = Path.Combine(env.ContentRootPath, "kontur-schedule.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                Value = JsonSerializer.Deserialize<KonturSchedule>(File.ReadAllText(_path)) ?? new KonturSchedule();
            }
        }
        catch
        {
            Value = new KonturSchedule();
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(Value, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // не критично — расписание останется в памяти до перезапуска
        }
    }
}

/// <summary>Состояние планировщика для страницы (последний запуск, журнал, следующий запуск).</summary>
public sealed class KonturSchedulerState
{
    private readonly object _gate = new();

    public bool IsRunning { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public bool LastSuccess { get; set; }
    public string LastMessage { get; set; } = string.Empty;
    public int LastAdded { get; set; }
    public DateTime? NextRunUtc { get; set; }

    public List<string> Log { get; } = new();

    public void AddLog(string message)
    {
        lock (_gate)
        {
            Log.Add($"[{DateTime.Now:dd.MM HH:mm:ss}] {message}");
            if (Log.Count > 100)
            {
                Log.RemoveAt(0);
            }
        }
    }

    public IReadOnlyList<string> GetLog()
    {
        lock (_gate)
        {
            return Log.ToList();
        }
    }
}

/// <summary>
/// Планировщик выгрузки Контур: по расписанию (утром/вечером) запускает внешний
/// KonturExport.exe (--headless --no-pause --output …), затем импортирует свежий
/// Excel в «Избранное Контур». Поддерживает ручной запуск с сайта.
/// </summary>
public sealed class KonturSchedulerService : BackgroundService
{
    private static readonly ChangeActor SystemActor = new("scheduler", "Планировщик Контур");

    private readonly IConfiguration _config;
    private readonly IServiceProvider _services;
    private readonly KonturScheduleStore _store;
    private readonly KonturSchedulerState _state;
    private readonly ILogger<KonturSchedulerService> _logger;
    private readonly HashSet<string> _doneSlots = new(StringComparer.Ordinal);

    public KonturSchedulerService(IConfiguration config, IServiceProvider services,
        KonturScheduleStore store, KonturSchedulerState state, ILogger<KonturSchedulerService> logger)
    {
        _config = config;
        _services = services;
        _store = store;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync();
                ComputeNextRun();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка тика планировщика Контур");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task TickAsync()
    {
        var schedule = _store.Value;
        if (!schedule.Enabled)
        {
            return;
        }

        var now = DateTime.Now;
        await RunDueAsync(schedule.Times, ExportMode.Favorites, now);
        await RunDueAsync(schedule.TemplatesTimes, ExportMode.Templates, now);
    }

    private async Task RunDueAsync(List<string> times, ExportMode mode, DateTime now)
    {
        foreach (var time in times)
        {
            if (!TimeSpan.TryParse(time, out var ts))
            {
                continue;
            }

            var slot = now.Date + ts;
            var slotKey = $"{mode}:{slot:yyyy-MM-ddTHH:mm}";

            if (now >= slot && (now - slot) < TimeSpan.FromMinutes(5) && _doneSlots.Add(slotKey))
            {
                await RunAsync($"по расписанию {time}", mode);
            }
        }
    }

    public async Task RunAsync(string reason, ExportMode mode = ExportMode.Favorites)
    {
        if (_state.IsRunning)
        {
            return;
        }

        _state.IsRunning = true;
        var label = mode == ExportMode.Templates ? "Контур ловушка" : "Избранное";
        _state.AddLog($"Запуск ({label}): {reason}");

        try
        {
            var options = _config.GetSection("Kontur").Get<KonturExporterOptions>() ?? new KonturExporterOptions();
            var outDir = ResolveOutputDirectory(options, mode);
            Directory.CreateDirectory(outDir);
            var start = DateTime.Now;

            RunExporter(options, outDir, mode);

            await ImportNewestAsync(outDir, start, mode);
        }
        catch (Exception ex)
        {
            _state.LastSuccess = false;
            _state.LastMessage = ex.Message;
            _state.AddLog("Ошибка: " + ex.Message);
        }
        finally
        {
            _state.LastRunUtc = DateTime.UtcNow;
            _state.IsRunning = false;
        }
    }

    private static string ResolveOutputDirectory(KonturExporterOptions options, ExportMode mode)
    {
        var baseDir = !string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? options.OutputDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "KonturExports");
        return Path.Combine(baseDir, mode == ExportMode.Templates ? "templates" : "favorites");
    }

    private void RunExporter(KonturExporterOptions options, string outDir, ExportMode mode)
    {
        if (string.IsNullOrWhiteSpace(options.ExporterPath) || !File.Exists(options.ExporterPath))
        {
            _state.AddLog($"Экспортер не найден по пути «{options.ExporterPath}». Проверьте Kontur:ExporterPath в appsettings. Пробуем импорт из папки.");
            return;
        }

        var modeFlag = mode == ExportMode.Templates ? "--templates" : "--favorites";
        var args = $"--headless --no-pause {modeFlag} --output \"{outDir}\"";
        _state.AddLog($"Команда: \"{options.ExporterPath}\" {args}");

        try
        {
            var psi = new ProcessStartInfo(options.ExporterPath, args)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(options.ExporterPath)) ?? outDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                _state.AddLog("Не удалось запустить экспортер.");
                return;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var timeout = TimeSpan.FromMinutes(options.TimeoutMinutes <= 0 ? 15 : options.TimeoutMinutes);
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(true); } catch { }
                _state.AddLog("Экспортер не уложился в таймаут и был остановлен.");
            }
            else
            {
                _state.AddLog($"Экспортер завершился с кодом {process.ExitCode}.");
            }

            var stdout = stdoutTask.Result.Trim();
            var stderr = stderrTask.Result.Trim();
            if (stdout.Length > 0) _state.AddLog("Вывод: " + Tail(stdout));
            if (stderr.Length > 0) _state.AddLog("Ошибки: " + Tail(stderr));
        }
        catch (Exception ex)
        {
            _state.AddLog("Ошибка запуска экспортера: " + ex.Message);
        }
    }

    private static string Tail(string value) => value.Length <= 1500 ? value : value[^1500..];

    private async Task ImportNewestAsync(string outDir, DateTime start, ExportMode mode)
    {
        var file = new DirectoryInfo(outDir).GetFiles("*.xlsx")
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();

        if (file is null || file.LastWriteTime < start.AddMinutes(-2))
        {
            _state.LastSuccess = true;
            _state.LastMessage = "Новый файл выгрузки не найден.";
            _state.AddLog(_state.LastMessage);
            return;
        }

        using var scope = _services.CreateScope();
        var kontur = scope.ServiceProvider.GetRequiredService<IKonturService>();

        await using var stream = file.OpenRead();
        var result = mode == ExportMode.Templates
            ? await kontur.ImportToTemplatesAsync(stream, SystemActor)
            : await kontur.ImportFromExcelAsync(stream, SystemActor);

        _state.LastSuccess = result.Success;
        _state.LastAdded = result.Success ? result.Value : 0;
        _state.LastMessage = result.Success ? $"Импортировано строк: {result.Value} ({file.Name})" : (result.Error ?? "Ошибка импорта");
        _state.AddLog(_state.LastMessage);

        if (result.Success)
        {
            _store.Value.LastImportedFile = file.FullName;
            _store.Save();
        }
    }

    private void ComputeNextRun()
    {
        var schedule = _store.Value;
        if (!schedule.Enabled)
        {
            _state.NextRunUtc = null;
            return;
        }

        var now = DateTime.Now;
        DateTime? next = null;

        foreach (var time in schedule.Times.Concat(schedule.TemplatesTimes))
        {
            if (!TimeSpan.TryParse(time, out var ts))
            {
                continue;
            }

            var today = now.Date + ts;
            var candidate = today > now ? today : now.Date.AddDays(1) + ts;
            if (next is null || candidate < next)
            {
                next = candidate;
            }
        }

        _state.NextRunUtc = next;
    }
}
