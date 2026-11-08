using Microsoft.Extensions.Options;

namespace SnabDrive.Web.Services.Security;

/// <summary>
/// Заголовки безопасности на каждый ответ.
///
/// Что закрывает:
///  • Content-Security-Policy — выполнение сторонних скриптов и встраивание сайта в iframe
///    (clickjacking) даже если злоумышленник сумеет вставить свой тег в страницу;
///  • X-Content-Type-Options: nosniff — браузер не «угадывает» тип файла, поэтому
///    загруженный пользователем файл нельзя выдать за исполняемый скрипт;
///  • X-Frame-Options: DENY — дубль frame-ancestors для старых браузеров;
///  • Referrer-Policy — в Referer не уходит полный адрес страницы с идентификаторами;
///  • Permissions-Policy — отключены не используемые сайтом геолокация, камера, микрофон;
///  • Cross-Origin-* — чужой сайт не может ни встроить наши ответы, ни читать их.
///
/// Заголовки ставятся ДО передачи запроса дальше: к этому моменту ответ ещё не начал
/// отправляться, поэтому правка коллекции безопасна.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityOptions _options;
    private readonly string _csp;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecurityOptions> options)
    {
        _next = next;
        _options = options.Value;
        _csp = _options.BuildContentSecurityPolicy();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_options.EnableSecurityHeaders)
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] =
                "geolocation=(), microphone=(), camera=(), payment=(), usb=(), magnetometer=(), gyroscope=(), accelerometer=(), fullscreen=(self)";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";

            if (_csp.Length > 0)
            {
                headers["Content-Security-Policy"] = _csp;
            }
        }

        // IIS добавляет X-Powered-By на своём уровне; дублируем снятие здесь, чтобы
        // не сообщать версию платформы при запуске без IIS (Kestrel, Linux, контейнер).
        context.Response.Headers.Remove("X-Powered-By");
        context.Response.Headers.Remove("Server");

        await _next(context);
    }
}
