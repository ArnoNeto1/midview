// A stand-in for Office 2007's setup.exe: it reads the /config file the way
// the real one does (at start, then again while it works), takes a moment
// like a real removal, writes what it saw to MIDVIEW_FAKE_SETUP_REPORT and
// exits with MIDVIEW_FAKE_SETUP_EXIT.
var arguments = Environment.GetCommandLineArgs()[1..];
var configAt = Array.FindIndex(arguments, a => string.Equals(a, "/config", StringComparison.OrdinalIgnoreCase));
var config = configAt >= 0 && configAt + 1 < arguments.Length ? arguments[configAt + 1] : null;

var atStart = config is not null && File.Exists(config) ? File.ReadAllText(config).Trim() : "(no config)";
Thread.Sleep(TimeSpan.FromSeconds(2));
var atEnd = config is not null && File.Exists(config);

if (Environment.GetEnvironmentVariable("MIDVIEW_FAKE_SETUP_REPORT") is { Length: > 0 } report)
{
    File.WriteAllLines(report, new[] { string.Join(" ", arguments), atStart, atEnd ? "config still there" : "config gone" });
}
return int.TryParse(Environment.GetEnvironmentVariable("MIDVIEW_FAKE_SETUP_EXIT"), out var code) ? code : 0;
