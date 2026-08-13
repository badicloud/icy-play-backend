using IcyPlay.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
public sealed class SystemController : ControllerBase
{
    [HttpGet("info")]
    [ProducesResponseType(typeof(ApiEnvelope<SystemInfoResponse>), StatusCodes.Status200OK)]
    public ActionResult<ApiEnvelope<SystemInfoResponse>> GetInfo()
    {
        return Ok(new ApiEnvelope<SystemInfoResponse>(
            new SystemInfoResponse(
                "IcyPlay API",
                ApiConstants.ApiVersion,
                "Ready")));
    }
}

public sealed record SystemInfoResponse(
    string Name,
    string Version,
    string Status);
