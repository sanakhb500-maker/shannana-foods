using Erp.Application.Accounting;
using Erp.Application.Common;
using Erp.Domain.Common;
using Erp.Domain.Entities;
using Erp.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Controllers;

/// <summary>Chart of accounts. Everyone signed in may read it; changes need <see cref="Policies.ManageAccounting"/>.</summary>
public class AccountsController(AppDbContext db, IValidator<Account> v, IAuthorizationService a) : CrudController<Account>(db, v, a)
{
    protected override string WritePolicy => Policies.ManageAccounting;
    protected override IQueryable<Account> Order(IQueryable<Account> q) => q.OrderBy(x => x.Code);
    protected override IQueryable<Account> Search(IQueryable<Account> q, string t) =>
        q.Where(x => x.Code.Contains(t) || x.NameAr.Contains(t) || x.NameEn.Contains(t));
    protected override IQueryable<Account> Filter(IQueryable<Account> q, IQueryCollection query) =>
        query["postableOnly"] == "true" ? q.Where(x => x.IsPostable) : q;

    protected override async Task<string?> BeforeSaveAsync(Account acc, Account? existing, CancellationToken ct)
    {
        acc.Code = acc.Code.ToUpperInvariant();
        // The posting role is system-owned: a client can neither set nor remove it.
        acc.SystemRole = existing?.SystemRole;

        if (acc.ParentId is { } pid)
        {
            if (existing is not null && pid == existing.Id) return "An account cannot be its own parent.";
            var parent = await Db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid, ct);
            if (parent is null) return "The parent account does not exist.";
            if (parent.IsPostable) return "The parent must be a header account (not postable).";
            if (parent.Type != acc.Type) return "An account must have the same type as its parent.";
        }

        if (existing is not null)
        {
            var hasLines = await Db.JournalLines.AnyAsync(l => l.AccountId == existing.Id, ct);
            if (hasLines && existing.Type != acc.Type) return "The type cannot change once the account has postings.";
            if (hasLines && !acc.IsPostable) return "An account with postings must stay postable.";
            if (acc.IsPostable && await Db.Accounts.AnyAsync(x => x.ParentId == existing.Id, ct)) return "An account with children must be a header (not postable).";
            if (existing.SystemRole is not null && (acc.Type != existing.Type || !acc.IsPostable))
                return "Accounts used by automatic postings must keep their type and stay postable.";
        }
        return null;
    }

    protected override async Task<string?> BeforeDeactivateAsync(Account acc, CancellationToken ct)
    {
        if (acc.SystemRole is not null) return "This account is used by automatic postings and cannot be deactivated.";
        if (await Db.Accounts.AnyAsync(x => x.ParentId == acc.Id && x.IsActive, ct)) return "Deactivate or move its child accounts first.";
        var net = await Db.JournalLines.Where(l => l.AccountId == acc.Id).SumAsync(l => (decimal?)(l.Debit - l.Credit), ct) ?? 0m;
        return net != 0 ? "The account still has a balance." : null;
    }
}

[ApiController]
[Authorize(Policy = Policies.ViewAccounting)]
[Route("api/v1/journalentries")]
public class JournalEntriesController(IAccountingService svc, IValidator<JournalEntryInput> validator, IAuthorizationService auth) : ControllerBase
{
    private async Task<bool> CanWriteAsync() => (await auth.AuthorizeAsync(User, Policies.ManageAccounting)).Succeeded;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] JournalStatus? status, [FromQuery] string? sourceType, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.ListEntriesAsync(new JournalFilter(status, sourceType, from, to, q, page, pageSize), ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetEntryAsync(id, ct)));

    private async Task<IActionResult?> InvalidAsync(JournalEntryInput input, CancellationToken ct)
    {
        if (input is null) return BadRequest(ApiResponse.Fail("Request body is required."));
        var result = await validator.ValidateAsync(input, ct);
        if (result.IsValid) return null;
        var errors = result.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return BadRequest(ApiResponse.Fail("Validation failed.", errors));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] JournalEntryInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(input, ct) is { } bad) return bad;
        var created = await svc.CreateEntryAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse.Ok(created));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] JournalEntryInput input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        if (await InvalidAsync(input, ct) is { } bad) return bad;
        return Ok(ApiResponse.Ok(await svc.UpdateEntryAsync(id, input, ct)));
    }

    [HttpPost("{id:int}/post")]
    public async Task<IActionResult> Post(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.PostEntryAsync(id, ct)));

    /// <summary>Posts the mirror entry; returns it.</summary>
    [HttpPost("{id:int}/reverse")]
    public async Task<IActionResult> Reverse(int id, CancellationToken ct) =>
        !await CanWriteAsync() ? Forbid() : Ok(ApiResponse.Ok(await svc.ReverseEntryAsync(id, ct)));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        await svc.DeleteDraftAsync(id, ct);
        return Ok(ApiResponse.Ok(new { }));
    }
}

[ApiController]
[Authorize(Policy = Policies.ViewAccounting)]
[Route("api/v1/accounting")]
public class AccountingReportsController(IAccountingService svc, IRevaluationService reval, IAuthorizationService auth) : ControllerBase
{
    [HttpGet("revaluation/preview")]
    public async Task<IActionResult> RevaluationPreview([FromQuery] DateOnly asOf, CancellationToken ct) =>
        Ok(ApiResponse.Ok(await reval.PreviewAsync(asOf, ct)));

    [HttpGet("revaluation/runs")]
    public async Task<IActionResult> RevaluationRuns(CancellationToken ct) => Ok(ApiResponse.Ok(await reval.ListAsync(ct)));

    [HttpPost("revaluation")]
    public async Task<IActionResult> Revalue([FromBody] RevaluationInput input, CancellationToken ct) =>
        !(await auth.AuthorizeAsync(User, Policies.ManageAccounting)).Succeeded ? Forbid() : Ok(ApiResponse.Ok(await reval.RunAsync(input.AsOf, ct)));

    [HttpGet("trial-balance")]
    public async Task<IActionResult> TrialBalance([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(ApiResponse.Ok(await svc.GetTrialBalanceAsync(from, to, ct)));

    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger([FromQuery] int accountId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] PartyType partyType = PartyType.None, [FromQuery] int? partyId = null, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await svc.GetLedgerAsync(accountId, from, to, partyType, partyId, ct)));

    [HttpGet("income-statement")]
    public async Task<IActionResult> IncomeStatement([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(ApiResponse.Ok(await svc.GetIncomeStatementAsync(from, to, ct)));

    [HttpGet("balance-sheet")]
    public async Task<IActionResult> BalanceSheet([FromQuery] DateOnly? asOf, CancellationToken ct) =>
        Ok(ApiResponse.Ok(await svc.GetBalanceSheetAsync(asOf, ct)));

    [HttpGet("reconciliation")]
    public async Task<IActionResult> Reconciliation(CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetReconciliationAsync(ct)));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct) => Ok(ApiResponse.Ok(await svc.GetSummaryAsync(ct)));

    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken ct) => Ok(ApiResponse.Ok(new LockDateInput(await svc.GetLockDateAsync(ct))));

    [HttpPut("settings")]
    public async Task<IActionResult> SetSettings([FromBody] LockDateInput input, CancellationToken ct)
    {
        if (!(await auth.AuthorizeAsync(User, Policies.ManageAccounting)).Succeeded) return Forbid();
        return Ok(ApiResponse.Ok(new LockDateInput(await svc.SetLockDateAsync(input.LockDate, ct))));
    }
}
