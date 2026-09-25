using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Services;

namespace SnabDrive.Web.Api;

/// <summary>Пользователи и роли (только администратор).</summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = AppPolicies.ManageSystem)]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _audit;
    private readonly IRegistryNotifier _notifier;
    private readonly IPresenceService _presence;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        IAuditService audit,
        IRegistryNotifier notifier,
        IPresenceService presence)
    {
        _userManager = userManager;
        _audit = audit;
        _notifier = notifier;
        _presence = presence;
    }

    public sealed record UserRow(
        string Id,
        string UserName,
        string? DisplayName,
        string? Email,
        bool IsAdmin,
        bool IsBlocked,
        bool IsOnline,
        DateTime? LastLoginUtc,
        DateTime? LastSeenUtc);

    public sealed record UpsertUserRequest(string UserName, string? DisplayName, string? Email, string? Password, bool IsAdmin, bool IsBlocked);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserRow>>> Get(CancellationToken cancellationToken)
    {
        var onlineIds = _presence.GetOnline().Select(u => u.UserId).ToHashSet(StringComparer.Ordinal);

        var users = await _userManager.Users
            .OrderBy(u => u.UserName)
            .ToListAsync(cancellationToken);

        var rows = new List<UserRow>(users.Count);
        foreach (var user in users)
        {
            var isAdmin = await _userManager.IsInRoleAsync(user, AppRoles.Admin);
            rows.Add(new UserRow(user.Id, user.UserName ?? string.Empty, user.DisplayName, user.Email,
                isAdmin, user.IsBlocked, onlineIds.Contains(user.Id), user.LastLoginUtc, user.LastSeenUtc));
        }

        return Ok(rows);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] UpsertUserRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "Укажите логин и пароль" });
        }

        if (await _userManager.FindByNameAsync(request.UserName.Trim()) is not null)
        {
            return BadRequest(new { error = $"Пользователь «{request.UserName}» уже существует" });
        }

        var user = new ApplicationUser
        {
            UserName = request.UserName.Trim(),
            DisplayName = request.DisplayName,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email,
            EmailConfirmed = !string.IsNullOrWhiteSpace(request.Email),
            IsBlocked = request.IsBlocked
        };

        var created = await _userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return BadRequest(new { error = string.Join("; ", created.Errors.Select(e => e.Description)) });
        }

        await _userManager.AddToRoleAsync(user, request.IsAdmin ? AppRoles.Admin : AppRoles.Operator);

        await NotifyAsync(AuditAction.Created, user.Id, $"Создан пользователь «{user.UserName}»", cancellationToken);
        return Created($"/api/users/{user.Id}", user.Id);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpsertUserRequest request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        user.DisplayName = request.DisplayName;
        user.IsBlocked = request.IsBlocked;

        if (!string.IsNullOrWhiteSpace(request.Email) && !string.Equals(user.Email, request.Email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = request.Email;
            user.EmailConfirmed = true;
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await _userManager.ResetPasswordAsync(user, token, request.Password);
            if (!reset.Succeeded)
            {
                return BadRequest(new { error = string.Join("; ", reset.Errors.Select(e => e.Description)) });
            }
        }

        var updated = await _userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return BadRequest(new { error = string.Join("; ", updated.Errors.Select(e => e.Description)) });
        }

        await SyncRoleAsync(user, request.IsAdmin);
        await NotifyAsync(AuditAction.Updated, 0, $"Изменён пользователь «{user.UserName}»", cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (string.Equals(user.Id, User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, StringComparison.Ordinal))
        {
            return BadRequest(new { error = "Нельзя удалить собственную учётную запись" });
        }

        var admins = await _userManager.GetUsersInRoleAsync(AppRoles.Admin);
        if (await _userManager.IsInRoleAsync(user, AppRoles.Admin) && admins.Count <= 1)
        {
            return BadRequest(new { error = "Это последний администратор — сначала назначьте права другому пользователю" });
        }

        var deleted = await _userManager.DeleteAsync(user);
        if (!deleted.Succeeded)
        {
            return BadRequest(new { error = string.Join("; ", deleted.Errors.Select(e => e.Description)) });
        }

        await NotifyAsync(AuditAction.Deleted, 0, $"Удалён пользователь «{user.UserName}»", cancellationToken);
        return NoContent();
    }

    private async Task SyncRoleAsync(ApplicationUser user, bool isAdmin)
    {
        var currentlyAdmin = await _userManager.IsInRoleAsync(user, AppRoles.Admin);

        if (isAdmin && !currentlyAdmin)
        {
            await _userManager.RemoveFromRoleAsync(user, AppRoles.Operator);
            await _userManager.AddToRoleAsync(user, AppRoles.Admin);
        }
        else if (!isAdmin && currentlyAdmin)
        {
            await _userManager.RemoveFromRoleAsync(user, AppRoles.Admin);
            await _userManager.AddToRoleAsync(user, AppRoles.Operator);
        }

        // Меняем security stamp — сессии пользователя будут перепроверены и права обновятся.
        await _userManager.UpdateSecurityStampAsync(user);
    }

    private async Task NotifyAsync(string action, int entityId, string summary, CancellationToken cancellationToken)
    {
        await _audit.WriteAsync(action, "User", entityId, User.ToActor(), summary: summary, cancellationToken: cancellationToken);

        await _notifier.PublishAsync(new Domain.RegistryChangeEvent(
            Domain.ChangeKind.UserChanged, "User", entityId, User.Identity?.Name ?? "-", DateTime.UtcNow, summary),
            cancellationToken);
    }
}
