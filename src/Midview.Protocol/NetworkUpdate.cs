using System.Buffers.Binary;

namespace Midview.Protocol;

// Updating the student program over the network
// (docs/superpowers/specs/2026-09-26-network-update-design.md). The Master
// opens with UpdateOffer; the Agent answers UpdateAccepted or a
// CommandAck/CommandResult; after an accept come FileChunk messages up to
// exactly the offered zip size, answered with one CommandAck/CommandResult.
// Extracting and applying then happen without the connection, and
// GetUpdateStatus reports how they went (UpdateStatusReport).
//
// The message types, the payloads and the Agent's --apply-update and
// --recover-update flags are a contract between versions: the build doing
// the applying is always the one being replaced.
public static class NetworkUpdate
{
    // What a package's manifest must declare to be pushed over the network.
    public const int ProtocolVersion = 1;

    public const string ManifestFileName = "midview-build.json";

    // A build that lacks either can't run the lab.
    public static readonly IReadOnlyList<string> RequiredFiles = new[] { "Midview.Agent.exe", "Midview.Session.exe" };

    // The student program is ~70 MB zipped, ~160 MB unzipped, ~500 files.
    public const long MaxZipSize = 512L * 1024 * 1024;
    public const long MaxUncompressedSize = 1L * 1024 * 1024 * 1024;
    public const int MaxEntries = 5000;
    public const int MaxPathDepth = 16;

    public const int AttemptIdLength = 16;
    public const int HashLength = 32;
    public const int OfferLength = AttemptIdLength + HashLength + 8 + HashLength + 8;

    // The applier's limits (Agent), shared so the Master's deadline follows
    // them rather than guessing.
    public static readonly TimeSpan VerifyAllowance = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan ServiceStopTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ServiceStartTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ConfirmationWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RollbackCheck = TimeSpan.FromSeconds(30);

    // From the end of the upload until the attempt has certainly ended:
    // verifying, then applying and confirming, then a full rollback.
    public static readonly TimeSpan MasterDeadline =
        VerifyAllowance + ServiceStopTimeout + ServiceStartTimeout + ConfirmationWindow
        + ServiceStopTimeout + ServiceStartTimeout + RollbackCheck + TimeSpan.FromMinutes(1);

    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    // UpdateOffer payload (OfferLength bytes, big-endian): attempt id, the
    // package fingerprint, the zip's size and SHA-256, the uncompressed total.
    public static byte[] EncodeOffer(UpdateOffer offer)
    {
        if (offer.AttemptId.Length != AttemptIdLength || offer.Fingerprint.Length != HashLength
            || offer.ZipSha256.Length != HashLength)
        {
            throw new ArgumentException("invalid update offer", nameof(offer));
        }

        var payload = new byte[OfferLength];
        var span = payload.AsSpan();
        offer.AttemptId.CopyTo(span);
        offer.Fingerprint.CopyTo(span[AttemptIdLength..]);
        BinaryPrimitives.WriteInt64BigEndian(span[(AttemptIdLength + HashLength)..], offer.ZipSize);
        offer.ZipSha256.CopyTo(span[(AttemptIdLength + HashLength + 8)..]);
        BinaryPrimitives.WriteInt64BigEndian(span[(AttemptIdLength + 2 * HashLength + 8)..], offer.UncompressedSize);
        return payload;
    }

    public static bool TryDecodeOffer(byte[] payload, out UpdateOffer offer)
    {
        offer = new UpdateOffer(Array.Empty<byte>(), Array.Empty<byte>(), 0, Array.Empty<byte>(), 0);
        if (payload.Length != OfferLength)
        {
            return false;
        }

        var span = payload.AsSpan();
        var zipSize = BinaryPrimitives.ReadInt64BigEndian(span[(AttemptIdLength + HashLength)..]);
        var uncompressed = BinaryPrimitives.ReadInt64BigEndian(span[(AttemptIdLength + 2 * HashLength + 8)..]);
        if (zipSize <= 0 || uncompressed <= 0)
        {
            return false;
        }

        offer = new UpdateOffer(
            span[..AttemptIdLength].ToArray(),
            span.Slice(AttemptIdLength, HashLength).ToArray(),
            zipSize,
            span.Slice(AttemptIdLength + HashLength + 8, HashLength).ToArray(),
            uncompressed);
        return true;
    }

    // A path inside a build: relative, '/'- or '\'-separated, and every
    // segment already a safe Windows name (FileNames.Safe would leave it
    // unchanged) -- so no "..", drive, ':' (alternate streams), reserved
    // device name or trailing dot. Rejected rather than repaired: a build
    // whose files had to be renamed wouldn't run. Normalized with '/'.
    public static bool IsValidRelativePath(string path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(path) || path[0] is '/' or '\\')
        {
            return false;
        }

        var segments = path.Split('/', '\\');
        if (segments.Length > MaxPathDepth)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." || FileNames.Safe(segment) != segment)
            {
                return false;
            }
        }

        normalized = string.Join('/', segments);
        return true;
    }
}

public sealed record UpdateOffer(byte[] AttemptId, byte[] Fingerprint, long ZipSize, byte[] ZipSha256, long UncompressedSize);

// The states of an update attempt, in the order they happen. The last three
// are final: nothing more happens to that attempt.
public static class UpdateStates
{
    public const string Receiving = "receiving";
    public const string Verifying = "verifying";
    public const string Applying = "applying";
    public const string Provisional = "provisional";
    public const string Committed = "committed";
    public const string RolledBack = "rolledBack";
    public const string Failed = "failed";

    public static bool IsFinal(string? state) => state is Committed or RolledBack or Failed;
}
