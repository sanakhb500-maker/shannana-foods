using Erp.Application.Common;
using Erp.Application.Inventory;
using Erp.Domain.Common;
using Erp.Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>Shared plumbing: write-permission check and validation → 400 envelope.</summary>
public abstract class InventoryControllerBase(IAuthorizationService auth) : ControllerBase
{
    protected async Task<bool> CanWriteAsync() => (await auth.AuthorizeAsync(User, Policies.ManageInventory)).Succeeded;

    protected async Task<IActionResult?> InvalidAsync<T>(IValidator<T> validator, T input, CancellationToken ct)
    {
        if (input is null) return BadRequest(ApiResponse.Fail("Request body is required."));
        var result = await validator.ValidateAsync(input, ct);
        if (result.IsValid) return null;
        var errors = result.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return BadRequest(ApiResponse.Fail("Validation failed.", errors));
    }
}

[ApiController]
[Authorize]
[Route("api/v1/stocktransfers")]
public class StockTransfersController(IInventoryService svc, IValidator<TransferInput> validator, IAuthorizationService auth)
    : InventoryControllerBase(auth)
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] int? warehouseId, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListTransfersAsync(new DocumentFilter(status, warehouseId, q, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetTransferAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TransferInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        var created = await svc.CreateTransferAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] TransferInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.UpdateTransferAsync(id, input, ct)));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.ConfirmTransferAsync(id, ct)));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.CancelTransferAsync(id, ct)));
}

[ApiController]
[Authorize]
[Route("api/v1/stockadjustments")]
public class StockAdjustmentsController(IInventoryService svc, IValidator<AdjustmentInput> validator, IAuthorizationService auth)
    : InventoryControllerBase(auth)
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] int? warehouseId, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListAdjustmentsAsync(new DocumentFilter(status, warehouseId, q, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetAdjustmentAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdjustmentInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        var created = await svc.CreateAdjustmentAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AdjustmentInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.UpdateAdjustmentAsync(id, input, ct)));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.ConfirmAdjustmentAsync(id, ct)));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.CancelAdjustmentAsync(id, ct)));
}

/// <summary>Read-only stock reports. Any signed-in user may view them.</summary>
[ApiController]
[Authorize]
[Route("api/v1/stock")]
public class StockReportsController(IInventoryService svc) : ControllerBase
{
    [HttpGet("balances")]
    public async Task<IActionResult> Balances([FromQuery] int? warehouseId, [FromQuery] int? itemId, [FromQuery] string? q,
        [FromQuery] bool nonZeroOnly = true, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.GetBalancesAsync(new BalanceFilter(warehouseId, itemId, q, nonZeroOnly, page, pageSize), ct)));

    [HttpGet("movements")]
    public async Task<IActionResult> Movements([FromQuery] int? warehouseId, [FromQuery] int? itemId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? documentType, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.GetMovementsAsync(new MovementFilter(warehouseId, itemId, from, to, documentType, page, pageSize), ct)));

    [HttpGet("low-stock")]
    public async Task<IActionResult> LowStock(CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetLowStockAsync(ct)));
}
