using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;

namespace Midview.Master;

public enum AgentApkResult
{
    Ready,
    Offline,
    Invalid,
}

// Keeps the Midview phone app in the Master's data folder: downloads it, and
// only trusts a file whose signing certificate is Midview's.
public sealed class AgentApk(HttpClient http, string folder, string expectedChecksum)
{
    public string ApkPath { get; } = Path.Combine(folder, "MidviewAgent.apk");

    public async Task<AgentApkResult> GetAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, "MidviewAgent." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            try
            {
                using var response = await http.GetAsync(PhoneProvisioning.ApkUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(temp);
                await source.CopyToAsync(target, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return StoredIsGood() ? AgentApkResult.Ready : AgentApkResult.Offline;
            }

            if (!Matches(temp))
            {
                return AgentApkResult.Invalid;
            }

            File.Move(temp, ApkPath, overwrite: true);
            return AgentApkResult.Ready;
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }
        }
    }

    private bool StoredIsGood() => File.Exists(ApkPath) && Matches(ApkPath);

    private bool Matches(string path) =>
        ApkSignature.SignerChecksum(path) is { } sum && sum == expectedChecksum;
}
