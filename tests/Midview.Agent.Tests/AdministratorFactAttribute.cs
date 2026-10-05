using System.Security.Principal;
using Xunit;

// For a test that creates the Agent's folders the way the service does: shut
// to everyone but SYSTEM and Administrators, so only an administrator can
// still write into them (and delete them) afterwards. Without administrator
// rights -- a terminal not opened as administrator, where UAC strips them --
// the test is reported as skipped, saying why, instead of failing on "access
// denied". On GitHub Actions the runner always is an administrator, so there
// it's never skipped: a run that lost those rights must fail, not quietly
// stop checking.
public sealed class AdministratorFactAttribute : FactAttribute
{
    public AdministratorFactAttribute() => Skip = AdministratorRights.SkipReason;
}

public sealed class AdministratorTheoryAttribute : TheoryAttribute
{
    public AdministratorTheoryAttribute() => Skip = AdministratorRights.SkipReason;
}

internal static class AdministratorRights
{
    public static string? SkipReason =>
        !OperatingSystem.IsWindows()
        || new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)
        || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
            ? null
            : "precisa de direitos de administrador: rode os testes num terminal aberto como administrador";
}
