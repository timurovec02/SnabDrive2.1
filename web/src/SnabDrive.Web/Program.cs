using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SnabDrive.Web;
using SnabDrive.Web.Components;
using SnabDrive.Web.Data;
using SnabDrive.Web.Hubs;
using SnabDrive.Web.Services;
using SnabDrive.Web.Services.Security;

var builder = WebApplication.CreateBuilder(args);

// Что не получилось настроить при старте — выводится в журнал после сборки приложения.
var startupWarnings = new List<string>();

// ------------------------------------------------------------------ конфигурация

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));

// Защита сайта: заголовки безопасности, CSP, лимиты запросов, источники CORS.
// Всё настраивается секцией Security в appsettings — см. Services/Security/SecurityOptions.cs.
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.SectionName));
var security = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();

// ------------------------------------------------------------------ ключи Data Protection
// Ими шифруются cookie входа и antiforgery-токены. Без явного хранилища ключи лежат
// в профиле пула приложений: ресайкл пула — это разлогин всех пользователей, а в саду
// (несколько рабочих процессов) cookie, выданная одним процессом, не читается другим.
var keysPath = string.IsNullOrWhiteSpace(security.DataProtectionKeysPath)
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SnabDrive", "keys")
    : security.DataProtectionKeysPath;

try
{
    Directory.CreateDirectory(keysPath);
    var dataProtection = builder.Services.AddDataProtection()
        .SetApplicationName("SnabDrive.Web")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

    // DPAPI есть только в Windows; на других ОС ключи защищает правами сама папка.
    if (OperatingSystem.IsWindows())
    {
        dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
    }
}
catch (Exception keysEx)
{
    // Папку создать не удалось (нет прав) — остаёмся на стандартном хранилище,
    // но пишем об этом в журнал сразу после старта.
    startupWarnings.Add("Не удалось создать папку ключей Data Protection " + keysPath + ": " + keysEx.Message);
}

var registryConnection = builder.Configuration.GetConnectionString("Registry")
                         ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Registry");

// Провайдер базы один — SQL Server.
Action<DbContextOptionsBuilder> configureProvider = options =>
    options.UseSqlServer(registryConnection, sql => sql.EnableRetryOnFailure(3));

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

// HSTS: браузер запоминает, что сайт работает только по https. Заголовок выдаётся
// вне режима разработки; в ответах по http браузеры его игнорируют, поэтому он безопасен.
builder.Services.AddHsts(hsts =>
{
    hsts.MaxAge = TimeSpan.FromDays(security.HstsMaxAgeDays);
    hsts.IncludeSubDomains = true;
});

// Перенаправление http -> https. Порт берётся из Security:HttpsPort, если задан;
// само перенаправление включается Security:EnableHttpsRedirection после настройки HTTPS в IIS.
builder.Services.AddHttpsRedirection(redirection =>
{
    var httpsPort = builder.Configuration.GetValue<int?>("Security:HttpsPort");
    if (httpsPort is > 0)
    {
        redirection.HttpsPort = httpsPort;
    }
});

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

        // Пароль проверяется на сервере (PasswordValidator) и при создании пользователя,
        // и при смене пароля, поэтому обойти правило из браузера нельзя.
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredUniqueChars = 3;

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
    // HttpOnly — скрипт со страницы не может прочитать cookie (кража сессии при XSS).
    options.Cookie.HttpOnly = true;
    // Lax — cookie не уходит на чужой сайт, поэтому подделка кросс-сайтового запроса
    // (форма с другого домена) приходит уже без авторизации.
    options.Cookie.SameSite = SameSiteMode.Lax;
    // Secure — по http cookie не передаётся. Ставьте Security:CookieSecurePolicy = "Always"
    // вместе с HTTPS; до этого момента SameAsRequest, чтобы не закрыть вход на http-развёртывании.
    options.Cookie.SecurePolicy = security.ResolveCookieSecurePolicy() == CookieSecurePolicyValue.Always
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
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

builder.Services.AddControllers(mvc =>
{
    // CSRF: любой POST/PUT/PATCH/DELETE в API обязан прийти с antiforgery-токеном
    // (поле формы __RequestVerificationToken или заголовок RequestVerificationToken).
    // GET/HEAD/OPTIONS не проверяются. Токен клиентам выдаёт GET /account/antiforgery-token.
    mvc.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});
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

// Antiforgery-cookie — те же правила, что и у cookie входа.
builder.Services.AddAntiforgery(antiforgery =>
{
    antiforgery.Cookie.Name = "SnabDrive.Antiforgery";
    antiforgery.Cookie.HttpOnly = true;
    antiforgery.Cookie.SameSite = SameSiteMode.Lax;
    antiforgery.Cookie.SecurePolicy = security.ResolveCookieSecurePolicy() == CookieSecurePolicyValue.Always
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
    antiforgery.HeaderName = "RequestVerificationToken";
});

// CORS: по умолчанию список источников пуст — кросс-доменные запросы к сайту и API
// запрещены (браузер их блокирует, заголовки Access-Control-Allow-* не выдаются).
// Понадобится доступ с другого домена — добавьте его в Security:AllowedOrigins.
var corsOrigins = security.AllowedOrigins
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .ToArray();

if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
        .WithOrigins(corsOrigins)
        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
        .WithHeaders("Content-Type", "Accept", "RequestVerificationToken")
        .AllowCredentials()));
}

// ------------------------------------------------------------------ лимиты запросов
// Защита от перебора паролей и от «заливания» сайта запросами: считается число запросов
// с одного IP за минуту. Статика и циркул Blazor не ограничиваются, иначе интерфейс
// начал бы тормозить на обычной работе.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.OnRejected = async (context, cancellationToken) =>
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers["Retry-After"] = "60";
        response.ContentType = "text/plain; charset=utf-8";
        await response.WriteAsync("Слишком много запросов. Подождите минуту и попробуйте снова.", cancellationToken);
    };

    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var path = httpContext.Request.Path;
        if (path.StartsWithSegments("/_blazor") || path.StartsWithSegments("/_framework") ||
            path.StartsWithSegments("/css") || path.StartsWithSegments("/js") ||
            path.StartsWithSegments("/lib") || path.StartsWithSegments("/favicon.ico"))
        {
            return RateLimitPartition.GetNoLimiter("без ограничений");
        }

        return RateLimitPartition.GetFixedWindowLimiter("site:" + ClientKeyHelper.Get(httpContext), _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(10, security.GlobalRequestsPerMinute),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    limiter.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        "login:" + ClientKeyHelper.Get(httpContext),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, security.LoginAttemptsPerMinute),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    limiter.AddPolicy("api", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        "api:" + ClientKeyHelper.Get(httpContext),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(10, security.ApiRequestsPerMinute),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services.AddSingleton<IRegistryEventBus, RegistryEventBus>();
builder.Services.AddSingleton<IPresenceService, PresenceService>();

builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IRegistryNotifier, SignalRRegistryNotifier>();
builder.Services.AddScoped<IRegistryService, RegistryService>();
builder.Services.AddScoped<IStageHistoryService, StageHistoryService>();
builder.Services.AddScoped<IKonturService, KonturService>();
builder.Services.AddScoped<ILegacyImportService, LegacyImportService>();
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

// Размер тела запроса: 20 МБ под вложения (совпадает с AttachmentLimits).
// Больше — сразу 413, без чтения тела в память.
//
// Под IIS лимит задаётся в web.config (requestLimits maxAllowedContentLength): его
// проверяет сам IIS до передачи запроса приложению, поэтому тип IISServerOptions
// здесь не нужен — он, к тому же, живёт в сборке Microsoft.AspNetCore.Server.IIS,
// которой нет в общем фреймворке Microsoft.AspNetCore.App.
const long maxRequestBodyBytes = 20 * 1024 * 1024L;

builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = maxRequestBodyBytes);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(form => form.MultipartBodyLengthLimit = maxRequestBodyBytes);

var app = builder.Build();

// ------------------------------------------------------------------ конвейер

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);

    // HSTS — только когда он включён: получив заголовок, браузер перестанет пускать
    // сайт по http на весь срок, и при истёкшем сертификате обойти это будет нельзя.
    if (security.EnableHsts)
    {
        app.UseHsts();
    }
}

// Заголовки безопасности — до всего остального, чтобы они попадали и в ответы с ошибками.
app.UseMiddleware<SecurityHeadersMiddleware>();

// http -> https: включается Security:EnableHttpsRedirection после настройки сертификата.
if (app.Configuration.GetValue<bool>("Security:EnableHttpsRedirection"))
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

// Статика уже отдана, поэтому лимиты действуют только на страницы и API.
app.UseRateLimiter();

if (corsOrigins.Length > 0)
{
    app.UseCors();
}

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

// Токен для клиентов API (Swagger, внешние виджеты): без него POST/PUT/DELETE вернут 400.
// Отдаётся только авторизованным; прочитать его с чужого домена не выйдет — мешает same-origin.
app.MapGet("/account/antiforgery-token", (IAntiforgery antiforgery, HttpContext httpContext) =>
{
    var tokens = antiforgery.GetAndStoreTokens(httpContext);
    return Results.Ok(new { token = tokens.RequestToken, headerName = "RequestVerificationToken" });
})
.RequireAuthorization()
.ExcludeFromDescription();

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

    foreach (var warning in startupWarnings)
    {
        logger.LogWarning("{Warning}", warning);
    }

    logger.LogInformation(
        "Защита: заголовки — {Headers}, CSP — {Csp}, лимит сайта — {Global}/мин, вход — {Login}/мин, CORS — {Cors}",
        security.EnableSecurityHeaders ? "включены" : "ВЫКЛЮЧЕНЫ",
        string.IsNullOrWhiteSpace(security.BuildContentSecurityPolicy()) ? "не задана" : "включена",
        security.GlobalRequestsPerMinute,
        security.LoginAttemptsPerMinute,
        corsOrigins.Length == 0 ? "кросс-доменные запросы запрещены" : string.Join(", ", corsOrigins));
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

/// <summary>
/// Ключ для лимитов запросов. IPv6-адрес, отображённый на IPv4, приводим к обычному виду,
/// иначе один и тот же клиент попадает в разные корзины.
/// </summary>
file static class ClientKeyHelper
{
    public static string Get(HttpContext httpContext)
    {
        var ip = httpContext.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return "unknown";
        }

        return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4().ToString() : ip.ToString();
    }
}

/// <summary>Точка входа, доступная для интеграционных тестов (WebApplicationFactory).</summary>
public partial class Program;
