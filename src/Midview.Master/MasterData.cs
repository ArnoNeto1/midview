using System.IO;

namespace Midview.Master;

// The folder the teacher's own files live in: the classroom list, the
// teacher's key, the schedule, the saved menus and preferences.
public static class MasterData
{
    // Only a tool that runs Master against made-up machines points it
    // elsewhere, so the teacher's real classroom is never touched.
    public static string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Midview");
}
