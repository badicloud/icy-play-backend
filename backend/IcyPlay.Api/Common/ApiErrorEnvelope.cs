namespace IcyPlay.Api.Common;

public sealed record ApiErrorEnvelope(
    ApiError Error,
    object? Meta = null);
