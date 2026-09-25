using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;

namespace SnabDrive.Web.Services;

/// <summary>Имена ролей веб-приложения.</summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Operator = "Operator";

    public static readonly string[] All = { Admin, Operator };
}

/// <summary>Политики авторизации.</summary>
public static class AppPolicies
{
    /// <summary>Пользователи, роли, справочники, журнал аудита.</summary>
    public const string ManageSystem = nameof(ManageSystem);

    /// <summary>Архивация и возврат из архива (в WPF-версии это умел только админ).</summary>
    public const string ManageArchive = nameof(ManageArchive);

    /// <summary>Изменение записей реестра.</summary>
    public const string WriteRegistry = nameof(WriteRegistry);
}

/// <summary>
/// Подготовка базы при старте приложения:
/// — веб-таблицы (Identity + AuditLog + UserColumnPermission) создаются EF-миграцией;
/// — существующие таблицы реестра НЕ изменяются (для SQLite-dev создаются с нуля);
/// — создаются роли Admin/Operator и учётка администратора;
/// — опционально импортируются пользователи из старой таблицы [User].
/// </summary>
public static class DatabaseBootstrapper
{
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger)
    {
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;

        await InitializeIdentityAsync(services, options, logger);
        await InitializeRegistryAsync(services, options, logger);
    }

    private static async Task InitializeIdentityAsync(IServiceProvider services, DatabaseOptions options, ILogger logger)
    {
        var context = services.GetRequiredService<AppIdentityDbContext>();

        if (options.AutoMigrateIdentitySchema)
        {
            var migrations = (await context.Database.GetMigrationsAsync()).Any();
            if (migrations)
            {
                logger.LogInformation("Применяю миграции веб-схемы");
                await context.Database.MigrateAsync();
            }
            else if (options.IsSqlServer)
            {
                // Продовая база создаётся ТОЛЬКО миграцией — вручную таблицы не создаём.
                logger.LogError(
                    "В сборке нет миграций веб-схемы. Выполните: " +
                    "cd web/src/SnabDrive.Web && " +
                    "dotnet ef migrations add InitialWebSchema --context AppIdentityDbContext && " +
                    "dotnet ef database update --context AppIdentityDbContext");
            }
            else
            {
                // Dev-SQLite: миграции ещё не сгенерированы, создаём недостающие таблицы модели.
                // EnsureCreated здесь не подходит — он ничего не делает, если в базе уже есть
                // хоть одна таблица (а таблицы реестра там уже есть).
                await EnsureModelTablesAsync(context, "AspNetUsers", logger, "веб-схемы (Identity + AuditLog)");
            }
        }

        if (options.SeedRolesAndAdmin)
        {
            await SeedRolesAsync(services, logger);
            await SeedAdminAsync(services, options, logger);
        }

        if (options.ImportLegacyUsers)
        {
            await ImportLegacyUsersAsync(services, logger);
        }
    }

    private static async Task InitializeRegistryAsync(IServiceProvider services, DatabaseOptions options, ILogger logger)
    {
        var factory = services.GetRequiredService<IDbContextFactory<RegistryDbContext>>();
        await using var context = await factory.CreateDbContextAsync();

        var exists = await context.Database.CanConnectAsync();

        if (options.EnsureLegacySchema && options.IsSqlite)
        {
            await EnsureModelTablesAsync(context, "Regedit", logger, "реестра (dev SQLite)");

            if (options.SeedDictionaries)
            {
                await SeedDictionariesAsync(context, logger);
            }

            return;
        }

        if (!exists)
        {
            logger.LogWarning("Не удалось подключиться к базе реестра. Проверьте строку подключения.");
            return;
        }

        // Для существующей SQL Server-базы ничего не создаём — только проверяем наличие ключевых таблиц.
        var missing = new List<string>();
        if (!await TableExistsAsync(context, "Regedit")) missing.Add("Regedit");
        if (!await TableExistsAsync(context, "ArchiveRegedit")) missing.Add("ArchiveRegedit");
        if (!await TableExistsAsync(context, "CellColors")) missing.Add("CellColors");

        if (missing.Count > 0)
        {
            logger.LogWarning("В базе не найдены таблицы: {Tables}. Проверьте, та ли база указана в подключении.",
                string.Join(", ", missing));
        }
        else
        {
            logger.LogInformation("Подключение к базе реестра успешно, основные таблицы на месте");
        }
    }

    /// <summary>
    /// Создаёт таблицы модели конкретного контекста, если их ещё нет.
    /// В отличие от EnsureCreated, работает даже когда в базе уже есть чужие таблицы.
    /// </summary>
    private static async Task EnsureModelTablesAsync(DbContext context, string probeTable, ILogger logger, string schemaLabel)
    {
        var creator = context.Database.GetService<IRelationalDatabaseCreator>();

        if (!creator.Exists())
        {
            creator.Create();
        }

        if (await TableExistsAsync(context, probeTable))
        {
            return;
        }

        creator.CreateTables();
        logger.LogInformation("Созданы таблицы {Schema}", schemaLabel);
    }

    private static async Task<bool> TableExistsAsync(DbContext context, string tableName)
    {
        try
        {
            var connection = context.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = context.Database.IsSqlite()
                ? "SELECT count(*) FROM sqlite_master WHERE type='table' AND name = $name"
                : "SELECT count(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @name";

            var parameter = command.CreateParameter();
            parameter.ParameterName = context.Database.IsSqlite() ? "$name" : "@name";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen)
            {
                await connection.OpenAsync();
            }

            try
            {
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
            finally
            {
                if (!wasOpen)
                {
                    await connection.CloseAsync();
                }
            }
        }
        catch
        {
            return false;
        }
    }

    private static async Task SeedDictionariesAsync(RegistryDbContext context, ILogger logger)
    {
        if (await context.TypesOfPurchase.AnyAsync())
        {
            return;
        }

        context.TypesOfPurchase.AddRange(
            new TypeOfPurchase { NameOfPurchase = "Электронный аукцион", ColorCode = "#FF4CAF50" },
            new TypeOfPurchase { NameOfPurchase = "Запрос котировок", ColorCode = "#FF2196F3" },
            new TypeOfPurchase { NameOfPurchase = "Открытый конкурс", ColorCode = "#FFFFC107" });

        context.B2BStatuses.AddRange(
            new B2BStatus { NameB2B = "Не подан" },
            new B2BStatus { NameB2B = "Подан" },
            new B2BStatus { NameB2B = "Отклонён" },
            new B2BStatus { NameB2B = "Победа" });

        context.ExecutionStatuses.AddRange(
            new ExecutionStatus { NameExecution = "Не начато" },
            new ExecutionStatus { NameExecution = "В работе" },
            new ExecutionStatus { NameExecution = "Завершено" });

        await context.SaveChangesAsync();
        logger.LogInformation("Справочники заполнены демо-значениями");
    }

    private static async Task SeedRolesAsync(IServiceProvider services, ILogger logger)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
                logger.LogInformation("Создана роль {Role}", role);
            }
        }
    }

    private static async Task SeedAdminAsync(IServiceProvider services, DatabaseOptions options, ILogger logger)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var login = options.SeedAdminLogin.Trim();

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(options.SeedAdminPassword))
        {
            return;
        }

        var existing = await userManager.FindByNameAsync(login);
        if (existing is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = login,
            Email = null,
            EmailConfirmed = true,
            DisplayName = "Администратор"
        };

        var created = await userManager.CreateAsync(admin, options.SeedAdminPassword);
        if (created.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, AppRoles.Admin);
            logger.LogWarning("Создан администратор «{Login}» со стандартным паролем из конфигурации — смените его после первого входа!", login);
        }
        else
        {
            logger.LogError("Не удалось создать администратора: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));
        }
    }

    private static async Task ImportLegacyUsersAsync(IServiceProvider services, ILogger logger)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var registryFactory = services.GetRequiredService<IDbContextFactory<RegistryDbContext>>();

        await using var registry = await registryFactory.CreateDbContextAsync();

        List<LegacyUser> legacyUsers;
        try
        {
            legacyUsers = await registry.LegacyUsers.AsNoTracking().ToListAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось прочитать старую таблицу [User] — импорт пропущен");
            return;
        }

        var imported = 0;
        var skipped = 0;

        foreach (var legacy in legacyUsers)
        {
            var login = legacy.Login?.Trim();
            if (string.IsNullOrWhiteSpace(login))
            {
                skipped++;
                continue;
            }

            var existing = await userManager.FindByNameAsync(login);
            if (existing is not null)
            {
                skipped++;
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = login,
                Email = string.IsNullOrWhiteSpace(legacy.Email) ? null : legacy.Email,
                EmailConfirmed = !string.IsNullOrWhiteSpace(legacy.Email),
                DisplayName = login,
                LegacyUserId = legacy.Id
            };

            // Старые пароли хранились открытым текстом. Берём их как есть при создании учётки —
            // Identity сразу сохранит хэш, а в старой таблице пароль больше не используется.
            var password = string.IsNullOrWhiteSpace(legacy.Password) ? Guid.NewGuid().ToString("N") + "!aA1" : legacy.Password;
            if (password.Length < 6)
            {
                password = password + "!aA1";
            }

            var created = await userManager.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                logger.LogWarning("Импорт пользователя {Login} не удался: {Errors}", login,
                    string.Join("; ", created.Errors.Select(e => e.Description)));
                skipped++;
                continue;
            }

            await userManager.AddToRoleAsync(user, legacy.IsAdmin ? AppRoles.Admin : AppRoles.Operator);
            imported++;
        }

        logger.LogInformation("Импорт пользователей из [User]: добавлено {Imported}, пропущено {Skipped}", imported, skipped);
    }
}
