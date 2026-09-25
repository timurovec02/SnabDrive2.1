using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
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

builder.Services.AddDbContext<RegistryDbContext>(configureProvider);
builder.Services.AddDbContextFactory<RegistryDbContext>(configureProvider);
builder.Services.AddDbContext<AppIdentityDbContext>(configureProvider);
builder.Services.AddDbContextFactory<AppIdentityDbContext>(configureProvider);

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

    options.AddPolicy(AppPolicies.ManageSystem, policy => policy.RequireRole(AppRoles.Admin));
    options.AddPolicy(AppPolicies.ManageArchive, policy => policy.RequireRole(AppRoles.Admin));
    options.AddPolicy(AppPolicies.WriteRegistry, policy => policy.RequireRole(AppRoles.Admin, AppRoles.Operator));
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
        Title = "SnabDrive Web API",
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
builder.Services.AddScoped<IDictionaryService, DictionaryService>();
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
