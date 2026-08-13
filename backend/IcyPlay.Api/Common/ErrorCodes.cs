namespace IcyPlay.Api.Common;

public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthorized = "AUTH_UNAUTHORIZED";
    public const string Forbidden = "AUTH_FORBIDDEN";
    public const string NotFound = "RESOURCE_NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
    public const string UnexpectedError = "UNEXPECTED_ERROR";
}
