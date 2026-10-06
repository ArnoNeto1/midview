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

// The other way round: for a test of what a student, who isn't an
// administrator, runs into. An administrator can always get past it, so run
// as one (as on GitHub Actions) it's skipped, saying why.
public sealed class NotAdministratorFactAttribute : FactAttribute
{
    public NotAdministratorFactAttribute() =>
        Skip = AdministratorRights.IsAdministrator
            ? "verifica o que um aluno sem administrador vê: rode os testes num terminal comum"
            : null;
}

internal static class AdministratorRights
{
    public static bool IsAdministrator =>
        OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static string? SkipReason =>
        !OperatingSystem.IsWindows()
        || IsAdministrator
        || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
            ? null
            : "precisa de direitos de administrador: rode os testes num terminal aberto como administrador";
}
