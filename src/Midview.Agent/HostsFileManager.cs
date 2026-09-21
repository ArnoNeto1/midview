using System.Diagnostics;

namespace Midview.Agent;

public static class HostsFileManager
{
    private const string BeginMarker = "# BEGIN MIDVIEW SITE BLOCK";
    private const string EndMarker = "# END MIDVIEW SITE BLOCK";

    public static string Reconcile(string existingContent, IEnumerable<string> domains)
    {
        var lines = existingContent.Split('\n');
        var kept = new List<string>();
        var insideBlock = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed == BeginMarker)
            {
                insideBlock = true;
                continue;
            }
            if (trimmed == EndMarker)
            {
                insideBlock = false;
                continue;
            }
            if (!insideBlock)
            {
                kept.Add(line);
            }
        }

        while (kept.Count > 0 && kept[^1].Trim().Length == 0)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        var allDomains = ExpandBareAndWww(domains);

        if (allDomains.Count == 0)
        {
            return string.Join('\n', kept) + "\n";
        }

        var blockLines = new List<string> { BeginMarker };
        foreach (var domain in allDomains.OrderBy(d => d, StringComparer.Ordinal))
        {
            blockLines.Add($"127.0.0.1 {domain}");
        }
        blockLines.Add(EndMarker);

        return string.Join('\n', kept) + "\n" + string.Join('\n', blockLines) + "\n";
    }

    public static HashSet<string> ExpandBareAndWww(IEnumerable<string> domains)
    {
        var expanded = new HashSet<string>();
        foreach (var domain in domains)
        {
            expanded.Add(domain);
            expanded.Add(domain.StartsWith("www.", StringComparison.Ordinal)
                ? domain[4..]
                : "www." + domain);
        }
        return expanded;
    }

    public static void ApplyToFile(string hostsFilePath, IEnumerable<string> domains)
    {
        var existingContent = File.Exists(hostsFilePath) ? File.ReadAllText(hostsFilePath) : string.Empty;
        var newContent = Reconcile(existingContent, domains);
        File.WriteAllText(hostsFilePath, newContent);

        using var flushProcess = Process.Start(new ProcessStartInfo("ipconfig.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/flushdns" },
        });
        flushProcess?.WaitForExit();
    }
}
