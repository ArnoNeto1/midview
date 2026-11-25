using Midview.Master;
using Midview.Protocol;
using Xunit;

// "Papel de parede e PIN da escola" in "Preparar celulares": asked once for
// each kind, right after the Wi-Fi, before what to separate; "Pular" goes on
// as "Guardar e seguir" does, and "trocar" brings it back from any step.
public sealed class PhoneSetupProgressDefaultsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-defaults-asked-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Not_asked_yet_the_wifi_leads_to_the_defaults_then_what_to_separate()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: false, gathered: false, defaultsAsked: false);

        progress.Next();
        Assert.Equal(SetupPage.Defaults, progress.Page);

        progress.Next();
        Assert.Equal(SetupPage.Gather, progress.Page);
    }

    [Fact]
    public void With_wifi_and_not_asked_it_starts_on_the_defaults_and_goes_to_the_number()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: true, gathered: true, defaultsAsked: false);
        Assert.Equal(SetupPage.Defaults, progress.Page);

        progress.Next();
        Assert.Equal("numero", progress.StepId);
    }

    [Fact]
    public void Back_from_the_defaults_is_the_wifi_page()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: true, gathered: true, defaultsAsked: false);

        progress.Back();

        Assert.Equal(SetupPage.Wifi, progress.Page);
        progress.Next();
        Assert.Equal(SetupPage.Defaults, progress.Page);
    }

    [Fact]
    public void Asked_before_it_is_not_shown()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: false, gathered: true, defaultsAsked: true);

        progress.Next();

        Assert.Equal("numero", progress.StepId);
    }

    [Fact]
    public void Trocar_opens_it_from_a_step_and_comes_back_to_that_step()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: true, gathered: true, defaultsAsked: true);
        progress.SetNumber("7");
        progress.Next();
        Assert.Equal("ligar", progress.StepId);

        progress.ChangeDefaults();
        Assert.Equal(SetupPage.Defaults, progress.Page);
        Assert.True(progress.ReturnsAfterDefaults);
        progress.Next();
        Assert.Equal("ligar", progress.StepId);

        progress.ChangeDefaults();
        progress.Back();
        Assert.Equal("ligar", progress.StepId);
    }

    // Asked once for phones and once for tablets: the pictures differ.
    [Fact]
    public void The_question_is_remembered_for_each_kind()
    {
        Assert.False(PhoneDefaultsFile.Asked(AndroidDeviceKind.Phone, _folder));

        PhoneDefaultsFile.MarkAsked(AndroidDeviceKind.Tablet, _folder);

        Assert.True(PhoneDefaultsFile.Asked(AndroidDeviceKind.Tablet, _folder));
        Assert.False(PhoneDefaultsFile.Asked(AndroidDeviceKind.Phone, _folder));
    }

    [Fact]
    public void The_page_says_where_the_defaults_go_and_how_to_skip()
    {
        Assert.Equal("Papel de parede, PIN e senha da escola", PhoneDefaultsText.Heading);
        Assert.Contains("cada tablet preparado", PhoneDefaultsText.Intro(AndroidDeviceKind.Tablet));
        Assert.Contains("senha de administrador é obrigatória", PhoneDefaultsText.Intro(AndroidDeviceKind.Tablet));
        Assert.Contains("Configurar tablet", PhoneDefaultsText.Intro(AndroidDeviceKind.Tablet));
        Assert.Contains("cada celular preparado", PhoneDefaultsText.Intro(AndroidDeviceKind.Phone));
        Assert.Contains("Configurar celular", PhoneDefaultsText.Intro(AndroidDeviceKind.Phone));
    }
}
