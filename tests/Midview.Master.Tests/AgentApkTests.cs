using System.Net;
using Midview.Master;
using Xunit;

public class AgentApkTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-agentapk-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class FakeHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond());
    }

    private static byte[] Apk(byte[] cert) =>
        ApkTestBuilder.Apk((ApkTestBuilder.V2Id, ApkTestBuilder.SignerBlock(cert)));

    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static HttpResponseMessage Throw() => throw new HttpRequestException("no internet");

    private AgentApk Make(Func<HttpResponseMessage> respond, byte[] cert) =>
        new(new HttpClient(new FakeHandler(respond)), _folder, ApkTestBuilder.Checksum(cert));

    [Fact]
    public async Task Good_download_is_stored_and_ready()
    {
        var cert = ApkTestBuilder.NewCertificate("a");
        var apk = Apk(cert);
        var agent = Make(() => Ok(apk), cert);

        Assert.Equal(AgentApkResult.Ready, await agent.GetAsync(CancellationToken.None));
        Assert.Equal(Path.Combine(_folder, "MidviewAgent.apk"), agent.ApkPath);
        Assert.Equal(apk, File.ReadAllBytes(agent.ApkPath));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Offline_with_good_stored_copy_is_ready()
    {
        var cert = ApkTestBuilder.NewCertificate("a");
        var agent = Make(Throw, cert);
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(agent.ApkPath, Apk(cert));

        Assert.Equal(AgentApkResult.Ready, await agent.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Offline_without_stored_copy_is_offline()
    {
        var agent = Make(Throw, ApkTestBuilder.NewCertificate("a"));

        Assert.Equal(AgentApkResult.Offline, await agent.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Offline_with_bad_stored_copy_is_offline()
    {
        var cert = ApkTestBuilder.NewCertificate("a");
        var agent = Make(Throw, cert);
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(agent.ApkPath, Apk(ApkTestBuilder.NewCertificate("other")));

        Assert.Equal(AgentApkResult.Offline, await agent.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Download_with_other_certificate_is_invalid_and_stored_copy_stays()
    {
        var cert = ApkTestBuilder.NewCertificate("a");
        var stored = Apk(cert);
        var agent = Make(() => Ok(Apk(ApkTestBuilder.NewCertificate("evil"))), cert);
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(agent.ApkPath, stored);

        Assert.Equal(AgentApkResult.Invalid, await agent.GetAsync(CancellationToken.None));
        Assert.Equal(stored, File.ReadAllBytes(agent.ApkPath));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Http_error_counts_as_offline()
    {
        var cert = ApkTestBuilder.NewCertificate("a");
        var agent = Make(() => new HttpResponseMessage(HttpStatusCode.NotFound), cert);

        Assert.Equal(AgentApkResult.Offline, await agent.GetAsync(CancellationToken.None));
    }
}
