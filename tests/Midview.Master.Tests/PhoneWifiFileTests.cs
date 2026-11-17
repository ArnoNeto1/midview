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

    // A school's voucher Wi-Fi (Omada) is open: no password, the voucher code
    // is typed later on each device's login page.
    [Fact]
    public void An_open_network_has_no_password_and_a_short_one_is_refused()
    {
        Assert.Null(PhoneWifiFile.PasswordProblem(""));
        Assert.Null(PhoneWifiFile.PasswordProblem("12345678"));
        Assert.NotNull(PhoneWifiFile.PasswordProblem("1234567"));
        Assert.NotNull(PhoneWifiFile.PasswordProblem(" "));
    }

    [Fact]
    public void An_open_network_round_trips()
    {
        PhoneWifiFile.Save("Hermann 2026", "", _folder);

        Assert.Equal(("Hermann 2026", ""), PhoneWifiFile.Load(_folder));
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
