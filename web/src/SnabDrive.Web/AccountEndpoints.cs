using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;
using SnabDrive.Web.Services;

namespace SnabDrive.Web;

/// <summary>
/// Вход и выход. Сделаны minimal API-эндпоинтами, а не Blazor-кодом:
/// установить авторизационную cookie можно только в рамках HTTP-запроса,
/// а у интерактивного Blazor-циркула своего HttpContext уже нет.
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // RequireRateLimiting("login") — не больше Security:LoginAttemptsPerMinute попыток
        // в минуту с одного IP: перебор пароля упирается в 429 ещё до проверки учётки.
        app.MapPost("/account/login", HandleLoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("login")
            .AddEndpointFilter(new AntiforgeryEndpointFilter("/login?error=token"));

        app.MapPost("/account/logout", HandleLogoutAsync)
            .RequireAuthorization()
            .AddEndpointFilter(new AntiforgeryEndpointFilter("/login?error=token"));

        // Смена пароля — тоже form-POST: в интерактивном Blazor нет HttpContext,
        // а cookie (клейм MustChangePassword) можно перевыпустить только в HTTP-запросе.
        app.MapPost("/account/change-password", HandleChangePasswordAsync)
            .RequireAuthorization()
            .RequireRateLimiting("login")
            .AddEndpointFilter(new AntiforgeryEndpointFilter("/change-password?error=token"));

        return app;
    }

    /// <summary>
    /// Проверка antiforgery-токна для form-POST. Без неё любой сайт мог бы отправить
    /// форму входа/выхода от имени пользователя.
    /// </summary>
    /// <summary>Проверка antiforgery-токена form-POST перед выполнением эндпоинта.</summary>
    private sealed class AntiforgeryEndpointFilter : IEndpointFilter
    {
        private readonly string _failureRedirect;

        public AntiforgeryEndpointFilter(string failureRedirect) => _failureRedirect = failureRedirect;

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var httpContext = context.HttpContext;
            var antiforgery = httpContext.RequestServices.GetRequiredService<IAntiforgery>();

            try
            {
                await antiforgery.ValidateRequestAsync(httpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.Redirect(_failureRedirect);
            }

            return await next(context);
        }
    }

    private static async Task<IResult> HandleLogoutAsync(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.Redirect("/login?loggedout=1");
    }

    private static async Task<IResult> HandleLoginAsync(
        HttpRequest request,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuditService audit,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("SnabDrive.Web.Login");
        var form = await request.ReadFormAsync();

        var login = form["login"].ToString().Trim();
        var password = form["password"].ToString();
        var rememberMe = string.Equals(form["rememberMe"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
        var returnUrl = form["returnUrl"].ToString();

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
        {
            return Results.Redirect("/login?error=empty");
        }

        var user = await userManager.FindByNameAsync(login);

        if (user is not null && user.IsBlocked)
        {
            logger.LogWarning("Попытка входа заблокированной учётки {Login}", login);
            await audit.WriteAsync(AuditAction.LoginFailed, "User", 0, new ChangeActor(user.Id, login),
                summary: $"Вход отклонён: учётная запись заблокирована, {login}");
            return Results.Redirect("/login?error=blocked");
        }

        var result = await signInManager.PasswordSignInAsync(login, password, rememberMe, lockoutOnFailure: true);

        if (result.Succeeded && user is not null)
        {
            var principal = await signInManager.CreateUserPrincipalAsync(user);
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.Id;

            user.LastLoginUtc = DateTime.UtcNow;
            user.LastSeenUtc = DateTime.UtcNow;
            await userManager.UpdateAsync(user);

            await audit.WriteAsync(AuditAction.Login, "User", 0, new ChangeActor(userId, login),
                summary: $"Вход в систему: {login}");

            logger.LogInformation("Успешный вход: {Login}", login);

            // Первый вход: администратор создал учётку с временным паролем —
            // дальше страницы смены пароля пользователь не попадёт.
            if (user.MustChangePassword)
            {
                return Results.Redirect("/change-password?first=1");
            }

            return Results.Redirect(ToSafeLocalUrl(returnUrl));
        }

        if (result.IsLockedOut)
        {
            logger.LogWarning("Учётная запись {Login} временно заблокирована после неудачных попыток", login);
            await audit.WriteAsync(AuditAction.LoginFailed, "User", 0, new ChangeActor(user?.Id ?? "-", login),
                summary: $"Вход отклонён: блокировка после неудачных попыток, {login}");
            return Results.Redirect("/login?error=locked");
        }

        if (result.IsNotAllowed)
        {
            return Results.Redirect("/login?error=blocked");
        }

        logger.LogWarning("Неудачный вход: {Login}", login);
        await audit.WriteAsync(AuditAction.LoginFailed, "User", 0, new ChangeActor(user?.Id ?? "-", login),
            summary: $"Неверный логин или пароль: {login}");

        return Results.Redirect("/login?error=invalid");
    }

    private static async Task<IResult> HandleChangePasswordAsync(
        HttpRequest request,
        System.Security.Claims.ClaimsPrincipal principal,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuditService audit,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("SnabDrive.Web.ChangePassword");
        var form = await request.ReadFormAsync();

        var current = form["currentPassword"].ToString();
        var newPassword = form["newPassword"].ToString();
        var confirm = form["confirmPassword"].ToString();
        var first = form["first"].ToString() == "1";

        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Redirect("/login");
        }

        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(newPassword))
        {
            return Results.Redirect("/change-password?error=empty" + (first ? "&first=1" : string.Empty));
        }

        if (!string.Equals(newPassword, confirm, StringComparison.Ordinal))
        {
            return Results.Redirect("/change-password?error=mismatch" + (first ? "&first=1" : string.Empty));
        }

        if (!await userManager.CheckPasswordAsync(user, current))
        {
            await audit.WriteAsync(AuditAction.PasswordChanged, "User", 0,
                new ChangeActor(user.Id, user.UserName ?? string.Empty),
                summary: "Смена пароля: неверный текущий пароль");
            return Results.Redirect("/change-password?error=invalid" + (first ? "&first=1" : string.Empty));
        }

        // ChangePasswordAsync прогоняет пароль через валидаторы, включая PolicyPasswordValidator.
        var result = await userManager.ChangePasswordAsync(user, current, newPassword);
        if (!result.Succeeded)
        {
            var message = string.Join(" ", result.Errors.Select(e => e.Description));
            logger.LogWarning("Смена пароля отклонена для {Login}: {Errors}", user.UserName, message);
            return Results.Redirect("/change-password?error=policy&msg=" + Uri.EscapeDataString(message)
                + (first ? "&first=1" : string.Empty));
        }

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);
        }

        // Перевыпускаем cookie — клейм MustChangePassword исчезает, сессия не прерывается.
        await signInManager.RefreshSignInAsync(user);

        await audit.WriteAsync(AuditAction.PasswordChanged, "User", 0,
            new ChangeActor(user.Id, user.UserName ?? string.Empty),
            summary: first ? "Первый вход: временный пароль заменён постоянным" : "Пароль изменён");

        logger.LogInformation("Пароль изменён: {Login}", user.UserName);
        return Results.Redirect(first ? "/" : "/change-password?changed=1");
    }

    /// <summary>Защита от open redirect: принимаем только локальные относительные адреса.</summary>
    internal static string ToSafeLocalUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        var isLocalRelative = returnUrl.StartsWith('/') &&
                              !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
                              !returnUrl.StartsWith("/\\", StringComparison.Ordinal);

        return isLocalRelative ? returnUrl : "/";
    }
}
