using Midview.Master;
using Xunit;

// The school's Mi account on disk, and what an open assistant learns of it.
public class MiAccountFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-mi-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Saving_and_taking_it_off_tell_an_open_assistant()
    {
        var told = 0;
        void Count() => told++;
        MiAccountFile.Changed += Count;
        try
        {
            MiAccountFile.Save(new MiAccount("escola@example.com", "Escola2026"), _folder);
            Assert.Equal(1, told);
            Assert.Equal(new MiAccount("escola@example.com", "Escola2026"), MiAccountFile.Load(_folder));

            MiAccountFile.Clear(_folder);
            Assert.Equal(2, told);
            Assert.Null(MiAccountFile.Load(_folder));
        }
        finally
        {
            MiAccountFile.Changed -= Count;
        }
    }

    // A password the cable can't type: kept for signing in, signed out of by hand.
    [Theory]
    [InlineData("Escola 2026!", true)]
    [InlineData("it's", true)]
    [InlineData("ação2026", false)]
    [InlineData("senha%segura", false)]
    public void Only_a_password_the_cable_can_type_is_signed_out_of_by_the_cable(string password, bool canType)
    {
        Assert.Equal(canType, new MiAccount("escola@example.com", password).CableCanType);
    }

    [Fact]
    public void The_by_hand_note_names_what_the_cable_cannot_type()
    {
        Assert.Contains("acentos ou %s", PhoneSetupGuide.MiPasswordByHand, StringComparison.Ordinal);
        Assert.Contains("à mão", PhoneSetupGuide.MiPasswordByHand, StringComparison.Ordinal);
    }
}
