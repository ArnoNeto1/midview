namespace Midview.Protocol;

// Rules for saving a file named by the other side, which is never trusted
// to send a safe name: the Session's inbox (files from the teacher) and the
// Master's collected files (from the students, see FileCollection).
public static class FileNames
{
    public const int MaxNameLength = 120;

    // Windows' own rules, spelled out rather than taken from
    // Path.GetInvalidFileNameChars() so they're the same wherever this runs.
    private static readonly char[] InvalidNameChars = "<>:\"/\\|?*".ToCharArray();

    private static readonly HashSet<string> ReservedNames = new(
        new[] { "CON", "PRN", "AUX", "NUL" }
            .Concat(Enumerable.Range(1, 9).Select(n => $"COM{n}"))
            .Concat(Enumerable.Range(1, 9).Select(n => $"LPT{n}")),
        StringComparer.OrdinalIgnoreCase);

    // Only ever a plain file name: the last path segment of whatever was
    // sent ("..\..\x.exe" and "C:\Windows\x.exe" both become "x.exe"), with
    // characters Windows forbids replaced -- ':' included, which also rules
    // out drive letters and NTFS alternate streams.
    public static string Safe(string requested)
    {
        var name = requested.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Select(c => c < 32 || InvalidNameChars.Contains(c) ? '_' : c).ToArray());
        name = name.Trim().TrimEnd('.', ' '); // Windows silently drops trailing dots and spaces

        if (name.Length == 0)
        {
            name = "arquivo";
        }

        if (ReservedNames.Contains(name.Split('.')[0]))
        {
            name = "_" + name; // CON, NUL, COM1... are devices, not files, even with an extension
        }

        if (name.Length > MaxNameLength)
        {
            var extension = Path.GetExtension(name);
            if (extension.Length > 20)
            {
                extension = string.Empty;
            }
            name = name[..(MaxNameLength - extension.Length)].TrimEnd('.', ' ') + extension;
        }

        return name;
    }

    // Moves a completely written temp file to safeName in directory, never
    // overwriting: "notas.pdf" becomes "notas (2).pdf", "notas (3).pdf"...
    // Returns the name it was saved under.
    public static string MoveToUniqueName(string tempPath, string directory, string safeName)
    {
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        for (var copy = 1; ; copy++)
        {
            var candidate = copy == 1 ? safeName : $"{stem} ({copy}){extension}";
            var target = Path.Combine(directory, candidate);
            if (File.Exists(target) || Directory.Exists(target))
            {
                continue;
            }

            try
            {
                File.Move(tempPath, target);
                return candidate;
            }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target))
            {
                // Appeared between the check and the move; try the next name.
            }
        }
    }
}
