namespace SnabDrive.Web.Data;

/// <summary>Настройки доступа к данным (секция "Database" в appsettings).</summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Применять миграции/создавать схему веб-таблиц (Identity, AuditLog) при старте.</summary>
    public bool AutoMigrateIdentitySchema { get; set; } = true;

    /// <summary>Создать роли Admin/Operator и учётку администратора при первом старте.</summary>
    public bool SeedRolesAndAdmin { get; set; } = true;

    public string SeedAdminLogin { get; set; } = "admin";

    public string SeedAdminPassword { get; set; } = "ChangeMe-123!";

    /// <summary>Однократно импортировать пользователей из старой таблицы [User] в Identity.</summary>
    public bool ImportLegacyUsers { get; set; }
}
