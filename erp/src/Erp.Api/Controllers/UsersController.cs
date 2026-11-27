using System.Security.Claims;
using Erp.Application.Common;
using Erp.Domain.Common;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Controllers;

public sealed record UserDto(int Id, string Email, string FullName, bool IsActive, string[] Roles, int? DriverId = null);
/// <param name="DriverId">Required (and only used) when the Driver role is given: the driver record this login belongs to.</param>
public sealed record CreateUserRequest(string Email, string FullName, string Password, string[] Roles, int? DriverId = null);
public sealed record UpdateUserRequest(string FullName, bool IsActive, string[] Roles, int? DriverId = null);
public sealed record ResetPasswordRequest(string NewPassword);

[ApiController]
[Authorize(Policy = Policies.ManageUsers)]
[Route("api/v1/users")]
public class UsersController(UserManager<AppUser> users, AppDbContext db) : ControllerBase
{
    private int CurrentId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var all = await users.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct);
        var result = new List<UserDto>();
        foreach (var u in all) result.Add(await ToDto(u));
        return Ok(ApiResponse.Ok(result));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        var rolesError = ValidateRoles(req.Roles);
        if (rolesError is not null) return BadRequest(ApiResponse.Fail(rolesError));
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@')) return BadRequest(ApiResponse.Fail("A valid email is required."));
        if (string.IsNullOrWhiteSpace(req.FullName)) return BadRequest(ApiResponse.Fail("Full name is required."));

        var driverError = await ValidateDriverLinkAsync(req.Roles, req.DriverId, null);
        if (driverError is not null) return BadRequest(ApiResponse.Fail(driverError));

        var user = new AppUser { UserName = req.Email.Trim(), Email = req.Email.Trim(), EmailConfirmed = true, FullName = req.FullName.Trim() };
        var res = await users.CreateAsync(user, req.Password ?? "");
        if (!res.Succeeded) return BadRequest(IdentityFail(res));
        await users.AddToRolesAsync(user, req.Roles);
        await SyncDriverLinkAsync(user.Id, req.Roles, req.DriverId);
        return Created($"api/v1/users/{user.Id}", ApiResponse.Ok(await ToDto(user)));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest req)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return NotFound(ApiResponse.Fail("Not found."));
        var rolesError = ValidateRoles(req.Roles);
        if (rolesError is not null) return BadRequest(ApiResponse.Fail(rolesError));
        if (string.IsNullOrWhiteSpace(req.FullName)) return BadRequest(ApiResponse.Fail("Full name is required."));

        var driverError = await ValidateDriverLinkAsync(req.Roles, req.DriverId, id);
        if (driverError is not null) return BadRequest(ApiResponse.Fail(driverError));

        // Guard against locking everyone out.
        if (id == CurrentId && (!req.IsActive || !req.Roles.Contains(Roles.Admin)))
            return BadRequest(ApiResponse.Fail("You cannot deactivate yourself or remove your own Admin role."));

        user.FullName = req.FullName.Trim();
        user.IsActive = req.IsActive;
        var res = await users.UpdateAsync(user);
        if (!res.Succeeded) return BadRequest(IdentityFail(res));

        var current = await users.GetRolesAsync(user);
        await users.RemoveFromRolesAsync(user, current.Except(req.Roles));
        await users.AddToRolesAsync(user, req.Roles.Except(current));
        await SyncDriverLinkAsync(user.Id, req.Roles, req.DriverId);
        return Ok(ApiResponse.Ok(await ToDto(user)));
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordRequest req)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return NotFound(ApiResponse.Fail("Not found."));
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var res = await users.ResetPasswordAsync(user, token, req.NewPassword ?? "");
        return res.Succeeded ? Ok(ApiResponse.Ok(new { })) : BadRequest(IdentityFail(res));
    }

    private async Task<UserDto> ToDto(AppUser u) =>
        new(u.Id, u.Email ?? "", u.FullName, u.IsActive, (await users.GetRolesAsync(u)).ToArray(),
            await db.Drivers.AsNoTracking().Where(d => d.UserId == u.Id).Select(d => (int?)d.Id).FirstOrDefaultAsync());

    /// <summary>The Driver role needs an existing, active driver record that no other account uses.</summary>
    private async Task<string?> ValidateDriverLinkAsync(string[] roles, int? driverId, int? userId)
    {
        if (!roles.Contains(Roles.Driver)) return null;
        if (driverId is null) return "The Driver role needs a linked driver record.";
        var d = await db.Drivers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == driverId);
        if (d is null || !d.IsActive) return "The driver does not exist.";
        return d.UserId is { } other && other != userId ? "That driver is already linked to another account." : null;
    }

    /// <summary>Makes Driver.UserId match the roles: set for a Driver account, cleared otherwise.</summary>
    private async Task SyncDriverLinkAsync(int userId, string[] roles, int? driverId)
    {
        foreach (var old in await db.Drivers.Where(d => d.UserId == userId && (!roles.Contains(Roles.Driver) || d.Id != driverId)).ToListAsync()) old.UserId = null;
        await db.SaveChangesAsync(); // free the previous record first (unique index)
        if (roles.Contains(Roles.Driver) && driverId is { } id) (await db.Drivers.FirstAsync(d => d.Id == id)).UserId = userId;
        await db.SaveChangesAsync();
    }

    private static string? ValidateRoles(string[]? roles) =>
        roles is null || roles.Length == 0 ? "At least one role is required."
        : roles.Any(r => !Roles.All.Contains(r)) ? "Unknown role." : null;

    private static ApiResponse<object> IdentityFail(IdentityResult r) =>
        ApiResponse.Fail("Request failed.", new Dictionary<string, string[]> { ["identity"] = r.Errors.Select(e => e.Description).ToArray() });
}

[ApiController]
[Authorize]
[Route("api/v1/roles")]
public class RolesController : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(ApiResponse.Ok(Roles.All));
}
