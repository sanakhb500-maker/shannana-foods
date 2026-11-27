using Erp.Application.Common;
using Erp.Application.Expenses;
using Erp.Domain.Common;
using Erp.Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>Supplier bills for services. Read like purchasing data; written by Admin, Accountant and Purchasing.</summary>
[ApiController]
[Authorize(Policy = Policies.ViewPurchasing)]
[Route("api/v1/expenseinvoices")]
public class ExpenseInvoicesController(IExpenseService svc, IValidator<ExpenseInvoiceInput> validator, IAuthorizationService auth) : ControllerBase
{
    private async Task<bool> CanWriteAsync() => (await auth.AuthorizeAsync(User, Policies.ManageExpenseInvoices)).Succeeded;

    private async Task<IActionResult?> InvalidAsync(ExpenseInvoiceInput input, CancellationToken ct)
    {
        if (input is null) return BadRequest(ApiResponse.Fail("Request body is required."));
        var result = await validator.ValidateAsync(input, ct);
        if (result.IsValid) return null;
        return BadRequest(ApiResponse.Fail("Validation failed.", result.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] int? supplierId, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListInvoicesAsync(new ExpenseFilter(status, supplierId, q, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetInvoiceAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ExpenseInvoiceInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(input, ct) is { } bad) return bad;
        var created = await svc.CreateInvoiceAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ExpenseInvoiceInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.UpdateInvoiceAsync(id, input, ct)));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.ConfirmInvoiceAsync(id, ct)));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.CancelInvoiceAsync(id, ct)));
}

/// <summary>Expenses paid on the spot (cash or bank). Accounting data: Admin and Accountant only.</summary>
[ApiController]
[Authorize(Policy = Policies.ViewAccounting)]
[Route("api/v1/expensevouchers")]
public class ExpenseVouchersController(IExpenseService svc, IValidator<ExpenseVoucherInput> validator, IAuthorizationService auth) : ControllerBase
{
    private async Task<bool> CanWriteAsync() => (await auth.AuthorizeAsync(User, Policies.ManageAccounting)).Succeeded;

    private async Task<IActionResult?> InvalidAsync(ExpenseVoucherInput input, CancellationToken ct)
    {
        if (input is null) return BadRequest(ApiResponse.Fail("Request body is required."));
        var result = await validator.ValidateAsync(input, ct);
        if (result.IsValid) return null;
        return BadRequest(ApiResponse.Fail("Validation failed.", result.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListVouchersAsync(new ExpenseFilter(status, null, q, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetVoucherAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ExpenseVoucherInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(input, ct) is { } bad) return bad;
        var created = await svc.CreateVoucherAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ExpenseVoucherInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.UpdateVoucherAsync(id, input, ct)));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.ConfirmVoucherAsync(id, ct)));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.CancelVoucherAsync(id, ct)));
}
