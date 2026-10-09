using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Midview.Agent;

// Tells one version of an image file from another, so that the facts kept for
// a path (its version information and owner) are reused only while the file is
// really the same one. Keyed per path, with its length, write time and a
// fingerprint of its bytes.
//
// The length and write time alone don't tell them apart: a file's owner can
// pad a replacement to the same length and set its write time back (both are
// theirs to set), and the stale facts would stand -- long enough for a blocked
// program, renamed and swapped in, to be judged as the file that was there
// before. The fingerprint -- a hash of the length and of the first and last
// few kilobytes -- a different program can't match without being built to,
// and reading those few kilobytes is far cheaper than reading the version
// information and owner afresh on every scan, which is what the key spares.
internal readonly record struct FileChangeKey(string Path, long Length, DateTime Written, long Fingerprint)
{
    private const int SampleBytes = 8 * 1024;

    // Null when the file's bytes can't be read: the caller then doesn't cache
    // it, so a file that can't be fingerprinted is read afresh every time
    // rather than trusted from an entry that might be the wrong file.
    public static FileChangeKey? From(string path, FileInfo file) =>
        FingerprintOf(path, file.Length) is { } fingerprint
            ? new FileChangeKey(path, file.Length, file.LastWriteTimeUtc, fingerprint)
            : null;

    private static long? FingerprintOf(string path, long length)
    {
        try
        {
            // ReadWrite | Delete so reading the image doesn't fight a running
            // program or a file being replaced.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> lengthBytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, length);
            hash.AppendData(lengthBytes);

            var buffer = new byte[SampleBytes];
            var headRead = ReadAt(stream, 0, buffer);
            hash.AppendData(buffer, 0, headRead);

            // The tail, where it doesn't overlap the head already read.
            var tailStart = Math.Max(headRead, length - SampleBytes);
            if (tailStart < length)
            {
                var tailRead = ReadAt(stream, tailStart, buffer);
                hash.AppendData(buffer, 0, tailRead);
            }

            Span<byte> digest = stackalloc byte[32];
            hash.TryGetHashAndReset(digest, out _);
            return BinaryPrimitives.ReadInt64LittleEndian(digest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static int ReadAt(FileStream stream, long offset, byte[] buffer)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
    }
}
