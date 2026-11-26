namespace Erp.Domain.Common;

/// <summary>Base for every business entity: id, soft-delete flag and audit columns.</summary>
public abstract class AuditableEntity
{
    public int Id { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
