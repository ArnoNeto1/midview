using Midview.Master;
using Xunit;

// "Fixar a barra de cima ao abrir a tela do aluno" (Máquinas da sala > Avançado).
public class ViewWindowSettingsFileTests
{
    [Fact]
    public void Missing_or_broken_means_the_toolbar_slides_away()
    {
        var path = Path.Combine(Path.GetTempPath(), "midview-tela-" + Guid.NewGuid().ToString("N") + ".json");
        Assert.False(ViewWindowSettingsFile.Load(path).PinToolbar);
        File.WriteAllText(path, "{ não é json");
        try
        {
            Assert.False(ViewWindowSettingsFile.Load(path).PinToolbar);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_choice_is_kept()
    {
        var path = Path.Combine(Path.GetTempPath(), "midview-tela-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ViewWindowSettingsFile.Save(new ViewWindowSettings(PinToolbar: true), path);
            Assert.True(ViewWindowSettingsFile.Load(path).PinToolbar);
            ViewWindowSettingsFile.Save(new ViewWindowSettings(PinToolbar: false), path);
            Assert.False(ViewWindowSettingsFile.Load(path).PinToolbar);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
