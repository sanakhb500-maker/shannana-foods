using Erp.Application.Common;
using Erp.Application.Purchasing;
using Erp.Domain.Common;
using Erp.Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>
/// Purchasing data holds cost prices, so reading needs <see cref="Policies.ViewPurchasing"/> (not every signed-in user).
/// Writes need the policy each controller names.
/// </summary>
[Authorize(Policy = Policies.ViewPurchasing)]
public abstract class PurchasingControllerBase(IAuthorizationService auth) : ControllerBase
{
    protected abstract string WritePolicy { get; }

    protected async Task<bool> CanWriteAsync() => (await auth.AuthorizeAsync(User, WritePolicy)).Succeeded;

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
[Route("api/v1/purchaseorders")]
public class PurchaseOrdersController(IPurchasingService svc, IValidator<PurchaseOrderInput> validator, IAuthorizationService auth)
    : PurchasingControllerBase(auth)
{
    protected override string WritePolicy => Policies.ManagePurchasing;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] int? supplierId, [FromQuery] string? q,
        [FromQuery] bool pendingOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListOrdersAsync(new PurchasingFilter(status, supplierId, q, pendingOnly, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetOrderAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PurchaseOrderInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        var created = await svc.CreateOrderAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] PurchaseOrderInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.UpdateOrderAsync(id, input, ct)));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.ConfirmOrderAsync(id, ct)));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.CancelOrderAsync(id, ct)));
}

[ApiController]
[Route("api/v1/goodsreceipts")]
public class GoodsReceiptsController(IPurchasingService svc, IValidator<GoodsReceiptInput> validator, IAuthorizationService auth)
    : PurchasingControllerBase(auth)
{
    protected override string WritePolicy => Policies.ReceiveGoods;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] int? supplierId, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListReceiptsAsync(new PurchasingFilter(status, supplierId, q, false, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetReceiptAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] GoodsReceiptInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        var created = await svc.CreateReceiptAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] GoodsReceiptInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.UpdateReceiptAsync(id, input, ct)));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.ConfirmReceiptAsync(id, ct)));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.CancelReceiptAsync(id, ct)));
}

[ApiController]
[Route("api/v1/purchaseinvoices")]
public class PurchaseInvoicesController(IPurchasingService svc, IValidator<PurchaseInvoiceInput> validator, IAuthorizationService auth)
    : PurchasingControllerBase(auth)
{
    protected override string WritePolicy => Policies.ManagePurchasing;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] int? supplierId, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListInvoicesAsync(new PurchasingFilter(status, supplierId, q, false, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetInvoiceAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PurchaseInvoiceInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        var created = await svc.CreateInvoiceAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] PurchaseInvoiceInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.UpdateInvoiceAsync(id, input, ct)));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.ConfirmInvoiceAsync(id, ct)));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.CancelInvoiceAsync(id, ct)));
}

[ApiController]
[Route("api/v1/supplierpayments")]
public class SupplierPaymentsController(IPurchasingService svc, IValidator<SupplierPaymentInput> validator, IAuthorizationService auth)
    : PurchasingControllerBase(auth)
{
    protected override string WritePolicy => Policies.ManageSupplierPayments;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] int? supplierId, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListPaymentsAsync(new PurchasingFilter(status, supplierId, q, false, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetPaymentAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SupplierPaymentInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        var created = await svc.CreatePaymentAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SupplierPaymentInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.UpdatePaymentAsync(id, input, ct)));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.ConfirmPaymentAsync(id, ct)));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.CancelPaymentAsync(id, ct)));
}

[ApiController]
[Authorize(Policy = Policies.ViewPurchasing)]
[Route("api/v1/purchasing")]
public class PurchasingReportsController(IPurchasingService svc) : ControllerBase
{
    [HttpGet("supplier-statement")]
    public async Task<IActionResult> Statement([FromQuery] int supplierId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(ApiResponse.Ok(await svc.GetStatementAsync(supplierId, from, to, ct)));

    [HttpGet("open-invoices")]
    public async Task<IActionResult> OpenInvoices([FromQuery] int? supplierId, CancellationToken ct) =>
        Ok(ApiResponse.Ok(await svc.GetOpenInvoicesAsync(supplierId, ct)));

    [HttpGet("ageing")]
    public async Task<IActionResult> Ageing(CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetAgeingAsync(ct)));

    [HttpGet("supplier-balances")]
    public async Task<IActionResult> Balances(CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetSupplierBalancesAsync(ct)));
}
