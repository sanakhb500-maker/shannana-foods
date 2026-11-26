using Erp.Application.Common;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Controllers;

/// <summary>
/// Generic list / get / create / update / deactivate for a master-data entity.
/// Everyone signed in can read; writes need the policy returned by <see cref="WritePolicy"/>.
/// There is no hard delete: DELETE only sets IsActive = false.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/[controller]")]
public abstract class CrudController<T>(AppDbContext db, IValidator<T> validator, IAuthorizationService auth) : ControllerBase
    where T : AuditableEntity, new()
{
    protected AppDbContext Db => db;

    /// <summary>Policy (see <see cref="Policies"/>) required for create/update/delete.</summary>
    protected abstract string WritePolicy { get; }

    protected virtual IQueryable<T> Search(IQueryable<T> q, string term) => q;
    protected virtual IQueryable<T> Filter(IQueryable<T> q, IQueryCollection query) => q;
    protected virtual IQueryable<T> Order(IQueryable<T> q) => q.OrderByDescending(x => x.Id);

    /// <summary>Business checks before saving. Return an error message to reject with 400, or null to continue.</summary>
    protected virtual Task<string?> BeforeSaveAsync(T entity, T? existing, CancellationToken ct) => Task.FromResult<string?>(null);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] bool activeOnly = false, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var query = Db.Set<T>().AsNoTracking();
        if (activeOnly) query = query.Where(x => x.IsActive);
        query = Filter(query, Request.Query);
        if (!string.IsNullOrWhiteSpace(q)) query = Search(query, q.Trim());

        var total = await query.CountAsync(ct);
        var items = await Order(query).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(ApiResponse.Ok(new PagedResult<T>(items, total, page, pageSize)));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var e = await Db.Set<T>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return e is null ? NotFound(ApiResponse.Fail("Not found.")) : Ok(ApiResponse.Ok(e));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] T input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        input.Id = 0;
        input.IsActive = true;

        var invalid = await ValidateAsync(input, ct);
        if (invalid is not null) return invalid;
        var error = await BeforeSaveAsync(input, default, ct);
        if (error is not null) return BadRequest(ApiResponse.Fail(error));

        Db.Set<T>().Add(input);
        var failed = await SaveAsync(ct);
        return failed ?? CreatedAtAction(nameof(Get), new { id = input.Id }, ApiResponse.Ok(input));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] T input, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        var existing = await Db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (existing is null) return NotFound(ApiResponse.Fail("Not found."));

        input.Id = id;
        var invalid = await ValidateAsync(input, ct);
        if (invalid is not null) return invalid;
        var error = await BeforeSaveAsync(input, existing, ct);
        if (error is not null) return BadRequest(ApiResponse.Fail(error));

        // Copy client-editable values, then restore server-owned audit columns.
        var (createdAt, createdBy) = (existing.CreatedAt, existing.CreatedBy);
        Db.Entry(existing).CurrentValues.SetValues(input);
        existing.Id = id;
        existing.CreatedAt = createdAt;
        existing.CreatedBy = createdBy;

        var failed = await SaveAsync(ct);
        return failed ?? Ok(ApiResponse.Ok(existing));
    }

    /// <summary>Soft delete: marks the record inactive so history and references stay intact.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        if (!await CanWriteAsync()) return Forbid();
        var existing = await Db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (existing is null) return NotFound(ApiResponse.Fail("Not found."));
        var error = await BeforeDeactivateAsync(existing, ct);
        if (error is not null) return BadRequest(ApiResponse.Fail(error));
        existing.IsActive = false;
        return await SaveAsync(ct) ?? Ok(ApiResponse.Ok(existing));
    }

    protected virtual Task<string?> BeforeDeactivateAsync(T existing, CancellationToken ct) => Task.FromResult<string?>(null);

    private async Task<bool> CanWriteAsync() => (await auth.AuthorizeAsync(User, WritePolicy)).Succeeded;

    private async Task<IActionResult?> ValidateAsync(T input, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(input, ct);
        if (result.IsValid) return null;
        var errors = result.Errors.GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return BadRequest(ApiResponse.Fail("Validation failed.", errors));
    }

    /// <summary>Saves; maps unique-index and foreign-key violations to 409 / 400 instead of 500.</summary>
    private async Task<IActionResult?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await Db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict(ApiResponse.Fail("A record with the same unique value (code / barcode / plate) already exists."));
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            return BadRequest(ApiResponse.Fail("A referenced record does not exist or is still in use."));
        }
    }
}
