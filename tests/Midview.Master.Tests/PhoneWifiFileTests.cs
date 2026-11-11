using Midview.Master;
using Xunit;

public class PhoneWifiFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-wifi-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Wifi_file_round_trips_and_is_not_plain_text()
    {
        PhoneWifiFile.Save("Lab-Escola", "Senha@çã-segredo", _folder);

        var loaded = PhoneWifiFile.Load(_folder);

        Assert.NotNull(loaded);
        Assert.Equal("Lab-Escola", loaded!.Value.Ssid);
        Assert.Equal("Senha@çã-segredo", loaded.Value.Password);
        foreach (var file in Directory.GetFiles(_folder))
        {
            var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file));
            Assert.DoesNotContain("segredo", text);
            Assert.DoesNotContain("Lab-Escola", text);
        }
    }

    [Fact]
    public void Load_returns_null_when_absent_or_damaged()
    {
        Assert.Null(PhoneWifiFile.Load(_folder));
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(_folder, "phone-wifi.bin"), new byte[] { 1, 2, 3, 4, 5 });

        Assert.Null(PhoneWifiFile.Load(_folder));
    }
}
