using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SnabDrive.Web;
using SnabDrive.Web.Components;
using SnabDrive.Web.Data;
using SnabDrive.Web.Hubs;
using SnabDrive.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------ конфигурация

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();

var registryConnection = builder.Configuration.GetConnectionString("Registry")
                         ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Registry");

Action<DbContextOptionsBuilder> configureProvider = options =>
{
    if (databaseOptions.IsSqlServer)
    {
        options.UseSqlServer(registryConnection, sql => sql.EnableRetryOnFailure(3));
    }
    else
    {
        options.UseSqlite(registryConnection);
    }
};

// ------------------------------------------------------------------ данные

// AddDbContext даёт scoped DbContextOptions<T>; фабрика по умолчанию singleton и
// «не может есть» scoped-опции (падение при ValidateScopes). Выравниваем lifetime:
// все потребители фабрики — scoped-сервисы либо явный CreateScope при старте.
builder.Services.AddDbContext<RegistryDbContext>(configureProvider);
builder.Services.AddDbContextFactory<RegistryDbContext>(configureProvider, ServiceLifetime.Scoped);
builder.Services.AddDbContext<AppIdentityDbContext>(configureProvider);
builder.Services.AddDbContextFactory<AppIdentityDbContext>(configureProvider, ServiceLifetime.Scoped);

// ------------------------------------------------------------------ авторизация

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.SignIn.RequireConfirmedAccount = false;

        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;

        // Защита от подбора пароля (в WPF-версии было 3 попытки и блокировка на минуту).
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppIdentityDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/account/logout";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.Name = "SnabDrive.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    options.AddPolicy(AppPolicies.ManageSystem, policy => policy.RequireRole(AppRoles.Admin, AppRoles.Director));
    options.AddPolicy(AppPolicies.ManageArchive, policy => policy.RequireRole(AppRoles.Admin, AppRoles.Director, AppRoles.TenderHead, AppRoles.TenderManager, AppRoles.Accountant));
    options.AddPolicy(AppPolicies.WriteRegistry, policy => policy.RequireRole(AppRoles.Admin, AppRoles.Director, AppRoles.TenderHead, AppRoles.TenderManager, AppRoles.Accountant));
});

// ------------------------------------------------------------------ UI / API / realtime

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Снабдрайв Web API",
        Version = "v1",
        Description = "CRUD реестра закупок, справочники, аудит, онлайн-пользователи."
    });
});

builder.Services.AddSignalR();
builder.Services.AddAntiforgery();

builder.Services.AddSingleton<IRegistryEventBus, RegistryEventBus>();
builder.Services.AddSingleton<IPresenceService, PresenceService>();

builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IRegistryNotifier, SignalRRegistryNotifier>();
builder.Services.AddScoped<IRegistryService, RegistryService>();
builder.Services.AddScoped<IStageHistoryService, StageHistoryService>();
builder.Services.AddScoped<IKonturService, KonturService>();
builder.Services.AddSingleton<KonturScheduleStore>();
builder.Services.AddSingleton<KonturSchedulerState>();
builder.Services.AddSingleton<KonturSchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<KonturSchedulerService>());

// Уведомления о предстоящих торгах (за 1 день и в день торгов) для роли TenderHead — в Битрикс24, однократно.
builder.Services.AddHostedService<BiddingNotificationService>();

// Воркфлоу по стадиям + уведомления в Битрикс24
builder.Services.AddHttpClient();
builder.Services.Configure<BitrixOptions>(builder.Configuration.GetSection("Bitrix"));
builder.Services.AddScoped<BitrixNotifier>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddScoped<IDictionaryService, DictionaryService>();
builder.Services.AddScoped<IBranchService, BranchService>();
builder.Services.AddScoped<IColumnAccessService, ColumnAccessService>();
builder.Services.AddScoped<IRolePermissionService, RolePermissionService>();
builder.Services.AddScoped<IMaintenanceService, MaintenanceService>();
builder.Services.AddSingleton<ICellEditLockService, CellEditLockService>();
builder.Services.AddScoped<UserSessionState>();
builder.Services.AddScoped<IToastService, ToastService>();

var app = builder.Build();

// ------------------------------------------------------------------ конвейер

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHub<RegistryHub>(RegistryHub.Route);
app.MapAccountEndpoints();

// Скачивание вложений из БД (таблица [Attachments]) — файлы не лежат на файловой системе IIS.
// Защищено глобальным FallbackPolicy (RequireAuthenticatedUser): cookie аутентификации приходит вместе с запросом.
// Файл отдаётся потоково: раньше EF вычитывал всё содержимое в память (20 МБ на каждое
// скачивание), из-за чего отдача больших вложений была заметно медленнее загрузки.
app.MapGet("/attachments/{id:int}/{**name}", async (int id, CancellationToken ct, IDbContextFactory<RegistryDbContext> factory) =>
{
    // Контекс намеренно без using: при потоковой отдаче его закрывает DbBlobStream,
    // когда клиент докачает файл. На путях с ошибкой освобождаем явно.
    var db = await factory.CreateDbContextAsync(ct);
    var streamed = false;
    try
    {
        // Метаданные — отдельным запросом и без [Content].
        var meta = await db.Attachments.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new { a.FileName, a.ContentType })
            .FirstOrDefaultAsync(ct);
        if (meta is null)
        {
            return Results.NotFound();
        }

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        // Команду не освобождаем здесь: она нужна, пока открыт читатель.
        var command = connection.CreateCommand();
        command.CommandText = "SELECT [Content] FROM [Attachments] WHERE [Id] = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = id;
        command.Parameters.Add(parameter);

        // SequentialAccess: BLOB читается кусками по мере отдачи клиенту.
        var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, ct);
        if (!await reader.ReadAsync(ct))
        {
            await reader.DisposeAsync();
            await command.DisposeAsync();
            return Results.NotFound();
        }

        streamed = true;
        return Results.Stream(
            new DbBlobStream(reader.GetStream(0), reader, command, db),
            string.IsNullOrWhiteSpace(meta.ContentType) ? "application/octet-stream" : meta.ContentType,
            meta.FileName);
    }
    finally
    {
        // При потоковой отдаче контекст закроет DbBlobStream, когда клиент докачает файл.
        if (!streamed)
        {
            await db.DisposeAsync();
        }
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Онлайн-пользователи дублируются в SignalR для внешних клиентов (виджеты, интеграции).
var eventBus = app.Services.GetRequiredService<IRegistryEventBus>();
var hubContext = app.Services.GetRequiredService<IHubContext<RegistryHub>>();
eventBus.PresenceChanged += onlineUsers =>
{
    _ = hubContext.Clients.All.SendAsync(RegistryHub.PresenceChangedMethod, onlineUsers);
};

// Культура по умолчанию — русская (форматы дат и чисел в UI).
try
{
    var culture = new CultureInfo("ru-RU");
    CultureInfo.DefaultThreadCurrentCulture = culture;
    CultureInfo.DefaultThreadCurrentUICulture = culture;
}
catch (CultureNotFoundException)
{
    // Если в контейнере нет данных ru-RU — остаёмся на инвариантной культуре.
}

// ------------------------------------------------------------------ старт

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await DatabaseBootstrapper.InitializeAsync(scope.ServiceProvider, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Ошибка подготовки базы данных при старте");
    }
}

app.Run();

/// <summary>Точка входа, доступная для интеграционных тестов (WebApplicationFactory).</summary>
public partial class Program;
