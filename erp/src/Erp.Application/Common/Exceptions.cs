namespace Erp.Application.Common;

/// <summary>A business rule was violated (maps to HTTP 400 with the message).</summary>
public class BusinessRuleException(string message) : Exception(message);

/// <summary>Another user changed the same stock at the same moment (maps to HTTP 409; the caller may retry).</summary>
public class ConcurrencyConflictException() : Exception("The stock was changed by someone else at the same time. Please retry.");

/// <summary>A referenced record does not exist (maps to HTTP 404).</summary>
public class NotFoundException(string message) : Exception(message);
