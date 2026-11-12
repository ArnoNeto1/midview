using System;
using System.IO;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Midview.Master;

// Reads who signed an APK: the SHA-256 of the first signer's first certificate,
// taken from the v3 (or else v2) APK Signing Block that sits right before the
// ZIP Central Directory. Never throws: anything unexpected gives null.
public static class ApkSignature
{
    private const uint EocdSignature = 0x06054b50;
    private const int EocdMinSize = 22;
    private const uint V2Id = 0x7109871a;
    private const uint V3Id = 0xf05368c0;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("APK Sig Block 42");

    // Base64 URL without "=", the same format as PhoneProvisioning.SignatureChecksum.
    public static string? SignerChecksum(string apkPath)
    {
        try
        {
            var data = File.ReadAllBytes(apkPath);
            var block = FindSigningBlockPairs(data);
            if (block is null)
            {
                return null;
            }

            var value = FindPair(block.Value, V3Id) ?? FindPair(block.Value, V2Id);
            if (value is null)
            {
                return null;
            }

            var cert = FirstCertificate(value.Value);
            if (cert is null)
            {
                return null;
            }

            return Convert.ToBase64String(SHA256.HashData(cert.Value.Span))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The id/value pairs of the signing block, or null when there is none.
    private static ReadOnlyMemory<byte>? FindSigningBlockPairs(byte[] data)
    {
        var eocd = -1;
        var lowest = Math.Max(0, data.Length - EocdMinSize - 0xFFFF);
        for (var i = data.Length - EocdMinSize; i >= lowest; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i, 4)) == EocdSignature)
            {
                eocd = i;
                break;
            }
        }

        if (eocd < 0)
        {
            return null;
        }

        var cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(eocd + 16, 4));
        if (cdOffset < 24 + 8 || cdOffset > (uint)eocd)
        {
            return null;
        }

        var footer = (int)cdOffset - 24;
        if (!data.AsSpan(footer + 8, 16).SequenceEqual(Magic))
        {
            return null;
        }

        var size = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(footer, 8));
        // size counts everything after its own first 8 bytes: pairs + footer size + magic.
        if (size < 24 || size > (ulong)(cdOffset - 8))
        {
            return null;
        }

        var start = (int)cdOffset - (int)size - 8;
        var first = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(start, 8));
        if (first != size)
        {
            return null;
        }

        return data.AsMemory(start + 8, (int)size - 24);
    }

    private static ReadOnlyMemory<byte>? FindPair(ReadOnlyMemory<byte> pairs, uint id)
    {
        var span = pairs.Span;
        var pos = 0;
        while (pos + 12 <= span.Length)
        {
            var len = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(pos, 8));
            if (len < 4 || len > (ulong)(span.Length - pos - 8))
            {
                return null;
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(pos + 8, 4)) == id)
            {
                return pairs.Slice(pos + 12, (int)len - 4);
            }

            pos += 8 + (int)len;
        }

        return null;
    }

    // value: signers -> first signer -> signed data -> (digests, certificates -> first).
    private static ReadOnlyMemory<byte>? FirstCertificate(ReadOnlyMemory<byte> value)
    {
        var signers = Prefixed(value, 0, out _);
        var signer = signers is null ? null : Prefixed(signers.Value, 0, out _);
        var signedData = signer is null ? null : Prefixed(signer.Value, 0, out _);
        if (signedData is null)
        {
            return null;
        }

        Prefixed(signedData.Value, 0, out var afterDigests);
        if (afterDigests < 0)
        {
            return null;
        }

        var certs = Prefixed(signedData.Value, afterDigests, out _);
        return certs is null ? null : Prefixed(certs.Value, 0, out _);
    }

    // A 4-byte length-prefixed chunk at offset; next is the offset after it (-1 if invalid).
    private static ReadOnlyMemory<byte>? Prefixed(ReadOnlyMemory<byte> data, int offset, out int next)
    {
        next = -1;
        var span = data.Span;
        if (offset < 0 || offset + 4 > span.Length)
        {
            return null;
        }

        var len = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
        if (len > (uint)(span.Length - offset - 4))
        {
            return null;
        }

        next = offset + 4 + (int)len;
        return data.Slice(offset + 4, (int)len);
    }
}
