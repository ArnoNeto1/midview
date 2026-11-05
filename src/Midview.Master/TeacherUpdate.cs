using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Midview.Master;

// "Atualizar professor" (Máquinas da sala): this teacher PC's Midview updated
// to the newest version published on GitHub, without the teacher opening the
// site and downloading MidviewSetup.exe by hand. The students' machines are
// still updated apart, with "Atualizar as máquinas pela rede".
//
// The latest release is asked of GitHub's API; its MidviewSetup.exe is
// downloaded into this PC's Midview folder and checked against the SHA-256
// GitHub publishes for it. Then the Midview closes, and a hidden Windows
// PowerShell script (ApplyScript), which locks no file of the program, waits
// for it to be gone, checks the setup again while holding it open for reading
// only, runs it silently as "professor" (the setup asks Windows for
// administrator rights itself, so a refusal just ends it) and opens the
// Midview again with FinishedArgument, how the setup ended and the version.
public static class TeacherUpdate
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/ArnoNeto1/midview/releases/latest";
    public const string SetupAssetName = "MidviewSetup.exe";

    // What the script opens the Midview with once the setup has ended:
    // FinishedArgument, the setup's exit code (-1: the setup no longer matched
    // what was downloaded; -2: the script itself failed) and the version.
    public const string FinishedArgument = "--atualizacao-terminou";

    // Far above the setup's ~100 MB, so a wrong answer can't fill the disk.
    private const long MaxSetupBytes = 1L << 30;

    private const string UninstallKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F2B1C7E-4D3A-4B8E-9C61-2E7A5D0B9F14}_is1";

    private static readonly Regex Sha256Digest = new("^sha256:([0-9a-fA-F]{64})$", RegexOptions.CultureInvariant);

    // The version published on GitHub and its setup. Sha256: lower-case hex.
    public sealed record Release(Version Version, Uri SetupUrl, long Size, string Sha256);

    // The published version against this PC's.
    public enum Standing
    {
        Newer,
        Same,
        Older,
    }

    // Where the downloaded setup, the script and the setup's log go.
    public static string Folder => Path.Combine(MasterData.Folder, "atualizacao");

    // This program's version (Directory.Build.props), as in its properties.
    public static Version CurrentVersion =>
        ParseVersion(typeof(TeacherUpdate).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? typeof(TeacherUpdate).Assembly.GetName().Version
        ?? new Version(0, 0, 0);

    public static Standing Compare(Version current, Version published) =>
        published > current ? Standing.Newer : published == current ? Standing.Same : Standing.Older;

    // GitHub refuses requests that don't name their program.
    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Midview", CurrentVersion.ToString()));
        return client;
    }

    // InvalidDataException with why, in Portuguese, for an answer that isn't
    // a release to update to; HttpRequestException for the network.
    public static async Task<Release> FetchLatestAsync(HttpClient client, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidDataException("nenhuma versão publicada no GitHub");
        }
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(cancellation));
    }

    // The release's version (its tag, "v0.0.38") and its MidviewSetup.exe,
    // which must come from GitHub over HTTPS with a SHA-256 to check it by.
    public static Release ParseRelease(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString();
            var version = ParseVersion(tag)
                ?? throw new InvalidDataException($"a versão publicada \"{tag}\" não é um número de versão");

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != SetupAssetName)
                {
                    continue;
                }

                var url = asset.GetProperty("browser_download_url").GetString();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var setupUrl) || setupUrl.Scheme != Uri.UriSchemeHttps
                    || !string.Equals(setupUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"o {SetupAssetName} da versão {version} não está no GitHub");
                }
                var size = asset.GetProperty("size").GetInt64();
                if (size <= 0 || size > MaxSetupBytes)
                {
                    throw new InvalidDataException($"o {SetupAssetName} da versão {version} tem um tamanho inválido");
                }
                var digest = asset.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String
                    ? Sha256Digest.Match(value.GetString()!)
                    : Match.Empty;
                if (!digest.Success)
                {
                    throw new InvalidDataException(
                        $"o GitHub não informou a soma de verificação do {SetupAssetName} da versão {version}");
                }
                return new Release(version, setupUrl, size, digest.Groups[1].Value.ToLowerInvariant());
            }
            throw new InvalidDataException($"a versão {version} no GitHub não tem o {SetupAssetName}");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("a resposta do GitHub não pôde ser lida", ex);
        }
    }

    // Downloads the release's setup into folder (an earlier one left there
    // goes) and returns where it is. Only a setup of the published size and
    // SHA-256 is left there; anything else is deleted, with
    // InvalidDataException. percent: 0 to 100 as it comes.
    public static async Task<string> DownloadAsync(
        HttpClient client, Release release, string folder, IProgress<int>? percent, CancellationToken cancellation)
    {
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.EnumerateFiles(folder, "MidviewSetup-*"))
        {
            TryDelete(old);
        }

        var path = Path.Combine(folder, $"MidviewSetup-{release.Version}.exe");
        var partial = path + ".part";
        try
        {
            using (var response = await client.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancellation))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellation);
                await using var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None);
                using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long total = 0;
                var reported = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    total += read;
                    if (total > release.Size)
                    {
                        throw new InvalidDataException("o arquivo baixado é maior que o publicado no GitHub");
                    }
                    sha256.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    var now = (int)(total * 100 / release.Size);
                    if (now != reported)
                    {
                        reported = now;
                        percent?.Report(now);
                    }
                }
                if (total != release.Size)
                {
                    throw new InvalidDataException("o download veio incompleto");
                }
                if (!string.Equals(Convert.ToHexString(sha256.GetHashAndReset()), release.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("o arquivo baixado não confere com o publicado no GitHub");
                }
            }
            File.Move(partial, path, overwrite: true);
            return path;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    // The installed teacher program the setup replaces, from what the setup
    // wrote in the registry, or null and why this copy can't be updated so.
    public static string? InstalledProgram(out string problem)
    {
        string? kind = null;
        string? location = null;
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using (var midview = machine.OpenSubKey(@"Software\Midview"))
            {
                kind = midview?.GetValue("Tipo") as string;
            }
            using (var uninstall = machine.OpenSubKey(UninstallKey))
            {
                location = uninstall?.GetValue("InstallLocation") as string;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Read as not installed.
        }
        return InstalledProgram(kind, location, Environment.ProcessPath ?? string.Empty, out problem);
    }

    // kind and location: what the setup wrote (HKLM Software\Midview "Tipo",
    // its uninstall entry's InstallLocation). runningExe: this program.
    public static string? InstalledProgram(string? kind, string? location, string runningExe, out string problem)
    {
        if (kind != "professor" || string.IsNullOrWhiteSpace(location))
        {
            problem = "Este PC não tem o Midview Professor instalado pelo MidviewSetup: instale-o pelo MidviewSetup para poder atualizar daqui.";
            return null;
        }

        var installed = Path.TrimEndingDirectorySeparator(location.Trim());
        if (!string.Equals(Path.GetDirectoryName(runningExe), installed, StringComparison.OrdinalIgnoreCase))
        {
            problem = $"Esta cópia do Midview não é a instalada neste PC, que fica em {installed}: atualize por ela.";
            return null;
        }

        problem = string.Empty;
        return Path.Combine(location.Trim(), "Midview.Master.exe");
    }

    // Writes ApplyScript into folder, where it is run from.
    public static string WriteApplyScript(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "aplicar-atualizacao.ps1");
        // With a BOM, as Windows PowerShell reads a file without one as ANSI.
        File.WriteAllText(path, ApplyScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    // Windows PowerShell, hidden, running the script for that setup. waitFor:
    // this Midview's process, which must be gone before the setup runs.
    public static ProcessStartInfo ApplyStartInfo(
        string script, string setup, string sha256, int waitFor, string program, string log, Version version)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                     "-Instalador", setup, "-Sha256", sha256, "-EsperarPid", waitFor.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "-Programa", program, "-Registro", log, "-Versao", version.ToString(),
                 })
        {
            info.ArgumentList.Add(argument);
        }
        return info;
    }

    // The setup's log for that version, in folder.
    public static string LogPath(string folder, Version version) => Path.Combine(folder, $"instalacao-{version}.log");

    // What to tell the teacher when the script opened the Midview again
    // (FinishedArgument code version), or null for arguments that aren't that.
    public static string? FinishedNotice(string code, string versionText, Version current, string folder)
    {
        if (!int.TryParse(code, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var exitCode)
            || ParseVersion(versionText) is not { } version)
        {
            return null;
        }

        return exitCode switch
        {
            0 when current >= version => $"O Midview deste PC foi atualizado para a versão {version}.",
            -1 => $"A atualização para a versão {version} não foi feita: o instalador baixado não confere mais com o publicado no GitHub. " +
                "Tente de novo em Máquinas da sala.",
            _ => $"A atualização para a versão {version} não foi feita (código {exitCode}); o Midview continua na versão {current}. " +
                "Se o Windows pediu permissão de administrador e ela foi recusada, é só tentar de novo em Máquinas da sala. " +
                $"O registro da instalação fica em {LogPath(folder, version)}.",
        };
    }

    // Did the script's run (FinishedArgument code version) put that version
    // on this PC? Only then is the teacher asked about the students' machines,
    // which get what this PC now carries.
    public static bool FinishedWell(string code, string versionText, Version current) =>
        int.TryParse(code, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var exitCode)
        && exitCode == 0
        && ParseVersion(versionText) is { } version
        && current >= version;

    // What the teacher is asked when the Midview opens and GitHub has a newer
    // version: the same warnings as the button in Máquinas da sala, since
    // answering yes goes straight to the download.
    public static string NewVersionQuestion(Version current, Version available) =>
        $"Chegou uma versão nova do Midview: {available} (este PC está na {current}).\n\n" +
        "Para instalar, o Midview fecha e abre de novo sozinho, o que leva 1 ou 2 minutos. " +
        "Enquanto isso, as telas bloqueadas dos alunos são liberadas. O Windows vai pedir permissão de administrador.\n\n" +
        "Deseja atualizar agora?";

    // "v0.0.38", "0.0.38" or "0.0.38+abc" as a version; null for anything else.
    private static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var trimmed = text.Trim().TrimStart('v', 'V');
        var plus = trimmed.IndexOf('+');
        if (plus >= 0)
        {
            trimmed = trimmed[..plus];
        }
        return Version.TryParse(trimmed, out var version) && version.Build >= 0 ? version : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left; the next download clears it.
        }
    }

    // Kept to ASCII: Windows PowerShell's parser takes no chances with the rest.
    private const string ApplyScript = """
        # Written by the teacher's Midview ("Atualizar professor", TeacherUpdate.cs)
        # and run hidden as the teacher once that Midview is closing: waits for it
        # to be gone, checks the downloaded setup again, runs it silently (the
        # setup asks Windows for administrator rights itself) and opens the
        # Midview again with how the setup ended. The setup is held open for
        # reading only from the check to its end, so nothing replaces it between.
        param(
            [Parameter(Mandatory = $true)][string] $Instalador,
            [Parameter(Mandatory = $true)][string] $Sha256,
            [Parameter(Mandatory = $true)][int] $EsperarPid,
            [Parameter(Mandatory = $true)][string] $Programa,
            [Parameter(Mandatory = $true)][string] $Registro,
            [Parameter(Mandatory = $true)][string] $Versao
        )

        $ErrorActionPreference = 'Stop'

        # The setup refuses to replace a Midview that is still open. One that
        # doesn't close is left as it is: nothing to install, nothing to open.
        $aberto = Get-Process -Id $EsperarPid -ErrorAction SilentlyContinue
        if ($aberto -and -not $aberto.WaitForExit(120000)) {
            exit 2
        }

        $codigo = -2
        try {
            $arquivo = [IO.File]::Open($Instalador, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            try {
                $soma = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($arquivo)).Replace('-', '')
                if ($soma -ne $Sha256) {
                    $codigo = -1
                } else {
                    $setup = Start-Process -FilePath $Instalador -PassThru `
                        -ArgumentList ('/SILENT /SUPPRESSMSGBOXES /NORESTART /TIPO=professor /LOG="' + $Registro + '"')
                    $null = $setup.Handle
                    $setup.WaitForExit()
                    $codigo = $setup.ExitCode
                }
            } finally {
                $arquivo.Dispose()
            }
        } catch {
            $codigo = -2
        }

        Remove-Item -LiteralPath $Instalador -ErrorAction SilentlyContinue
        Start-Process -FilePath $Programa -ArgumentList ('--atualizacao-terminou ' + $codigo + ' ' + $Versao)
        """;
}
