namespace IcyPlay.Application.Storage;

/// <summary>Deletes a stored image from Cloudinary, for photos kept only for a while.</summary>
public interface ICloudinaryAssetRemover
{
    /// <summary>
    /// Deletes the image. True when it is gone — deleted now, or not there to
    /// begin with — so the caller can forget it; false when Cloudinary could not
    /// be reached or refused, to try again later.
    /// </summary>
    Task<bool> DestroyAsync(string publicId, CancellationToken cancellationToken);
}
