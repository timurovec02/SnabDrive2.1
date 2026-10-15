using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Services;

/// <summary>Вкладки/разделы, доступ к которым настраивается по ролям.</summary>
public static class AppFeatures
{
    public const string Registry = "registry";
    public const string Archive = "archive";
    public const string Kontur = "kontur";
    public const string Scheduler = "scheduler";
    public const string Workflow = "workflow";
    public const string Director = "director";
    public const string Users = "users";
    public const string Audit = "audit";
    public const string Dictionaries = "dictionaries";

    /// <summary>Ключ → заголовок (в порядке отображения).</summary>
    public static readonly IReadOnlyList<(string Key, string Title)> All = new (string, string)[]
    {
        (Registry, "Реестр"),
        (Archive, "Архив"),
        (Kontur, "Контур — избранное"),
        (Scheduler, "Планировщик Контура"),
        (Workflow, "Задачи"),
        (Director, "Ловушка / Директор"),
        (Users, "Пользователи"),
        (Audit, "Журнал аудита"),
        (Dictionaries, "Справочники")
    };

    public static string Title(string key) => All.FirstOrDefault(f => f.Key == key).Title ?? key;
}

/// <summary>Набор прав роли: доступ к вкладкам + права на колонки реестра.</summary>
public sealed class RolePermissionSet
{
    /// <summary>Доступ к вкладкам (ключ AppFeatures → разрешено).</summary>
    public Dictionary<string, bool> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Права на колонки реестра. Пусто = все колонки видны и доступны (режим AllColumns).</summary>
    public List<ColumnPermission> Columns { get; set; } = new();

    public bool FeatureEnabled(string key) => Features.TryGetValue(key, out var v) && v;

    public RolePermissionSet Clone() => new()
    {
        Features = Features.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
        Columns = Columns.Select(c => c with { }).ToList()
    };
}

/// <summary>Значения прав по умолчанию для каждой роли (используются при первом старте/сбросе).</summary>
public static class RolePermissionDefaults
{
    public static RolePermissionSet For(string role)
    {
        var set = new RolePermissionSet();
        foreach (var (key, _) in AppFeatures.All)
        {
            set.Features[key] = role switch
            {
                AppRoles.Admin => true, // админ — всё
                AppRoles.Director => key != AppFeatures.Users, // директор — весь бизнес, кроме управления пользователями
                AppRoles.TenderHead => key is AppFeatures.Registry or AppFeatures.Archive or AppFeatures.Kontur
                    or AppFeatures.Scheduler or AppFeatures.Workflow or AppFeatures.Director or AppFeatures.Audit,
                AppRoles.TenderManager => key is AppFeatures.Registry or AppFeatures.Archive or AppFeatures.Kontur
                    or AppFeatures.Scheduler or AppFeatures.Workflow,
                AppRoles.Accountant => key is AppFeatures.Registry or AppFeatures.Archive,
                _ => key is AppFeatures.Registry // прочим (в т.ч. Operator) — минимум
            };
        }
        // Columns пусто = все колонки (AllColumns). Уточняется в настройке роли.
        return set;
    }
}
