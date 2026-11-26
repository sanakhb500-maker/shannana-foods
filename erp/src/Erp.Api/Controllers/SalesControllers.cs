using Erp.Application.Common;
using Erp.Application.Sales;
using Erp.Domain.Common;
using Erp.Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Api.Controllers;

/// <summary>Reading sales data needs <see cref="Policies.ViewSales"/>; each controller names the policy for writes.</summary>
[Authorize(Policy = Policies.ViewSales)]
public abstract class SalesControllerBase(IAuthorizationService auth) : ControllerBase
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
[Route("api/v1/salesorders")]
public class SalesOrdersController(ISalesService svc, IValidator<SalesOrderInput> validator, IAuthorizationService auth) : SalesControllerBase(auth)
{
    protected override string WritePolicy => Policies.ManageSales;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] int? customerId, [FromQuery] string? q,
        [FromQuery] bool pendingOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListOrdersAsync(new SalesFilter(status, customerId, q, pendingOnly, false, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetOrderAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SalesOrderInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        var created = await svc.CreateOrderAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SalesOrderInput input, CancellationToken ct)
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
[Route("api/v1/deliverynotes")]
public class DeliveryNotesController(ISalesService svc, IValidator<DeliveryNoteInput> validator, IAuthorizationService auth) : SalesControllerBase(auth)
{
    protected override string WritePolicy => Policies.ManageDeliveries;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] int? customerId, [FromQuery] string? q,
        [FromQuery] bool unassignedOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListDeliveriesAsync(new SalesFilter(status, customerId, q, false, unassignedOnly, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetDeliveryAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DeliveryNoteInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        var created = await svc.CreateDeliveryAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] DeliveryNoteInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.UpdateDeliveryAsync(id, input, ct)));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.ConfirmDeliveryAsync(id, ct)));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.CancelDeliveryAsync(id, ct)));
}

[ApiController]
[Route("api/v1/salesinvoices")]
public class SalesInvoicesController(ISalesService svc, IValidator<SalesInvoiceInput> validator, IAuthorizationService auth) : SalesControllerBase(auth)
{
    protected override string WritePolicy => Policies.ManageSales;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] int? customerId, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListInvoicesAsync(new SalesFilter(status, customerId, q, false, false, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetInvoiceAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SalesInvoiceInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        var created = await svc.CreateInvoiceAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SalesInvoiceInput input, CancellationToken ct)
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
[Route("api/v1/salesreturns")]
public class SalesReturnsController(ISalesService svc, IValidator<SalesReturnInput> validator, IAuthorizationService auth) : SalesControllerBase(auth)
{
    protected override string WritePolicy => Policies.ManageDeliveries;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] int? customerId, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListReturnsAsync(new SalesFilter(status, customerId, q, false, false, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetReturnAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SalesReturnInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        var created = await svc.CreateReturnAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SalesReturnInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.UpdateReturnAsync(id, input, ct)));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.ConfirmReturnAsync(id, ct)));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.CancelReturnAsync(id, ct)));
}

[ApiController]
[Route("api/v1/customerreceipts")]
public class CustomerReceiptsController(ISalesService svc, IValidator<CustomerReceiptInput> validator, IAuthorizationService auth) : SalesControllerBase(auth)
{
    protected override string WritePolicy => Policies.ManageCustomerReceipts;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DocumentStatus? status, [FromQuery] int? customerId, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListReceiptsAsync(new SalesFilter(status, customerId, q, false, false, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetReceiptAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CustomerReceiptInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(validator, input, ct) is { } bad) return bad;
        var created = await svc.CreateReceiptAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CustomerReceiptInput input, CancellationToken ct)
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
[Route("api/v1/trips")]
public class TripsController(ISalesService svc, IValidator<TripInput> tripValidator, IValidator<StopStatusInput> stopValidator, IAuthorizationService auth)
    : SalesControllerBase(auth)
{
    protected override string WritePolicy => Policies.ManageTrips;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] TripStatus? status, [FromQuery] int? driverId, [FromQuery] int? vehicleId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListTripsAsync(new TripFilter(status, driverId, vehicleId, from, to, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetTripAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TripInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(tripValidator, input, ct) is { } bad) return bad;
        var created = await svc.CreateTripAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] TripInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(tripValidator, input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.UpdateTripAsync(id, input, ct)));
    }

    [HttpPost("{id:int}/start")]
    public async Task<IActionResult> Start(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.StartTripAsync(id, ct)));

    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> Complete(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.CompleteTripAsync(id, ct)));

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.CancelTripAsync(id, ct)));

    [HttpPut("{id:int}/stops/{stopId:int}")]
    public async Task<IActionResult> SetStop(int id, int stopId, [FromBody] StopStatusInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(stopValidator, input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.SetStopStatusAsync(id, stopId, input, ct)));
    }
}

[ApiController]
[Authorize(Policy = Policies.ViewSales)]
[Route("api/v1/sales")]
public class SalesReportsController(ISalesService svc) : ControllerBase
{
    [HttpGet("customer-statement")]
    public async Task<IActionResult> Statement([FromQuery] int customerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(ApiResponse.Ok(await svc.GetStatementAsync(customerId, from, to, ct)));

    [HttpGet("customer-balances")]
    public async Task<IActionResult> Balances(CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetCustomerBalancesAsync(ct)));
}
