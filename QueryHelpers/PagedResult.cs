namespace identityServer.QueryHelpers;

public record PagedResult<T>(IReadOnlyList<T> Data, int Total);
