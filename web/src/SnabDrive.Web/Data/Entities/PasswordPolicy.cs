namespace SnabDrive.Web.Data.Entities;

/// <summary>
/// Политика сложности паролей. Одна строка (Id = 1), настраивается администратором
/// на вкладке «Пользователи» → «Политика паролей». Значения по умолчанию повторяют
/// былые жёсткие требования (10 символов, 3 различных, все классы символов).
/// </summary>
public class PasswordPolicy
{
    public int Id { get; set; } = 1;

    /// <summary>Минимальная длина пароля.</summary>
    public int MinLength { get; set; } = 10;

    /// <summary>Минимум различных символов в пароле.</summary>
    public int MinUniqueChars { get; set; } = 3;

    /// <summary>Хотя бы одна заглавная буква.</summary>
    public bool RequireUppercase { get; set; } = true;

    /// <summary>Хотя бы одна строчная буква.</summary>
    public bool RequireLowercase { get; set; } = true;

    /// <summary>Хотя бы одна цифра.</summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>Хотя бы один спецсимвол (не буква и не цифра).</summary>
    public bool RequireSpecial { get; set; } = true;

    public DateTime? UpdatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }
}
