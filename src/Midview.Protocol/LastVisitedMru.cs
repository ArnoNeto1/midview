using System.Buffers.Binary;
using System.Text;

namespace Midview.Protocol;

// The format of the common dialog's "last folder used" registry key
// (ComDlg32\LastVisitedPidlMRU): MRUListEx holds the order, newest first, and
// each numbered value is "<program name, UTF-16LE> 00 00 <folder's ID list>".
// The ID list is never decoded here, so the Agent can compare entries byte for
// byte; only the Session, through the shell, reads one back (IdListOf hands it
// the bytes).
public static class LastVisitedMru
{
    public const string OrderName = "MRUListEx";

    // Windows keeps the names of a few dozen entries, so a bigger number means
    // the value was not written by Windows.
    private const int MaxNumber = 9999;

    // A program name is a file name, so one that does not end within this many
    // characters is not one.
    private const int MaxProgramLength = 260;

    private const int EndMarker = -1;

    // Null when the value is not a trustworthy MRUListEx, so the caller leaves
    // the dialog's history as it is. No value at all is an empty list.
    public static List<int>? ParseOrder(byte[]? data)
    {
        var order = new List<int>();
        if (data is null)
            return order;
        if (data.Length % 4 != 0 || data.Length == 0)
            return null;

        var seen = new HashSet<int>();
        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var number = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
            if (number == EndMarker)
                return offset == data.Length - 4 ? order : null;
            if (number < 0 || number > MaxNumber || !seen.Add(number))
                return null;
            order.Add(number);
        }
        return null;
    }

    public static byte[] FormatOrder(IEnumerable<int> order)
    {
        var numbers = order.ToList();
        var data = new byte[(numbers.Count + 1) * 4];
        for (var i = 0; i < numbers.Count; i++)
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 4), numbers[i]);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(numbers.Count * 4), EndMarker);
        return data;
    }

    // The program an entry belongs to, or null when no UTF-16 null ends a name
    // in the first MaxProgramLength characters.
    public static string? ProgramOf(byte[] entry)
    {
        var length = NameLength(entry);
        return length < 0 ? null : Encoding.Unicode.GetString(entry, 0, length * 2);
    }

    // The bytes after the program name's null (the folder's ID list, undecoded),
    // or null when ProgramOf finds no name.
    public static byte[]? IdListOf(byte[] entry)
    {
        var length = NameLength(entry);
        return length < 0 ? null : entry.AsSpan((length + 1) * 2).ToArray();
    }

    // In UTF-16 units, up to the first null; -1 when there is none in reach.
    private static int NameLength(byte[] entry)
    {
        var limit = Math.Min(MaxProgramLength, entry.Length / 2);
        for (var i = 0; i < limit; i++)
        {
            if (entry[i * 2] == 0 && entry[i * 2 + 1] == 0)
                return i;
        }
        return -1;
    }

    public static byte[] Entry(string program, byte[] idList)
    {
        var name = Encoding.Unicode.GetBytes(program);
        var entry = new byte[name.Length + 2 + idList.Length];
        name.CopyTo(entry, 0);
        idList.CopyTo(entry, name.Length + 2);
        return entry;
    }
}
