using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Midview.Master;
using Midview.Protocol;
using Xunit;

// PhoneAgentApk against a made-up GitHub (a fake HttpMessageHandler): never the network.
public sealed class PhoneAgentApkTests : IDisposable
{
    private static readonly Version V = new(0, 0, 53);
    private const string DownloadUrl = "https://github.com/ArnoNeto1/midview/releases/download/v0.0.53/MidviewAgent.apk";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"midview-apk-{Guid.NewGuid():N}");
    private readonly byte[] _apk = RandomNumberGenerator.GetBytes(FileTransfer.ChunkSize * 2 + 77);

    public PhoneAgentApkTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private string Release(string? digest = "default", string url = DownloadUrl, long? size = null, string name = PhoneAgentApk.AssetName) =>
        JsonSerializer.Serialize(new
        {
            tag_name = "v0.0.53",
            assets = new object[]
            {
                new { name = "MidviewSetup.exe", browser_download_url = "https://github.com/x/MidviewSetup.exe", size = 10, digest = "sha256:" + new string('a', 64) },
                digest is null
                    ? new { name, browser_download_url = url, size = size ?? _apk.Length }
                    : (object)new { name, browser_download_url = url, size = size ?? _apk.Length, digest = digest == "default" ? "sha256:" + Sha(_apk) : digest },
            },
        });

    private static HttpClient Client(FakeGitHub handler) => new(handler);

    [Fact]
    public async Task Downloads_the_release_asset_checks_it_and_keeps_it()
    {
        var github = new FakeGitHub(Release(), _apk);
        using var client = Client(github);

        using var apk = await PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None);

        Assert.Equal("https://api.github.com/repos/ArnoNeto1/midview/releases/tags/v0.0.53", github.Asked[0].ToString());
        Assert.Equal(DownloadUrl, github.Asked[1].ToString());
        Assert.Equal(Path.Combine(_folder, "MidviewAgent-0.0.53.apk"), apk.ZipPath);
        Assert.Equal(_apk, File.ReadAllBytes(apk.ZipPath));
        Assert.Equal(_apk.Length, apk.ZipSize);
        Assert.Equal(Sha(_apk), apk.Sha256Hex, ignoreCase: true);
        Assert.True(PhoneAgentApk.IsKept(_folder, V));
        Assert.Empty(Directory.GetFiles(_folder, "*.part"));
    }

    [Fact]
    public async Task A_kept_apk_that_matches_is_reused_without_downloading()
    {
        File.WriteAllBytes(PhoneAgentApk.KeptPath(_folder, V), _apk);
        var github = new FakeGitHub(Release(), new byte[] { 9 });
        using var client = Client(github);

        using var apk = await PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None);

        Assert.Single(github.Asked);
        Assert.Equal(_apk, File.ReadAllBytes(apk.ZipPath));
    }

    [Fact]
    public async Task A_kept_apk_that_does_not_match_is_downloaded_again()
    {
        File.WriteAllBytes(PhoneAgentApk.KeptPath(_folder, V), new byte[] { 1, 2, 3 });
        var github = new FakeGitHub(Release(), _apk);
        using var client = Client(github);

        using var apk = await PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None);

        Assert.Equal(2, github.Asked.Count);
        Assert.Equal(_apk, File.ReadAllBytes(apk.ZipPath));
    }

    // GitHub out of reach: the APK kept earlier was checked when it was saved.
    [Fact]
    public async Task A_kept_apk_is_used_when_github_cannot_be_reached()
    {
        File.WriteAllBytes(PhoneAgentApk.KeptPath(_folder, V), _apk);
        using var client = Client(new FakeGitHub(Release(), _apk) { Unreachable = true });

        using var apk = await PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None);

        Assert.Equal(_apk.Length, apk.ZipSize);
    }

    [Fact]
    public async Task Github_out_of_reach_and_nothing_kept_says_so()
    {
        using var client = Client(new FakeGitHub(Release(), _apk) { Unreachable = true });

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None));

        Assert.Equal("não foi possível falar com o GitHub para baixar o app do celular (confira a internet deste PC)", error.Message);
    }

    [Fact]
    public async Task A_version_not_published_says_so()
    {
        using var client = Client(new FakeGitHub(Release(), _apk) { ReleaseStatus = HttpStatusCode.NotFound });

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None));

        Assert.Equal("a versão 0.0.53 do Midview ainda não foi publicada no GitHub", error.Message);
    }

    [Fact]
    public async Task An_asset_without_digest_is_refused()
    {
        var github = new FakeGitHub(Release(digest: null), _apk);
        using var client = Client(github);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None));

        Assert.Equal("o GitHub não informou a soma de verificação do app do celular da versão 0.0.53", error.Message);
        Assert.Single(github.Asked);
    }

    [Theory]
    [InlineData("https://example.com/MidviewAgent.apk")]
    [InlineData("http://github.com/ArnoNeto1/midview/releases/download/v0.0.53/MidviewAgent.apk")]
    public async Task An_asset_outside_github_is_refused(string url)
    {
        var github = new FakeGitHub(Release(url: url), _apk);
        using var client = Client(github);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None));

        Assert.Equal("o app do celular da versão 0.0.53 não está no GitHub", error.Message);
        Assert.Single(github.Asked);
    }

    [Fact]
    public async Task A_download_with_the_wrong_hash_is_refused_and_not_kept()
    {
        var github = new FakeGitHub(Release(digest: "sha256:" + new string('0', 64)), _apk);
        using var client = Client(github);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None));

        Assert.Equal("o app do celular baixado não confere com o publicado no GitHub", error.Message);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    // The connection drops in the middle: the teacher reads Portuguese, never
    // .NET's English message, and nothing is kept.
    [Fact]
    public async Task A_download_that_drops_says_it_did_not_finish()
    {
        var github = new FakeGitHub(Release(), _apk) { ApkStream = () => new DroppingStream(_apk, after: 1000) };
        using var client = Client(github);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None));

        Assert.Equal("o download do app do celular não terminou (confira a internet deste PC)", error.Message);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    // The connection stays open but nothing more arrives: the download gives
    // up after the read timeout (60 s in the program) with the same text.
    [Fact]
    public async Task A_download_that_stalls_gives_up_with_the_same_text()
    {
        var github = new FakeGitHub(Release(), _apk) { ApkStream = () => new StallingStream(_apk, after: 1000) };
        using var client = Client(github);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None, readTimeout: TimeSpan.FromMilliseconds(200)));

        Assert.Equal("o download do app do celular não terminou (confira a internet deste PC)", error.Message);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    // The teacher's own cancel is not a stall: it stays a cancellation.
    [Fact]
    public async Task Cancelling_a_stalled_download_stays_a_cancellation()
    {
        var github = new FakeGitHub(Release(), _apk) { ApkStream = () => new StallingStream(_apk, after: 1000) };
        using var client = Client(github);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PhoneAgentApk.ForVersionAsync(V, client, _folder, cancel.Token, readTimeout: TimeSpan.FromMinutes(1)));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task An_asset_too_big_is_refused()
    {
        var github = new FakeGitHub(Release(size: PhoneUpdate.MaxApkSize + 1), _apk);
        using var client = Client(github);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None));

        Assert.Equal("o app do celular da versão 0.0.53 é grande demais (até 64 MB)", error.Message);
        Assert.Single(github.Asked);
    }

    [Fact]
    public async Task A_release_without_the_apk_says_so()
    {
        using var client = Client(new FakeGitHub(Release(name: "Outro.apk"), _apk));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => PhoneAgentApk.ForVersionAsync(V, client, _folder, CancellationToken.None));

        Assert.Equal("a versão 0.0.53 no GitHub não tem o app do celular (MidviewAgent.apk)", error.Message);
    }

    [Fact]
    public void From_file_offers_the_apk_hash_and_size_as_both_fingerprint_and_zip()
    {
        var path = Path.Combine(_folder, "local.apk");
        File.WriteAllBytes(path, _apk);
        using var apk = PhoneAgentApk.FromFile(path);
        var attempt = RandomNumberGenerator.GetBytes(NetworkUpdate.AttemptIdLength);

        var offer = apk.Offer(attempt);

        var sha = SHA256.HashData(_apk);
        Assert.Equal(attempt, offer.AttemptId);
        Assert.Equal(sha, offer.Fingerprint);
        Assert.Equal(sha, offer.ZipSha256);
        Assert.Equal(_apk.Length, offer.ZipSize);
        Assert.Equal(_apk.Length, offer.UncompressedSize);
        Assert.Equal(Convert.ToHexString(sha), apk.Sha256Hex);
        // The file is the teacher's: never deleted with the object.
        apk.Dispose();
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void From_file_too_big_or_empty_is_refused()
    {
        var empty = Path.Combine(_folder, "vazio.apk");
        File.WriteAllBytes(empty, Array.Empty<byte>());

        var error = Assert.Throws<InvalidDataException>(() => PhoneAgentApk.FromFile(empty));
        Assert.Equal("o arquivo do app do celular está vazio", error.Message);

        var big = Path.Combine(_folder, "grande.apk");
        using (var stream = File.Create(big))
        {
            stream.SetLength(PhoneUpdate.MaxApkSize + 1);
        }
        Assert.Equal("o app do celular é grande demais (até 64 MB)", Assert.Throws<InvalidDataException>(() => PhoneAgentApk.FromFile(big)).Message);
    }

    // A made-up GitHub: the release's JSON at the API, the APK at its download URL.
    private sealed class FakeGitHub(string release, byte[] apk) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = new();

        public bool Unreachable { get; init; }

        public HttpStatusCode ReleaseStatus { get; init; } = HttpStatusCode.OK;

        // The APK's body, when it must misbehave.
        public Func<Stream>? ApkStream { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);
            if (Unreachable)
            {
                throw new HttpRequestException("No such host is known.");
            }
            if (request.RequestUri!.Host == "api.github.com")
            {
                Assert.Contains("Midview", request.Headers.UserAgent.ToString());
                return Task.FromResult(new HttpResponseMessage(ReleaseStatus) { Content = new StringContent(release) });
            }
            HttpContent content = ApkStream is null ? new ByteArrayContent(apk) : new StreamContent(ApkStream());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    // Gives the first bytes, then fails as a dropped connection does.
    private sealed class DroppingStream(byte[] apk, int after) : MemoryStream(apk, 0, after)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            return read > 0 ? read : throw new IOException("Unable to read data from the transport connection: An existing connection was forcibly closed by the remote host.");
        }
    }

    // Gives the first bytes, then waits forever (until cancelled).
    private sealed class StallingStream(byte[] apk, int after) : MemoryStream(apk, 0, after)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            if (read > 0)
            {
                return read;
            }
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
