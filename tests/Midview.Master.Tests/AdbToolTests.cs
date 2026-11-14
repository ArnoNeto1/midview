using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Midview.Master;
using Xunit;
using Xunit.Abstractions;

// AdbTool against a made-up Google (a fake HttpMessageHandler) and a tiny
// zip laid out like Platform-Tools: never the network, except the live test,
// which runs only with MIDVIEW_LIVE_ADB=1.
public sealed class AdbToolTests : IDisposable
{
    private static readonly Uri ZipUrl = new("https://dl.google.com/android/repository/platform-tools_r1.0.0-win.zip");

    private readonly ITestOutputHelper _output;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-adbtool-{Guid.NewGuid():N}");
    private readonly string _kept;
    private readonly string _program;
    private readonly Dictionary<string, byte[]> _files = new()
    {
        ["adb.exe"] = RandomNumberGenerator.GetBytes(3000),
        ["AdbWinApi.dll"] = RandomNumberGenerator.GetBytes(700),
        ["AdbWinUsbApi.dll"] = RandomNumberGenerator.GetBytes(500),
    };
    private readonly byte[] _zip;

    public AdbToolTests(ITestOutputHelper output)
    {
        _output = output;
        _kept = Path.Combine(_root, "dados", "adb");
        _program = Path.Combine(_root, "programa", "adb");
        _zip = Zip(_files, extra: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // The zip as Google lays it out: everything under "platform-tools/",
    // with tools the Midview doesn't take.
    private static byte[] Zip(Dictionary<string, byte[]> files, bool extra)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("platform-tools/");
            var all = files.ToList();
            if (extra)
            {
                all.Add(new("fastboot.exe", new byte[] { 1, 2, 3 }));
            }
            foreach (var (name, bytes) in all)
            {
                using var stream = archive.CreateEntry("platform-tools/" + name).Open();
                stream.Write(bytes);
            }
        }
        return memory.ToArray();
    }

    private AdbToolPin Pin(byte[]? zip = null, string? sha = null, string? adbSha = null)
    {
        zip ??= _zip;
        return new AdbToolPin(ZipUrl, zip.Length, sha ?? Sha(zip), new[]
        {
            new AdbToolFile("adb.exe", _files["adb.exe"].Length, adbSha ?? Sha(_files["adb.exe"])),
            new AdbToolFile("AdbWinApi.dll", _files["AdbWinApi.dll"].Length, Sha(_files["AdbWinApi.dll"])),
            new AdbToolFile("AdbWinUsbApi.dll", _files["AdbWinUsbApi.dll"].Length, Sha(_files["AdbWinUsbApi.dll"])),
        });
    }

    private AdbTool Tool(FakeGoogle google, AdbToolPin? pin = null) =>
        new(new HttpClient(google), _kept, _program, pin ?? Pin());

    private void Put(string folder, Dictionary<string, byte[]>? files = null)
    {
        Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in files ?? _files)
        {
            File.WriteAllBytes(Path.Combine(folder, name), bytes);
        }
    }

    private void AssertNothingKept()
    {
        var left = Directory.Exists(_kept) ? Directory.GetFiles(_kept) : Array.Empty<string>();
        Assert.Empty(left);
    }

    [Fact]
    public async Task Downloads_checks_and_keeps_only_the_pinned_files()
    {
        var google = new FakeGoogle(_zip);
        var tool = Tool(google);
        var reported = new List<int>();

        var adb = await tool.DownloadAsync(new SyncProgress(reported.Add), CancellationToken.None);

        Assert.Equal(Path.Combine(_kept, "adb.exe"), adb);
        Assert.Equal(ZipUrl, google.Asked.Single());
        foreach (var (name, bytes) in _files)
        {
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_kept, name)));
        }
        Assert.Equal(_files.Keys.Order(StringComparer.Ordinal),
            Directory.GetFiles(_kept).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(100, reported.Last());
        Assert.Equal(adb, tool.Find());
    }

    [Fact]
    public async Task A_zip_with_another_hash_is_refused_and_nothing_is_kept()
    {
        var tool = Tool(new FakeGoogle(_zip), Pin(sha: new string('a', 64)));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => tool.DownloadAsync(null, CancellationToken.None));

        Assert.Equal(AdbTool.Mismatch, error.Message);
        AssertNothingKept();
        Assert.Null(tool.Find());
    }

    [Fact]
    public async Task A_zip_of_another_size_is_refused()
    {
        var bigger = _zip.Append((byte)0).ToArray();
        var tool = Tool(new FakeGoogle(bigger));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => tool.DownloadAsync(null, CancellationToken.None));

        Assert.Equal(AdbTool.Mismatch, error.Message);
        AssertNothingKept();
    }

    // Without a length up front, the size is counted as it comes.
    [Fact]
    public async Task A_longer_stream_without_length_is_refused_and_a_shorter_one_is_unfinished()
    {
        var longer = Tool(new FakeGoogle(_zip.Append((byte)0).ToArray()) { WithoutLength = true });
        var longerError = await Assert.ThrowsAsync<InvalidDataException>(() => longer.DownloadAsync(null, CancellationToken.None));
        Assert.Equal(AdbTool.Mismatch, longerError.Message);

        var shorter = Tool(new FakeGoogle(_zip[..^10]) { WithoutLength = true });
        var shorterError = await Assert.ThrowsAsync<InvalidDataException>(() => shorter.DownloadAsync(null, CancellationToken.None));
        Assert.Equal(AdbTool.Unfinished, shorterError.Message);
        AssertNothingKept();
    }

    [Fact]
    public async Task A_file_that_is_not_the_pinned_one_inside_the_zip_is_refused()
    {
        var tool = Tool(new FakeGoogle(_zip), Pin(adbSha: new string('b', 64)));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => tool.DownloadAsync(null, CancellationToken.None));

        Assert.Equal(AdbTool.Mismatch, error.Message);
        Assert.False(File.Exists(Path.Combine(_kept, "adb.exe")));
        Assert.Empty(Directory.GetFiles(_kept, "*.part"));
    }

    [Fact]
    public async Task No_internet_says_so_and_nothing_is_kept()
    {
        var tool = Tool(new FakeGoogle(_zip) { Unreachable = true });

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => tool.DownloadAsync(null, CancellationToken.None));

        Assert.Equal(AdbTool.Unreachable, error.Message);
        AssertNothingKept();
    }

    // A server that takes the request and never sends its headers: given up
    // on after the read timeout, as no internet.
    [Fact]
    public async Task A_server_that_never_answers_is_given_up_on()
    {
        var tool = Tool(new FakeGoogle(_zip) { NeverAnswers = true });

        var download = tool.DownloadAsync(null, CancellationToken.None, TimeSpan.FromMilliseconds(200));
        var finished = await Task.WhenAny(download, Task.Delay(TimeSpan.FromSeconds(30)));

        Assert.Same(download, finished);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => download);
        Assert.Equal(AdbTool.Unreachable, error.Message);
        AssertNothingKept();
    }

    [Fact]
    public async Task Cancelling_while_waiting_for_the_server_is_a_cancellation()
    {
        var tool = Tool(new FakeGoogle(_zip) { NeverAnswers = true });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.DownloadAsync(null, cancel.Token, TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_refused_download_says_it_may_be_blocked(HttpStatusCode status)
    {
        var tool = Tool(new FakeGoogle(_zip) { Status = status });

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => tool.DownloadAsync(null, CancellationToken.None));

        Assert.Equal(AdbTool.Blocked, error.Message);
        AssertNothingKept();
    }

    [Fact]
    public void An_adb_already_kept_is_found_without_the_network()
    {
        Put(_kept);
        var google = new FakeGoogle(_zip);

        Assert.Equal(Path.Combine(_kept, "adb.exe"), Tool(google).Find());
        Assert.Empty(google.Asked);
    }

    // Checked again at each use: a kept file changed since is not run.
    [Fact]
    public void A_kept_adb_changed_since_is_not_found()
    {
        Put(_kept);
        var changed = (byte[])_files["adb.exe"].Clone();
        changed[0] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(_kept, "adb.exe"), changed);

        Assert.Null(Tool(new FakeGoogle(_zip)).Find());
    }

    [Fact]
    public void A_kept_adb_missing_a_dll_is_not_found()
    {
        Put(_kept);
        File.Delete(Path.Combine(_kept, "AdbWinUsbApi.dll"));

        Assert.Null(Tool(new FakeGoogle(_zip)).Find());
    }

    [Fact]
    public void The_kept_adb_comes_before_the_one_beside_the_program()
    {
        Put(_kept);
        Put(_program);

        Assert.Equal(Path.Combine(_kept, "adb.exe"), Tool(new FakeGoogle(_zip)).Find());
    }

    // An older Midview installed adb beside the program: used while it is the pinned one.
    [Fact]
    public void The_adb_an_older_install_left_is_used_when_it_matches()
    {
        Put(_kept, new() { ["adb.exe"] = new byte[] { 9 } });
        Put(_program);

        Assert.Equal(Path.Combine(_program, "adb.exe"), Tool(new FakeGoogle(_zip)).Find());
    }

    [Fact]
    public void The_adb_an_older_install_left_is_not_used_when_it_differs()
    {
        var other = new Dictionary<string, byte[]>(_files) { ["adb.exe"] = RandomNumberGenerator.GetBytes(3000) };
        Put(_program, other);

        Assert.Null(Tool(new FakeGoogle(_zip)).Find());
    }

    [Fact]
    public void Nothing_anywhere_means_download()
    {
        Assert.Null(Tool(new FakeGoogle(_zip)).Find());
        Assert.Null(new AdbTool(new HttpClient(new FakeGoogle(_zip)), _kept, null, Pin()).Find());
    }

    [Fact]
    public void Googles_pin_is_its_official_zip_and_the_three_files_adb_needs()
    {
        var pin = AdbToolPin.Google;

        Assert.Equal("https", pin.Url.Scheme);
        Assert.Equal("dl.google.com", pin.Url.Host);
        Assert.StartsWith("/android/repository/platform-tools_r", pin.Url.AbsolutePath);
        Assert.EndsWith("-win.zip", pin.Url.AbsolutePath);
        Assert.Matches("^[0-9a-f]{64}$", pin.Sha256);
        Assert.Equal(new[] { "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll" }, pin.Files.Select(file => file.Name));
        Assert.All(pin.Files, file => Assert.Matches("^[0-9a-f]{64}$", file.Sha256));
        Assert.Equal(Path.Combine(MasterData.Folder, "adb"), AdbTool.DefaultFolder);
    }

    // The real zip from Google, then "adb version" from the copy kept:
    // MIDVIEW_LIVE_ADB=1 only (it downloads about 8 MB).
    [Fact]
    public async Task Live_the_real_zip_downloads_and_its_adb_runs()
    {
        if (Environment.GetEnvironmentVariable("MIDVIEW_LIVE_ADB") != "1")
        {
            _output.WriteLine("MIDVIEW_LIVE_ADB is not 1: Google was not asked");
            return;
        }
        using var http = new HttpClient();
        var tool = new AdbTool(http, _kept, null, AdbToolPin.Google);

        var path = await tool.DownloadAsync(null, CancellationToken.None);

        using var adb = new AdbProcess(path);
        var output = adb.Run(TimeSpan.FromSeconds(20), "version");
        _output.WriteLine(output.Text);
        Assert.Equal(0, output.ExitCode);
        Assert.Contains("Android Debug Bridge", output.Text);
        Assert.Equal(path, tool.Find());
    }

    // Reported in place, not posted: the list is read right after.
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    private sealed class FakeGoogle(byte[] body) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = new();

        public bool Unreachable { get; init; }

        // Takes the request and never answers.
        public bool NeverAnswers { get; init; }

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        // Sent as a stream of unknown length, as a proxy may.
        public bool WithoutLength { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);
            if (Unreachable)
            {
                throw new HttpRequestException("no route to host");
            }
            if (NeverAnswers)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            HttpContent content = WithoutLength
                ? new StreamContent(new UnknownLength(body))
                : new ByteArrayContent(body);
            return new HttpResponseMessage(Status) { Content = content };
        }
    }

    private sealed class UnknownLength(byte[] body) : MemoryStream(body)
    {
        public override bool CanSeek => false;
    }
}
