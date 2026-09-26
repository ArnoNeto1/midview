using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Midview.Protocol;

// The lab's timetable for turning on and off, as the teacher saves it in the
// Master. Days: bit n set = DayOfWeek n (bit 0 is Sunday). Times are minutes
// after midnight, in each computer's own local time; null = not scheduled.
// SavedAt (Unix seconds, on the teacher's PC) decides between two versions:
// the newest wins, so two teachers' Masters don't keep overwriting each
// other's.
//
// Also the payload of MessageType.SetPowerSchedule, an Agent command
// (PayloadLength bytes, big-endian): SavedAt (int64), Days, WakeMinute and
// ShutdownMinute (uint16 each, 0xFFFF for none). The Agent only acts on the
// shutdown: a machine that's off can't wake itself, so waking is the
// teacher's PC's job.
public sealed record PowerSchedule(long SavedAt, byte Days, int? WakeMinute, int? ShutdownMinute)
{
    public const byte Weekdays = 0b0111110;
    public const byte EveryDay = 0b1111111;
    public const int PayloadLength = 13;

    // How long before a scheduled shutdown the student is warned.
    public const int ShutdownWarningSeconds = 5 * 60;

    private const ushort NotScheduled = 0xFFFF;
    private const int MinutesPerDay = 24 * 60;

    private static readonly Regex TimePattern = new(@"^(\d{1,2})(?:[:h](\d{2})?)?$", RegexOptions.CultureInvariant);

    // Monday first, the way a school week reads.
    private static readonly (DayOfWeek Day, string Name)[] WeekOrder =
    {
        (DayOfWeek.Monday, "seg"), (DayOfWeek.Tuesday, "ter"), (DayOfWeek.Wednesday, "qua"),
        (DayOfWeek.Thursday, "qui"), (DayOfWeek.Friday, "sex"), (DayOfWeek.Saturday, "sáb"),
        (DayOfWeek.Sunday, "dom"),
    };

    public bool IsScheduledOn(DayOfWeek day) => (Days & (1 << (int)day)) != 0;

    public bool IsValid() =>
        (Days & ~EveryDay) == 0 && IsValidMinute(WakeMinute) && IsValidMinute(ShutdownMinute);

    public byte[] Encode()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException("invalid power schedule");
        }

        var payload = new byte[PayloadLength];
        BinaryPrimitives.WriteInt64BigEndian(payload, SavedAt);
        payload[8] = Days;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(9), (ushort)(WakeMinute ?? NotScheduled));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(11), (ushort)(ShutdownMinute ?? NotScheduled));
        return payload;
    }

    public static bool TryDecode(byte[] payload, out PowerSchedule schedule)
    {
        schedule = new PowerSchedule(0, 0, null, null);
        if (payload.Length != PayloadLength)
        {
            return false;
        }

        var decoded = new PowerSchedule(
            BinaryPrimitives.ReadInt64BigEndian(payload),
            payload[8],
            ToMinute(BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(9))),
            ToMinute(BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(11))));
        if (!decoded.IsValid())
        {
            return false;
        }

        schedule = decoded;
        return true;
    }

    public static string FormatTime(int minute) =>
        string.Create(CultureInfo.InvariantCulture, $"{minute / 60:00}:{minute % 60:00}");

    // "7", "7:00", "07:30", "7h" or "7h30".
    public static bool TryParseTime(string? text, out int minute)
    {
        minute = 0;
        var match = TimePattern.Match(text?.Trim() ?? string.Empty);
        if (!match.Success)
        {
            return false;
        }

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        if (hours > 23 || minutes > 59)
        {
            return false;
        }

        minute = hours * 60 + minutes;
        return true;
    }

    // "seg a sex", "todos os dias", "seg, qua e sex", "sáb e dom".
    public static string DescribeDays(byte days)
    {
        if ((days & EveryDay) == EveryDay)
        {
            return "todos os dias";
        }

        var positions = WeekOrder
            .Select((entry, index) => (entry.Name, Index: index, IsSet: (days & (1 << (int)entry.Day)) != 0))
            .Where(entry => entry.IsSet)
            .ToList();
        if (positions.Count == 0)
        {
            return "nenhum dia";
        }

        var isRun = positions[^1].Index - positions[0].Index == positions.Count - 1;
        if (isRun && positions.Count >= 3)
        {
            return $"{positions[0].Name} a {positions[^1].Name}";
        }

        var names = positions.Select(entry => entry.Name).ToList();
        return names.Count == 1 ? names[0] : $"{string.Join(", ", names.Take(names.Count - 1))} e {names[^1]}";
    }

    private static bool IsValidMinute(int? minute) => minute is null or (>= 0 and < MinutesPerDay);

    private static int? ToMinute(ushort value) => value == NotScheduled ? null : (int)value;
}
