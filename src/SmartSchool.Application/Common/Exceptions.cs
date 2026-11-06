namespace SmartSchool.Application.Common;

/// <summary>Base for errors that the API turns into a clean HTTP response.</summary>
public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class NotFoundException(string entity, object key)
    : AppException($"{entity} '{key}' was not found.", 404);

public sealed class ForbiddenException(string message = "You do not have access to this resource.")
    : AppException(message, 403);

public sealed class UnauthorizedException(string message = "Invalid email or password.")
    : AppException(message, 401);

public sealed class ConflictException(string message) : AppException(message, 409);

public sealed class ValidationException : AppException
{
    public ValidationException(string field, string message) : base(message, 400)
        => Errors = new() { [field] = [message] };

    public Dictionary<string, string[]> Errors { get; }
}
