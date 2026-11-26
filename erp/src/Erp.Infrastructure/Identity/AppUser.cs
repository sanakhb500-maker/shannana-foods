using Microsoft.AspNetCore.Identity;

namespace Erp.Infrastructure.Identity;

public class AppUser : IdentityUser<int>
{
    public string FullName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AppRole : IdentityRole<int>
{
    public AppRole() { }
    public AppRole(string name) : base(name) { }
}
