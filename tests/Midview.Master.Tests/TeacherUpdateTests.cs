using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Midview.Master;
using Xunit;

public sealed class TeacherUpdateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"midview-atualizacao-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private static readonly byte[] Setup = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();
    private static readonly string SetupSha256 = Convert.ToHexString(SHA256.HashData(Setup)).ToLowerInvariant();

    // What the GitHub API answers for the latest release, cut to what is read.
    private static string ReleaseJson(
        string tag = "v0.0.38", string assetName = "MidviewSetup.exe", string? digest = null, long? size = null,
        string url = "https://github.com/ArnoNeto1/midview/releases/download/v0.0.38/MidviewSetup.exe") =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Versão 0.0.38",
          "assets": [
            { "name": "outro.zip", "size": 10, "digest": "sha256:{{new string('0', 64)}}",
              "browser_download_url": "https://github.com/ArnoNeto1/midview/releases/download/v0.0.38/outro.zip" },
            { "name": "{{assetName}}", "size": {{size ?? Setup.Length}}, "digest": {{(digest is null ? $"\"sha256:{SetupSha256}\"" : digest)}},
              "browser_download_url": "{{url}}" }
          ]
        }
        """;

    [Fact]
    public void ParseRelease_ReadsTheVersionAndTheSetup()
    {
        var release = TeacherUpdate.ParseRelease(ReleaseJson());

        Assert.Equal(new Version(0, 0, 38), release.Version);
        Assert.Equal("https://github.com/ArnoNeto1/midview/releases/download/v0.0.38/MidviewSetup.exe", release.SetupUrl.ToString());
        Assert.Equal(Setup.Length, release.Size);
        Assert.Equal(SetupSha256, release.Sha256);
    }

    [Theory]
    [InlineData("null")]                          // GitHub gave no checksum: nothing to check the download against
    [InlineData("\"md5:abc\"")]
    [InlineData("\"sha256:1234\"")]
    public void ParseRelease_WithoutAChecksum_IsRefused(string digest)
    {
        Assert.Throws<InvalidDataException>(() => TeacherUpdate.ParseRelease(ReleaseJson(digest: digest)));
    }

    [Fact]
    public void ParseRelease_WithoutTheSetup_IsRefused()
    {
        var ex = Assert.Throws<InvalidDataException>(() => TeacherUpdate.ParseRelease(ReleaseJson(assetName: "Outro.exe")));

        Assert.Contains("MidviewSetup.exe", ex.Message);
    }

    [Theory]
    [InlineData("http://github.com/ArnoNeto1/midview/releases/download/v0.0.38/MidviewSetup.exe")]
    [InlineData("https://exemplo.com/MidviewSetup.exe")]
    public void ParseRelease_ASetupFromElsewhere_IsRefused(string url)
    {
        Assert.Throws<InvalidDataException>(() => TeacherUpdate.ParseRelease(ReleaseJson(url: url)));
    }

    [Theory]
    [InlineData("v0.0.38", 0, 0, 38)]
    [InlineData("0.1.2", 0, 1, 2)]
    public void ParseRelease_ReadsTheTag(string tag, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build), TeacherUpdate.ParseRelease(ReleaseJson(tag: tag)).Version);
    }

    [Theory]
    [InlineData("teste")]
    [InlineData("v")]
    public void ParseRelease_ATagThatIsntAVersion_IsRefused(string tag)
    {
        Assert.Throws<InvalidDataException>(() => TeacherUpdate.ParseRelease(ReleaseJson(tag: tag)));
    }

    [Fact]
    public void ParseRelease_NotJson_IsRefused()
    {
        Assert.Throws<InvalidDataException>(() => TeacherUpdate.ParseRelease("<html>"));
    }

    [Theory]
    [InlineData("0.0.37", "0.0.38", TeacherUpdate.Standing.Newer)]
    [InlineData("0.0.38", "0.0.38", TeacherUpdate.Standing.Same)]
    [InlineData("0.0.39", "0.0.38", TeacherUpdate.Standing.Older)]
    [InlineData("0.0.9", "0.0.10", TeacherUpdate.Standing.Newer)]
    public void Compare_TellsWhetherThePublishedOneIsNewer(string current, string published, TeacherUpdate.Standing standing)
    {
        Assert.Equal(standing, TeacherUpdate.Compare(Version.Parse(current), Version.Parse(published)));
    }

    [Fact]
    public void CurrentVersion_IsThisBuildsVersion()
    {
        Assert.True(TeacherUpdate.CurrentVersion > new Version(0, 0, 36));
    }

    [Fact]
    public async Task Download_SavesTheCheckedSetup_AndSaysHowFarItGot()
    {
        var progress = new List<int>();
        using var client = new HttpClient(new Serving(Setup));

        var path = await TeacherUpdate.DownloadAsync(
            client, TeacherUpdate.ParseRelease(ReleaseJson()), _folder, new SyncProgress(progress.Add), CancellationToken.None);

        Assert.Equal(Path.Combine(_folder, "MidviewSetup-0.0.38.exe"), path);
        Assert.Equal(Setup, File.ReadAllBytes(path));
        Assert.Equal(100, progress.Last());
        Assert.Equal(progress.OrderBy(p => p), progress);
    }

    [Fact]
    public async Task Download_ThatDoesntMatchTheChecksum_LeavesNothing()
    {
        var other = Setup.ToArray();
        other[1000] ^= 1;
        using var client = new HttpClient(new Serving(other));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => TeacherUpdate.DownloadAsync(
            client, TeacherUpdate.ParseRelease(ReleaseJson()), _folder, null, CancellationToken.None));

        Assert.Contains("não confere", ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public async Task Download_Incomplete_LeavesNothing()
    {
        using var client = new HttpClient(new Serving(Setup[..1000]));

        await Assert.ThrowsAsync<InvalidDataException>(() => TeacherUpdate.DownloadAsync(
            client, TeacherUpdate.ParseRelease(ReleaseJson()), _folder, null, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    // An earlier version's setup left behind (an update that never ran) goes.
    [Fact]
    public async Task Download_RemovesAnEarlierSetup()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "MidviewSetup-0.0.36.exe"), "velho");
        File.WriteAllText(Path.Combine(_folder, "instalacao-0.0.36.log"), "registro");
        using var client = new HttpClient(new Serving(Setup));

        await TeacherUpdate.DownloadAsync(client, TeacherUpdate.ParseRelease(ReleaseJson()), _folder, null, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_folder, "MidviewSetup-0.0.36.exe")));
        Assert.True(File.Exists(Path.Combine(_folder, "instalacao-0.0.36.log")));
    }

    [Fact]
    public async Task FetchLatest_AsksGitHubForTheLatestRelease()
    {
        var handler = new Serving(System.Text.Encoding.UTF8.GetBytes(ReleaseJson()));
        using var client = new HttpClient(handler);

        var release = await TeacherUpdate.FetchLatestAsync(client, CancellationToken.None);

        Assert.Equal(new Version(0, 0, 38), release.Version);
        Assert.Equal(TeacherUpdate.LatestReleaseUrl, handler.Asked!.RequestUri!.ToString());
    }

    [Fact]
    public async Task FetchLatest_WithNothingPublished_SaysSo()
    {
        using var client = new HttpClient(new Serving(Array.Empty<byte>(), HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => TeacherUpdate.FetchLatestAsync(client, CancellationToken.None));

        Assert.Contains("nenhuma versão", ex.Message);
    }

    [Fact]
    public void CreateClient_NamesItselfToGitHub()
    {
        using var client = TeacherUpdate.CreateClient();

        Assert.Contains(client.DefaultRequestHeaders.UserAgent, product => product.Product?.Name == "Midview");
    }

    [Theory]
    [InlineData(null, @"C:\Program Files\Midview Professor")]
    [InlineData("aluno", @"C:\Program Files\Midview")]
    public void InstalledProgram_NotATeacherInstall_IsAProblem(string? kind, string location)
    {
        var program = TeacherUpdate.InstalledProgram(kind, location, @"C:\Program Files\Midview Professor\Midview.Master.exe", out var problem);

        Assert.Null(program);
        Assert.Contains("MidviewSetup", problem);
    }

    [Fact]
    public void InstalledProgram_ACopyRunningElsewhere_IsAProblem()
    {
        var program = TeacherUpdate.InstalledProgram(
            "professor", @"C:\Program Files\Midview Professor", @"C:\Users\x\Desktop\teste\Midview.Master.exe", out var problem);

        Assert.Null(program);
        Assert.Contains(@"C:\Program Files\Midview Professor", problem);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Midview Professor")]
    [InlineData(@"C:\Program Files\Midview Professor\")]
    [InlineData(@"c:\program files\midview professor")]
    public void InstalledProgram_TheInstalledCopy_IsWhatTheSetupReplaces(string location)
    {
        var program = TeacherUpdate.InstalledProgram(
            "professor", location, @"C:\Program Files\Midview Professor\Midview.Master.exe", out var problem);

        Assert.Equal(Path.Combine(location, "Midview.Master.exe"), program);
        Assert.Equal(string.Empty, problem);
    }

    [Fact]
    public void FinishedNotice_ForAnUpdateThatWorked()
    {
        var notice = TeacherUpdate.FinishedNotice("0", "0.0.38", new Version(0, 0, 38), _folder);

        Assert.Equal("O Midview deste PC foi atualizado para a versão 0.0.38.", notice);
    }

    [Fact]
    public void FinishedNotice_ForASetupThatFailed_SaysWhereTheLogIs()
    {
        var notice = TeacherUpdate.FinishedNotice("5", "0.0.38", new Version(0, 0, 37), _folder)!;

        Assert.Contains("não foi feita", notice);
        Assert.Contains("código 5", notice);
        Assert.Contains(Path.Combine(_folder, "instalacao-0.0.38.log"), notice);
    }

    [Fact]
    public void FinishedNotice_ForADownloadThatChanged()
    {
        var notice = TeacherUpdate.FinishedNotice("-1", "0.0.38", new Version(0, 0, 37), _folder)!;

        Assert.Contains("não confere", notice);
    }

    [Fact]
    public void FinishedNotice_ForSomethingElse_IsNothing()
    {
        Assert.Null(TeacherUpdate.FinishedNotice("x", "0.0.38", new Version(0, 0, 37), _folder));
        Assert.Null(TeacherUpdate.FinishedNotice("0", "não", new Version(0, 0, 37), _folder));
    }

    // Only an update that really put the new version on this PC goes on to
    // ask about the students' machines.
    [Theory]
    [InlineData("0", "0.0.40", 40, true)]
    [InlineData("0", "0.0.40", 41, true)]
    [InlineData("0", "0.0.40", 39, false)]
    [InlineData("-1", "0.0.40", 39, false)]
    [InlineData("5", "0.0.40", 40, false)]
    [InlineData("x", "0.0.40", 40, false)]
    [InlineData("0", "não", 40, false)]
    public void FinishedWell_OnlyForTheNewVersionInstalled(string code, string version, int currentBuild, bool expected)
    {
        Assert.Equal(expected, TeacherUpdate.FinishedWell(code, version, new Version(0, 0, currentBuild)));
    }

    [Fact]
    public void NewVersionContent_SaysBothVersionsAndWhatHappens()
    {
        var content = TeacherUpdate.NewVersionContent(new Version(0, 0, 39), new Version(0, 0, 40));

        Assert.Equal("Atualizar professor", content.Title);
        Assert.Equal("Chegou uma versão nova do Midview", content.Heading);
        Assert.Equal(("0.0.39", "0.0.40"), content.Versions);
        Assert.Equal("Atualizar agora", content.Yes);
        Assert.Equal("Agora não", content.No);
        Assert.Equal(
            new[]
            {
                "O Midview fecha e abre de novo sozinho, o que leva 1 ou 2 minutos.",
                "Enquanto isso, as telas bloqueadas dos alunos são liberadas.",
                "O Windows vai pedir permissão de administrador.",
            },
            content.Facts!.Select(f => f.Text));
    }

    [Fact]
    public void ApplyStartInfo_RunsTheScriptHidden_WithEverythingItNeeds()
    {
        var info = TeacherUpdate.ApplyStartInfo(
            @"C:\Dados\aplicar.ps1", @"C:\Dados\MidviewSetup-0.0.38.exe", SetupSha256, 1234,
            @"C:\Program Files\Midview Professor\Midview.Master.exe", @"C:\Dados\instalacao-0.0.38.log", new Version(0, 0, 38));

        Assert.EndsWith(@"WindowsPowerShell\v1.0\powershell.exe", info.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        var args = info.ArgumentList.ToList();
        Assert.Equal(@"C:\Dados\aplicar.ps1", args[args.IndexOf("-File") + 1]);
        Assert.Equal("1234", args[args.IndexOf("-EsperarPid") + 1]);
        Assert.Equal(SetupSha256, args[args.IndexOf("-Sha256") + 1]);
        Assert.Equal(@"C:\Program Files\Midview Professor\Midview.Master.exe", args[args.IndexOf("-Programa") + 1]);
        Assert.Equal("0.0.38", args[args.IndexOf("-Versao") + 1]);
    }

    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    private sealed class Serving(byte[] body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }
}
