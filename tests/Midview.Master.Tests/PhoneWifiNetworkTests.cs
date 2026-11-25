using Midview.Master;
using Midview.Protocol;
using Xunit;

// "Rede Wi-Fi nova da escola": the network as the device gets it
// (PhoneWifiNetwork.kt reads the same bytes), what the window refuses
// before sending, and its words.
public class PhoneWifiNetworkTests
{
    [Fact]
    public void A_network_round_trips_with_and_without_a_password()
    {
        Assert.Equal(("Escola Nova", "senha1234"), PhoneWifiNetwork.Decode(PhoneWifiNetwork.Encode("Escola Nova", "senha1234")));
        Assert.Equal(("Hermann 2026", ""), PhoneWifiNetwork.Decode(PhoneWifiNetwork.Encode("Hermann 2026", "")));
        Assert.Equal(("Educação", ""), PhoneWifiNetwork.Decode(PhoneWifiNetwork.Encode("Educação", null)));
    }

    // The bytes PhoneWifiNetworkTest.theMastersBytesDecode reads on Android.
    [Fact]
    public void The_bytes_are_the_agents()
    {
        Assert.Equal(new byte[] { 2, (byte)'A', (byte)'B', 8, (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'6', (byte)'7', (byte)'8' },
            PhoneWifiNetwork.Encode("AB", "12345678"));
        Assert.Equal(new byte[] { 4, (byte)'H', (byte)'e', (byte)'r', (byte)'m', 0 }, PhoneWifiNetwork.Encode("Herm", ""));
    }

    [Fact]
    public void What_cannot_be_a_network_is_said_before_sending()
    {
        Assert.Equal(PhoneWifiNetwork.NameMissing, PhoneWifiNetwork.Problem("", ""));
        Assert.Equal(PhoneWifiNetwork.NameMissing, PhoneWifiNetwork.Problem("   ", ""));
        Assert.Equal(PhoneWifiNetwork.NameTooLong, PhoneWifiNetwork.Problem(new string('x', 33), ""));
        Assert.Equal(PhoneWifiNetwork.PasswordInvalid, PhoneWifiNetwork.Problem("Escola", "1234567"));
        Assert.Equal(PhoneWifiNetwork.PasswordInvalid, PhoneWifiNetwork.Problem("Escola", "senhaçãoabc"));
        Assert.Equal(PhoneWifiNetwork.PasswordInvalid, PhoneWifiNetwork.Problem("Escola", new string('a', 64)));
        Assert.Null(PhoneWifiNetwork.Problem("Escola", ""));
        Assert.Null(PhoneWifiNetwork.Problem("Escola", "12345678"));
        Assert.Throws<ArgumentException>(() => PhoneWifiNetwork.Encode("", "x"));
    }

    [Fact]
    public void A_payload_the_master_never_sends_is_refused()
    {
        Assert.Null(PhoneWifiNetwork.Decode(Array.Empty<byte>()));
        Assert.Null(PhoneWifiNetwork.Decode(new byte[] { 0, 0 }));
        Assert.Null(PhoneWifiNetwork.Decode(new byte[] { 5, (byte)'A', 0 }));
        Assert.Null(PhoneWifiNetwork.Decode(new byte[] { 1, (byte)'A', 3, (byte)'1', (byte)'2', (byte)'3' }));
        Assert.Null(PhoneWifiNetwork.Decode(new byte[] { 1, (byte)'A', 0, 9 }));
    }

    [Fact]
    public void The_window_words_the_new_network()
    {
        Assert.Equal("pondo a rede…", PhoneNetworkText.Working(PhoneNetworkAction.AddWifi));
        Assert.Equal("Pondo a rede Wi-Fi nova nos 3 aparelhos...", PhoneNetworkText.Progress(PhoneNetworkAction.AddWifi, 3));
        Assert.Equal("Rede Wi-Fi nova posta nos 3 aparelhos.", PhoneNetworkText.Summary(PhoneNetworkAction.AddWifi, 0, 3, 3));
        Assert.Equal("Rede Wi-Fi nova posta em 2 dos 3 aparelhos (veja abaixo).", PhoneNetworkText.Summary(PhoneNetworkAction.AddWifi, 0, 2, 3));
        Assert.Contains("voucher", PhoneNetworkText.WifiCardLine);
        Assert.Equal(112, (int)MessageType.AddPhoneWifi);
    }
}
