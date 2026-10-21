using Midview.Master;
using Xunit;

public class MasterDataTests
{
    [Fact]
    public void TheFolder_IsMidviewInTheTeachersLocalAppData()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Midview"),
            MasterData.Folder);
    }

    // No other test reads these default paths, so moving the folder for a
    // moment can't send another test's files elsewhere.
    [Fact]
    public void EveryFileOfTheTeachers_FollowsTheFolder()
    {
        var original = MasterData.Folder;
        var elsewhere = Path.Combine(Path.GetTempPath(), $"midview-data-{Guid.NewGuid():N}");
        try
        {
            MasterData.Folder = elsewhere;

            var paths = new[]
            {
                KnownMachinesFile.DefaultPath, PowerScheduleFile.DefaultPath, AppBlockFile.DefaultPath,
                MasterIdentity.DefaultCertificatePath, SavedMenu.WebsitesPath, SavedMenu.ProgramsPath,
                WakeTask.LogPath, GridViewFile.DefaultPath, ClassLessonFileMaster.Path, ClassStore.DefaultLessonsPath,
                ClassStore.DefaultIndexRoot,
            };

            Assert.All(paths, path => Assert.Equal(elsewhere, Path.GetDirectoryName(path)));
        }
        finally
        {
            MasterData.Folder = original;
        }
    }
}
