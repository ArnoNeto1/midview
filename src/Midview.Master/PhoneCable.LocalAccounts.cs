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

    // "ServiceInfo: AuthenticatorDescription {type=X}, ComponentInfo{package/class}, uid N"
    private static readonly Regex Authenticator = new(
        @"AuthenticatorDescription \{type=([\w.]+)\}, ComponentInfo\{([\w.]+)/", RegexOptions.Compiled);

    // The apps to turn off around the Owner step (set by Check).
    private IReadOnlyList<string> _localAuthenticators = Array.Empty<string>();

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
    // them has no app listed (it could not be hidden).
    internal static IReadOnlyList<string>? LocalAuthenticators(string dumpsys)
    {
        var owners = Authenticator.Matches(dumpsys)
            .GroupBy(m => m.Groups[1].Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Groups[2].Value, StringComparer.Ordinal);
        var packages = new List<string>();
        foreach (var type in MiSignOut.AccountTypes(dumpsys).Where(IsLocalAccount).Distinct())
        {
            if (!owners.TryGetValue(type, out var package) || package == "android")
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

    // Runs the Owner step with the local accounts' apps turned off.
    private AdbOutput WithLocalAccountsHidden(Func<AdbOutput> owner)
    {
        if (_localAuthenticators.Count == 0)
        {
            return owner();
        }
        try
        {
            foreach (var package in _localAuthenticators)
            {
                Shell(Short, "pm", "disable-user", "--user", "0", package);
            }
            return owner();
        }
        finally
        {
            foreach (var package in _localAuthenticators)
            {
                Shell(Short, "pm", "enable", package);
            }
        }
    }
}
