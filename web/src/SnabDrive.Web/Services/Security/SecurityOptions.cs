namespace SnabDrive.Web.Services.Security;

/// <summary>
/// Настройки защиты сайта (секция <c>Security</c> в appsettings).
/// Всё, что может потребоваться поменять на боевом сервере, вынесено сюда:
/// заголовки безопасности, политика CSP, лимиты запросов, источники CORS.
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// Политика Content-Security-Policy по умолчанию — рекомендованный Microsoft набор
    /// для Blazor Server: свои скрипты и стили, ничего стороннего, встраивание страницы
    /// в iframe запрещено (frame-ancestors 'none').
    ///
    /// 'unsafe-inline' оставлен только для стилей: разметка страниц активно использует
    /// атрибуты style (подсветка ячеек, рамки полей по ПКМ, «Звони»).
    /// </summary>
    public static readonly string DefaultContentSecurityPolicy = string.Join("; ", new[]
    {
        "default-src 'self'",
        "script-src 'self'",
        "style-src 'self' 'unsafe-inline'",
        "img-src 'self' data:",
        "font-src 'self'",
        "media-src 'self'",
        "connect-src 'self' ws: wss:",   // SignalR / циркул Blazor
        "object-src 'none'",
        "frame-ancestors 'none'",
        "base-uri 'self'",
        "form-action 'self'"
    });

    /// <summary>Добавлять ли заголовки безопасности к каждому ответу.</summary>
    public bool EnableSecurityHeaders { get; set; } = true;

    /// <summary>Значение заголовка Content-Security-Policy. Пустая строка — заголовок не добавляется.</summary>
    public string ContentSecurityPolicy { get; set; } = DefaultContentSecurityPolicy;

    /// <summary>
    /// Добавлять ли к CSP директиву upgrade-insecure-requests (браузер сам переводит
    /// http-запросы на https). Включать ТОЛЬКО после того, как на сервере реально
    /// работает HTTPS, иначе сайт перестанет открываться по http.
    /// </summary>
    public bool UpgradeInsecureRequests { get; set; }

    /// <summary>
    /// Политика cookie аутентификации:
    /// <c>SameAsRequest</c> — метка Secure ставится только на https-запросах (безопасно
    /// и не ломает вход, если сайт пока работает по http);
    /// <c>Always</c> — cookie передаётся только по https (включайте вместе с HTTPS).
    /// </summary>
    public string CookieSecurePolicy { get; set; } = "SameAsRequest";

    /// <summary>
    /// Папка для ключей Data Protection (шифрование cookie и antiforgery-токенов).
    /// Пусто — стандартное место; на IIS лучше задать явный путь, иначе ключи живут
    /// в профиле пула и все сессии сбрасываются при каждом ресайкле.
    /// </summary>
    public string DataProtectionKeysPath { get; set; } = string.Empty;

    /// <summary>Запросов в минуту с одного IP на всё приложение (кроме статики).</summary>
    public int GlobalRequestsPerMinute { get; set; } = 1200;

    /// <summary>Запросов в минуту на один эндпоинт API с одного IP.</summary>
    public int ApiRequestsPerMinute { get; set; } = 600;

    /// <summary>Попыток входа в минуту с одного IP — защита от перебора паролей.</summary>
    public int LoginAttemptsPerMinute { get; set; } = 10;

    /// <summary>
    /// Разрешённые источники для CORS. Пустой список — кросс-доменные запросы запрещены
    /// (браузер их заблокирует, заголовки Access-Control-Allow-* не выдаются).
    /// Пример: [ "https://portal.company.local" ].
    /// </summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    /// <summary>Итоговая политика CSP с учётом <see cref="UpgradeInsecureRequests"/>.</summary>
    public string BuildContentSecurityPolicy()
    {
        var policy = (ContentSecurityPolicy ?? string.Empty).Trim();
        if (policy.Length == 0)
        {
            return string.Empty;
        }

        if (!UpgradeInsecureRequests ||
            policy.Contains("upgrade-insecure-requests", StringComparison.OrdinalIgnoreCase))
        {
            return policy;
        }

        return policy.TrimEnd(';') + "; upgrade-insecure-requests";
    }

    /// <summary>Разбор <see cref="CookieSecurePolicy"/> без падения на опечатке в конфиге.</summary>
    public CookieSecurePolicyValue ResolveCookieSecurePolicy() =>
        string.Equals(CookieSecurePolicy, "Always", StringComparison.OrdinalIgnoreCase)
            ? CookieSecurePolicyValue.Always
            : CookieSecurePolicyValue.SameAsRequest;
}

/// <summary>Значения политики Secure для cookie (свой дубль, чтобы не тянуть ASP.NET-тип в конфиг).</summary>
public enum CookieSecurePolicyValue
{
    SameAsRequest,
    Always
}
