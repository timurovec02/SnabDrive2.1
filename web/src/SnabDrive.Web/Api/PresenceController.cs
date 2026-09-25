using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SnabDrive.Web.Domain;
using SnabDrive.Web.Services;

namespace SnabDrive.Web.Api;

/// <summary>Кто сейчас работает в системе.</summary>
[ApiController]
[Route("api/presence")]
[Authorize]
[Produces("application/json")]
public class PresenceController : ControllerBase
{
    private readonly IPresenceService _presence;

    public PresenceController(IPresenceService presence)
    {
        _presence = presence;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<OnlineUser>> Get() => Ok(_presence.GetOnline());

    [HttpGet("count")]
    public ActionResult<int> Count() => Ok(_presence.OnlineCount);
}
