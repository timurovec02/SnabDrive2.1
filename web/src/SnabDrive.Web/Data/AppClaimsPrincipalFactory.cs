using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SnabDrive.Web.Domain;

namespace SnabDrive.Web.Data;

/// <summary>Добавляет клейм kontur=1 пользователям с правом выгрузки из Контур.Закупки.</summary>
public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser>
{
    public AppClaimsPrincipalFactory(UserManager<ApplicationUser> userManager, IOptions<IdentityOptions> options)
        : base(userManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.KonturEnabled)
        {
            identity.AddClaim(new Claim(KonturClaim.Type, "1"));
        }

        return identity;
    }
}

public static class KonturClaim
{
    public const string Type = "snabdrive.kontur";

    public static bool CanUse(ClaimsPrincipal? user) =>
        user is not null && (user.IsInRole(SnabDrive.Web.Services.AppRoles.Admin) || user.HasClaim(Type, "1"));
}
