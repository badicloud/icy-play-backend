namespace IcyPlay.Api.Common;

public sealed record ApiEnvelope<T>(
    T? Data,
    object? Meta = null);
