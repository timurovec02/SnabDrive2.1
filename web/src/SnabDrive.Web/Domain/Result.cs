namespace SnabDrive.Web.Domain;

/// <summary>Результат операции без возвращаемого значения.</summary>
public sealed class Result
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public IReadOnlyDictionary<string, string> FieldErrors { get; init; } = new Dictionary<string, string>();

    public static Result Ok() => new() { Success = true };

    public static Result Fail(string error) => new() { Success = false, Error = error };

    public static Result Invalid(IReadOnlyDictionary<string, string> fieldErrors) => new()
    {
        Success = false,
        Error = "Проверьте правильность заполнения полей.",
        FieldErrors = fieldErrors
    };
}

/// <summary>Результат операции со значением.</summary>
public sealed class Result<T>
{
    public bool Success { get; init; }

    public T? Value { get; init; }

    public string? Error { get; init; }

    public IReadOnlyDictionary<string, string> FieldErrors { get; init; } = new Dictionary<string, string>();

    public static Result<T> Ok(T value) => new() { Success = true, Value = value };

    public static Result<T> Fail(string error) => new() { Success = false, Error = error };

    public static Result<T> Invalid(IReadOnlyDictionary<string, string> fieldErrors) => new()
    {
        Success = false,
        Error = "Проверьте правильность заполнения полей.",
        FieldErrors = fieldErrors
    };
}
