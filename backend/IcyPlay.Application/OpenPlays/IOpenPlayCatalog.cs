namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// The open plays the public site can offer. Only venues that are live, the
/// same rule the court catalogue uses, and only series that still have a
/// date to come.
/// </summary>
public interface IOpenPlayCatalog
{
    /// <param name="sport">A sport key, or null for every sport.</param>
    /// <param name="facility">One venue's id, or null for every venue.</param>
    Task<IReadOnlyCollection<CatalogOpenPlay>> ListAsync(
        string? sport,
        Guid? facility,
        CancellationToken cancellationToken);
}
