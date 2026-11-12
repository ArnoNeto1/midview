namespace Midview.Protocol;

// Updating a phone's Agent reuses NetworkUpdate's messages (UpdateOffer,
// UpdateAccepted, FileChunk, GetUpdateStatus, UpdateStatus). For a phone,
// UpdateOffer.Fingerprint = ZipSha256 = the SHA-256 of the APK, and
// ZipSize = UncompressedSize = the APK's size in bytes.
public static class PhoneUpdate
{
    public const long MaxApkSize = 64L * 1024 * 1024;
}
