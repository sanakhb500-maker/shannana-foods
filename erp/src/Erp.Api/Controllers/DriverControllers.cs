using Erp.Application.Common;
using Erp.Application.DriverApp;
using Erp.Domain.Common;
using Erp.Domain.Entities;
using Erp.Infrastructure.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>The mobile app for delivery drivers. Only the Driver role gets in, and every call is scoped to the driver linked to the account.</summary>
[ApiController]
[Authorize(Policy = Policies.DriverApp)]
[Route("api/v1/driver")]
public class DriverController(IDriverService svc) : ControllerBase
{
    [HttpGet("trips")]
    public async Task<IActionResult> Trips(CancellationToken ct) => Ok(ApiResponse.Ok(await svc.MyTripsAsync(ct)));

    [HttpGet("trips/{id:int}")]
    public async Task<IActionResult> Trip(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetTripAsync(id, ct)));

    [HttpPost("trips/{id:int}/start")]
    public async Task<IActionResult> Start(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.StartTripAsync(id, ct)));

    [HttpPost("trips/{id:int}/complete")]
    public async Task<IActionResult> Complete(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.CompleteTripAsync(id, ct)));

    [HttpPut("trips/{id:int}/stops/{stopId:int}")]
    public async Task<IActionResult> Result(int id, int stopId, [FromBody] StopResultInput input, CancellationToken ct) =>
        input is null ? BadRequest(ApiResponse.Fail("Request body is required.")) : Ok(ApiResponse.Ok(await svc.SetResultAsync(id, stopId, input, ct)));

    [HttpPost("stops/{stopId:int}/collections")]
    public async Task<IActionResult> Collect(int stopId, [FromBody] CollectionInput input, CancellationToken ct) =>
        input is null ? BadRequest(ApiResponse.Fail("Request body is required.")) : Ok(ApiResponse.Ok(await svc.CollectAsync(stopId, input, ct)));

    [HttpPost("stops/{stopId:int}/proof")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> Proof(int stopId, [FromForm] string kind, IFormFile file, CancellationToken ct)
    {
        if (!Enum.TryParse<ProofKind>(kind, true, out var k) || !Enum.IsDefined(k)) return BadRequest(ApiResponse.Fail("Choose Signature or Photo."));
        if (file is null || file.Length == 0 || file.Length > DriverService.MaxProofBytes) return BadRequest(ApiResponse.Fail("Only PNG or JPEG images up to 3 MB are accepted."));
        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);
        return Ok(ApiResponse.Ok(await svc.AddProofAsync(stopId, k, ms.ToArray(), ct)));
    }

    [HttpGet("proofs/{id:int}")]
    public async Task<IActionResult> GetProof(int id, CancellationToken ct)
    {
        var f = await svc.GetProofAsync(id, true, ct);
        return PhysicalFile(f.Path, f.ContentType);
    }
}

/// <summary>Lets ERP staff look at the proofs drivers recorded (the trips screen links here).</summary>
[ApiController]
[Authorize(Policy = Policies.ViewSales)]
[Route("api/v1/stopproofs")]
public class StopProofsController(IDriverService svc) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var f = await svc.GetProofAsync(id, false, ct);
        return PhysicalFile(f.Path, f.ContentType);
    }
}
