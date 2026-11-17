using Midview.Master;
using Xunit;

// The "conta-google" step: the cable opens the phone's accounts screen.
public sealed class PhoneCableAccountsTests
{
    private const string Serial = "79a61f85";
    private const string S = "-s " + Serial + " ";
    private const string OpenAccounts = S + "shell am start -a android.settings.SYNC_SETTINGS";

    private sealed class FakeAdb : IAdb
    {
        public readonly List<string> Calls = new();
        private readonly Dictionary<string, string> _answers = new();

        public FakeAdb Set(string key, string text)
        {
            _answers[key] = text;
            return this;
        }

        public AdbOutput Run(TimeSpan timeout, params string[] args)
        {
            var key = string.Join(' ', args);
            Calls.Add(key);
            return new AdbOutput(0, _answers.TryGetValue(key, out var text) ? text : "", false);
        }
    }

    [Fact]
    public void The_accounts_screen_opens_on_the_woken_phone()
    {
        var adb = new FakeAdb()
            .Set("devices", "List of devices attached\n" + Serial + "\tdevice\n\n")
            .Set(OpenAccounts, "Starting: Intent { act=android.settings.SYNC_SETTINGS }\n");

        Assert.True(new PhoneCable(adb).OpenAccounts());

        Assert.Equal(
            new[] { "devices", S + "shell input keyevent KEYCODE_WAKEUP", S + "shell wm dismiss-keyguard", OpenAccounts },
            adb.Calls);
    }

    [Theory]
    [InlineData("List of devices attached\n\n")]
    [InlineData("List of devices attached\n" + Serial + "\tunauthorized\n\n")]
    [InlineData("List of devices attached\n" + Serial + "\tdevice\nAB12\tdevice\n\n")]
    public void Without_one_allowed_phone_nothing_is_opened(string devices)
    {
        var adb = new FakeAdb().Set("devices", devices);

        Assert.False(new PhoneCable(adb).OpenAccounts());

        Assert.DoesNotContain(adb.Calls, c => c.Contains("am start", StringComparison.Ordinal));
    }

    [Fact]
    public void A_screen_the_phone_did_not_open_says_so()
    {
        var adb = new FakeAdb()
            .Set("devices", "List of devices attached\n" + Serial + "\tdevice\n\n")
            .Set(OpenAccounts, "Error: Activity not started, unable to resolve Intent\n");

        Assert.False(new PhoneCable(adb).OpenAccounts());
    }
}
