using System.Buffers.Binary;
using System.Text;

namespace Midview.Protocol;

// The class folder's lesson ("Pasta da turma", docs/superpowers/specs/
// 2026-10-20-class-folder-design.md): which turma the teacher is teaching and
// since when, or that the lesson has ended. LessonId is new for every lesson,
// so a file set aside for one lesson is never taken for another's. SavedAt
// (Unix seconds on the teacher's PC) decides between two versions, as
// LabConfig does: the newest wins. StartedAt (Unix seconds too) lets each
// Agent end a lesson that began on an earlier day (StartedBefore).
//
// Also the payload of MessageType.SetClassLesson and ClassLessonStatus
// (big-endian): format version (1 byte), SavedAt (int64), StartedAt (int64),
// Ended (1 byte, 0 or 1), LessonId (16 bytes, Guid.ToByteArray), Turma
// (uint16 byte length, then UTF-8).
public sealed record ClassLesson(long SavedAt, Guid LessonId, string Turma, long StartedAt, bool Ended)
{
    public const byte FormatVersion = 1;

    // In characters: what fits in a folder name next to "Pasta da turma – ".
    public const int MaxTurmaLength = 60;

    // Everything before the Turma bytes.
    private const int FixedLength = 36;

    private static readonly long MaxStartedAt = DateTimeOffset.MaxValue.ToUnixTimeSeconds();
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    // No lesson: the default, and what ending one leaves.
    public static ClassLesson None(long savedAt) => new(savedAt, Guid.Empty, "", 0, true);

    public bool IsOn => !Ended && LessonId != Guid.Empty;

    // The folder on the student's Desktop. U+2013 (en dash), not a hyphen.
    public string FolderName => "Pasta da turma – " + Turma;

    // Did the lesson start on a calendar day (local time) before the day of
    // localToday? Then it is left over from an earlier class.
    public bool StartedBefore(DateTime localToday) =>
        DateTimeOffset.FromUnixTimeSeconds(StartedAt).LocalDateTime.Date < localToday.Date;

    // What Encode writes and TryDecode, and ClassLessonFile.Read, accept.
    internal bool IsWellFormed =>
        Turma is not null && Turma.Length <= MaxTurmaLength && StartedAt >= 0 && StartedAt <= MaxStartedAt;

    public byte[] Encode()
    {
        if (!IsWellFormed)
        {
            throw new InvalidOperationException(
                $"The class lesson can't be sent: the turma is at most {MaxTurmaLength} characters and the start a date.");
        }

        var turma = StrictUtf8.GetBytes(Turma);
        var buffer = new byte[FixedLength + turma.Length];
        buffer[0] = FormatVersion;
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(1), SavedAt);
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(9), StartedAt);
        buffer[17] = (byte)(Ended ? 1 : 0);
        LessonId.ToByteArray().CopyTo(buffer, 18);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(34), (ushort)turma.Length);
        turma.CopyTo(buffer, FixedLength);
        return buffer;
    }

    public static bool TryDecode(byte[] payload, out ClassLesson lesson)
    {
        lesson = None(0);
        if (payload.Length < FixedLength || payload[0] != FormatVersion || payload[17] > 1)
        {
            return false;
        }

        var turmaLength = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(34));
        if (payload.Length != FixedLength + turmaLength)
        {
            return false;
        }

        string turma;
        try
        {
            turma = StrictUtf8.GetString(payload, FixedLength, turmaLength);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        var decoded = new ClassLesson(
            BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1)),
            new Guid(payload.AsSpan(18, 16)),
            turma,
            BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(9)),
            payload[17] == 1);
        if (!decoded.IsWellFormed)
        {
            return false;
        }

        lesson = decoded;
        return true;
    }
}
