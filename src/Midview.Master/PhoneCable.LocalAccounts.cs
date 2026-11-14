using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Midview.Master;

// Accounts some phones and tablets keep for their own contacts and SIM, with
// nothing online behind them and nobody to sign out of: the school's
// Multilaser M10 (Unisoc) has "Phone" (sprd.com.android.account.phone) from
// its Contacts app, which cannot be removed. They still make
// "dpm set-device-owner" refuse ("already some accounts"), so right before it
// the apps they belong to are turned off, which hides their accounts (the
// accounts themselves stay), and turned back on afterwards, whatever happened.
// Only an app on HideablePackages is ever turned off, only when it was on,
// and the ones turned off are written down (CableReenableFile) until they are
// on again: a cable that drops in between gets them back on at the next Check.
public sealed partial class PhoneCable
{
    // Only the sprd ones were seen on a device (the M10); the others name the
    // same local contacts on other vendors' builds.
    private static readonly HashSet<string> LocalAccountTypes = new(StringComparer.Ordinal)
    {
        "sprd.com.android.account.phone",
        "sprd.com.android.account.sim",
        "sprd.com.android.account.usim",
        "com.android.localphone",
        "com.android.sim",
    };

    // The apps that may be turned off for a moment: the Contacts app, which
    // owns the M10's local accounts and which the device does without for
    // those seconds. Never the phone app (com.android.phone: calls and the
    // SIM) or a system provider: a local account owned by anything else stops
    // the preparation instead (the teacher's to look at).
    private static readonly HashSet<string> HideablePackages = new(StringComparer.Ordinal)
    {
        "com.android.contacts",
    };

    // "ServiceInfo: AuthenticatorDescription {type=X}, ComponentInfo{package/class}, uid N"
    private static readonly Regex Authenticator = new(
        @"AuthenticatorDescription \{type=([\w.]+)\}, ComponentInfo\{([\w.]+)/", RegexOptions.Compiled);

    // The apps to turn off around the Owner step (set by Check).
    private IReadOnlyList<string> _localAuthenticators = Array.Empty<string>();

    // Where the apps turned off are written down until they are on again;
    // null: nowhere.
    public CableReenableFile? Reenable { get; init; }

    internal static bool IsLocalAccount(string type) => LocalAccountTypes.Contains(type);

    // The accounts that stop the preparation: any but the local ones. Accounts
    // counted but not listed by type stop it too.
    private static bool BlockingAccounts(string dumpsys)
    {
        if (!AnyAccount(dumpsys))
        {
            return false;
        }
        var types = MiSignOut.AccountTypes(dumpsys);
        return types.Count == 0 || types.Any(t => !IsLocalAccount(t));
    }

    // The apps the local accounts on the phone belong to, or null when one of
    // them has no app listed, or one that may not be turned off (it could not
    // be hidden).
    internal static IReadOnlyList<string>? LocalAuthenticators(string dumpsys)
    {
        var owners = Authenticator.Matches(dumpsys)
            .GroupBy(m => m.Groups[1].Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Groups[2].Value, StringComparer.Ordinal);
        var packages = new List<string>();
        foreach (var type in MiSignOut.AccountTypes(dumpsys).Where(IsLocalAccount).Distinct())
        {
            if (!owners.TryGetValue(type, out var package) || !HideablePackages.Contains(package))
            {
                return null;
            }
            if (!packages.Contains(package))
            {
                packages.Add(package);
            }
        }
        return packages;
    }

    // At each Check: the apps an earlier attempt turned off on this device
    // and could not turn back on (the cable dropped) are turned on now.
    private void ReenableLeftOff()
    {
        if (Reenable is null || _serial is null)
        {
            return;
        }
        var left = Reenable.For(_serial);
        if (left.Count > 0)
        {
            Reenable.Set(_serial, TurnOn(left));
        }
    }

    // Turns the apps on; returns the ones the device did not say are on.
    private List<string> TurnOn(IEnumerable<string> packages)
    {
        var still = new List<string>();
        foreach (var package in packages)
        {
            // "Package com.android.contacts new state: enabled"
            if (!Shell(Short, "pm", "enable", package).Text.Contains("new state: enabled", StringComparison.Ordinal))
            {
                still.Add(package);
            }
        }
        return still;
    }

    // The packages turned off on the device now ("pm list packages -d").
    private HashSet<string> DisabledPackages() =>
        Shell(Short, "pm", "list", "packages", "-d").Text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("package:", StringComparison.Ordinal))
            .Select(line => line["package:".Length..])
            .ToHashSet(StringComparer.Ordinal);

    // Runs the Owner step with the local accounts' apps turned off. An app
    // already off is left as it is: only the ones turned off here are turned
    // back on.
    private AdbOutput WithLocalAccountsHidden(Func<AdbOutput> owner)
    {
        if (_localAuthenticators.Count == 0)
        {
            return owner();
        }
        var off = DisabledPackages();
        var turnedOff = _localAuthenticators.Where(p => !off.Contains(p)).ToList();
        if (turnedOff.Count == 0)
        {
            return owner();
        }
        Reenable?.Set(_serial!, turnedOff);
        try
        {
            foreach (var package in turnedOff)
            {
                Shell(Short, "pm", "disable-user", "--user", "0", package);
            }
            return owner();
        }
        finally
        {
            // Turned on whether or not it is written down.
            var still = TurnOn(turnedOff);
            Reenable?.Set(_serial!, still);
        }
    }
}
