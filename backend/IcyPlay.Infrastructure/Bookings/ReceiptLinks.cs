namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// What a GCash receipt link has to look like, for a court booking and an open
/// play registration alike. One rule in one place, so the two cannot come to
/// accept different things.
/// </summary>
internal static class ReceiptLinks
{
    /// <summary>
    /// A picture: an image upload with a picture's extension. A receipt is a
    /// screenshot or a photo of the GCash confirmation, and the desk has to be
    /// able to look at it without downloading anything.
    /// </summary>
    public static bool IsImage(string url)
    {
        if (!url.Contains("/image/upload/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = url.Split('?')[0];
        var extension = Path.GetExtension(path);

        return extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic";
    }
}
