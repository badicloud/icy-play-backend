namespace IcyPlay.Application.Storage;

/// <summary>
/// Everything the browser needs to upload straight to Cloudinary. The API
/// secret is never part of this: only the signature derived from it.
/// </summary>
public sealed record CloudinaryUploadSignature(
    string CloudName,
    string ApiKey,
    string Folder,
    long Timestamp,
    string Signature);
