using Microsoft.AspNetCore.Identity;
using SnabDrive.Web.Data;

namespace SnabDrive.Web.Services;

/// <summary>
/// Проверка пароля по политике администратора (dbo.PasswordPolicies).
/// Подключается к UserManager и действует везде, где пароль задаётся через него:
/// создание пользователя, смена и сброс пароля — и в интерфейсе, и в API.
/// </summary>
public sealed class PolicyPasswordValidator : IPasswordValidator<ApplicationUser>
{
    private readonly IPasswordPolicyService _policy;

    public PolicyPasswordValidator(IPasswordPolicyService policy) => _policy = policy;

    public async Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        var policy = await _policy.GetAsync();
        var errors = _policy.Validate(password, policy);

        if (errors.Count == 0)
        {
            return IdentityResult.Success;
        }

        return IdentityResult.Failed(errors
            .Select(message => new IdentityError { Code = "PasswordPolicy", Description = message })
            .ToArray());
    }
}
