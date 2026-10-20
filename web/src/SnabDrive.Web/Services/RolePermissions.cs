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

    /// <summary>Стадии воркфлоу, за которые отвечает роль (Id из WorkflowStages.All). Пусто = все стадии.</summary>
    public List<int> Stages { get; set; } = new();

    public bool FeatureEnabled(string key) => Features.TryGetValue(key, out var v) && v;

    /// <summary>Отвечает ли роль за стадию (пустой набор = все стадии).</summary>
    public bool StageEnabled(int stageId) => Stages.Count == 0 || Stages.Contains(stageId);

    public RolePermissionSet Clone() => new()
    {
        Features = Features.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
        Columns = Columns.Select(c => c with { }).ToList(),
        Stages = Stages.ToList()
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
                AppRoles.Accountant => key is AppFeatures.Registry or AppFeatures.Archive or AppFeatures.Workflow,
                _ => key is AppFeatures.Registry // прочим (в т.ч. Operator) — минимум
            };
        }
        // Columns пусто = все колонки (AllColumns). Уточняется в настройке роли.

        // Стадии воркфлоу, за которые отвечает роль (Id из WorkflowStages.All). Пусто = все стадии.
        set.Stages = role switch
        {
            AppRoles.TenderManager => new List<int> { 1, 2, 12, 3, 4 }, // Контур, Резерв, Согласование, Подача заявки, Торги
            AppRoles.Accountant => new List<int> { 6, 9, 10 },           // Контракт подписан, Приемка, Оплата
            AppRoles.Operator => new List<int> { 1 },                    // только подготовка
            _ => new List<int>()                                         // Admin, Director, TenderHead — все стадии
        };

        return set;
    }
}
