using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// "Apagar os recolhidos neste PC": everything collected so far, under
// Documents\Midview\Recolhidos, goes to the teacher's Recycle Bin, so a
// wrong click can be undone. What can't go stays, named with why.
public static class CollectedFiles
{
    public static (int Recycled, IReadOnlyList<string> Problems) RecycleAll(string collectionsDirectory, IRecycleBin recycleBin)
    {
        var problems = new List<string>();
        if (!Directory.Exists(collectionsDirectory))
        {
            return (0, problems);
        }

        var recycled = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(collectionsDirectory).Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                recycleBin.Recycle(entry);
                recycled++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{Path.GetFileName(entry)} ({ErrorText.Describe(ex)})");
            }
        }
        return (recycled, problems);
    }
}
