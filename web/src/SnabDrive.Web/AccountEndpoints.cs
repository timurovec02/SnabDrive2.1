using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
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
        app.MapPost("/account/login", HandleLoginAsync)
            .AllowAnonymous()
            .AddEndpointFilter(AntiforgeryFilter("/login?error=token"));

        app.MapPost("/account/logout", HandleLogoutAsync)
            .RequireAuthorization()
            .AddEndpointFilter(AntiforgeryFilter("/login?error=token"));

        return app;
    }

    /// <summary>
    /// Проверка antiforgery-токна для form-POST. Без неё любой сайт мог бы отправить
    /// форму входа/выхода от имени пользователя.
    /// </summary>
    private static EndpointFilterDelegate AntiforgeryFilter(string failureRedirect) =>
        async (context, next) =>
        {
            var httpContext = context.HttpContext;
            var antiforgery = httpContext.RequestServices.GetRequiredService<IAntiforgery>();

            try
            {
                await antiforgery.ValidateRequestAsync(httpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.Redirect(failureRedirect);
            }

            return await next(context);
        };

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
