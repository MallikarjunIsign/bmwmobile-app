namespace IdentityService.Infrastructure;

/// <summary>An expected error with a user-friendly message, an HTTP status and a code the app can check.</summary>
public class AppException(int statusCode, string message, string? code = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? Code { get; } = code;

    public static AppException BadRequest(string message, string? code = null) => new(StatusCodes.Status400BadRequest, message, code);
    public static AppException Unauthorized(string message, string? code = null) => new(StatusCodes.Status401Unauthorized, message, code);
    public static AppException NotFound(string message, string? code = null) => new(StatusCodes.Status404NotFound, message, code);
    public static AppException Conflict(string message, string? code = null) => new(StatusCodes.Status409Conflict, message, code);
    public static AppException TooMany(string message, string? code = null) => new(StatusCodes.Status429TooManyRequests, message, code);
}
