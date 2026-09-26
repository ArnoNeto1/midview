using System.IO;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Master;

// The teacher's power timetable (see PowerSchedule), next to the classroom
// list. It's what the schedule window shows, what the pollers hand to each
// machine's Agent, and what the wake task on this PC follows.
public static class PowerScheduleFile
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Midview", "power-schedule.json");

    // Missing or unreadable means no schedule; an unreadable file is moved
    // aside to "<path>.corrupt" rather than lost.
    public static PowerSchedule? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var schedule = JsonSerializer.Deserialize<PowerSchedule>(File.ReadAllText(path));
            return schedule is not null && schedule.IsValid() ? schedule : null;
        }
        catch (JsonException)
        {
            File.Move(path, path + ".corrupt", overwrite: true);
            return null;
        }
    }

    public static void Save(string path, PowerSchedule schedule)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(schedule));
        File.Move(tempPath, path, overwrite: true);
    }
}
