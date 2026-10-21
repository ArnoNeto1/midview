using System.Buffers.Binary;
using System.Text;

namespace Midview.Protocol;

// ClassFolderWire: writing and reading a payload's parts.
public static partial class ClassFolderWire
{
    private sealed class Writer
    {
        private readonly MemoryStream _stream = new();

        public void PutByte(byte value) => _stream.WriteByte(value);

        public void PutGuid(Guid value) => _stream.Write(value.ToByteArray());

        public void PutUInt32(uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            _stream.Write(buffer);
        }

        public void PutInt64(long value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(buffer, value);
            _stream.Write(buffer);
        }

        public void PutText(string text, int maxBytes)
        {
            var bytes = StrictUtf8.GetBytes(text);
            if (bytes.Length > maxBytes)
            {
                throw new ArgumentException($"A string is at most {maxBytes} bytes of UTF-8, not {bytes.Length}.");
            }

            Span<byte> length = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
            _stream.Write(length);
            _stream.Write(bytes);
        }

        public void PutPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A path can't be empty.");
            }

            PutText(path, FileTransfer.MaxNameBytes);
        }

        public byte[] ToArray() => _stream.ToArray();
    }

    // Reads a payload from the front. Each Try* is false when what is left is
    // too short or not valid; callers stop at the first failure.
    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _position;

        public Reader(byte[] payload)
        {
            _bytes = payload;
            _position = 0;
        }

        public readonly int Remaining => _bytes.Length - _position;

        public readonly bool AtEnd => Remaining == 0;

        public bool TryByte(out byte value)
        {
            value = 0;
            if (Remaining < 1)
            {
                return false;
            }

            value = _bytes[_position++];
            return true;
        }

        public bool TryVersion() => TryByte(out var version) && version == FormatVersion;

        public bool TryGuid(out Guid value)
        {
            value = Guid.Empty;
            if (Remaining < 16)
            {
                return false;
            }

            value = new Guid(_bytes.Slice(_position, 16));
            _position += 16;
            return true;
        }

        public bool TryUInt32(out uint value)
        {
            value = 0;
            if (Remaining < 4)
            {
                return false;
            }

            value = BinaryPrimitives.ReadUInt32BigEndian(_bytes[_position..]);
            _position += 4;
            return true;
        }

        public bool TryInt64(out long value)
        {
            value = 0;
            if (Remaining < 8)
            {
                return false;
            }

            value = BinaryPrimitives.ReadInt64BigEndian(_bytes[_position..]);
            _position += 8;
            return true;
        }

        public bool TryText(int maxBytes, out string value)
        {
            value = "";
            if (Remaining < 2)
            {
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(_bytes[_position..]);
            if (length > maxBytes || Remaining - 2 < length)
            {
                return false;
            }

            try
            {
                value = StrictUtf8.GetString(_bytes.Slice(_position + 2, length));
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            _position += 2 + length;
            return true;
        }

        public bool TryPath(out string path) => TryText(FileTransfer.MaxNameBytes, out path) && !string.IsNullOrWhiteSpace(path);
    }
}
