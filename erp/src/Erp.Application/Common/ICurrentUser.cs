namespace Erp.Application.Common;

/// <summary>Who is performing the current operation (used for audit columns).</summary>
public interface ICurrentUser
{
    string? UserName { get; }
}
