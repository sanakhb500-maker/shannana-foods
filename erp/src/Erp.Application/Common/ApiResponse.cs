namespace Erp.Application.Common;

/// <summary>Uniform envelope: { success, data?, message?, errors? } (same shape as the website API).</summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public IDictionary<string, string[]>? Errors { get; init; }
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<object> Fail(string message, IDictionary<string, string[]>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors };
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
