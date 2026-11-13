using Midview.Master;
using Xunit;

// The school's Mi account in the assistant: asked once before the step that
// signs in with it, "trocar" from that step, and the end of the account done
// by hand once the cable could not.
public class PhoneSetupProgressMiTests
{
    private static PhoneSetupProgress AtStep(string id, bool asks)
    {
        var progress = new PhoneSetupProgress { AsksMiAccount = asks };
        progress.Start(hasWifi: true, gathered: true);
        progress.SetNumber("7");
        while (progress.Page == SetupPage.Step && progress.StepId != id)
        {
            progress.Next();
        }
        return progress;
    }

    [Fact]
    public void With_nothing_saved_the_page_comes_once_before_the_mi_step()
    {
        var progress = AtStep("desenvolvedor", asks: true);

        progress.Next();
        Assert.Equal(SetupPage.MiAccount, progress.Page);

        // Back: to the step before; Next again goes straight to the Mi step.
        progress.Back();
        Assert.Equal("desenvolvedor", progress.StepId);
        progress.Next();
        Assert.Equal("chip-conta-mi", progress.StepId);
    }

    [Fact]
    public void Saved_or_skipped_the_page_goes_on_to_the_mi_step()
    {
        var progress = AtStep("desenvolvedor", asks: true);
        progress.Next();

        progress.Next();

        Assert.Equal("chip-conta-mi", progress.StepId);
    }

    [Fact]
    public void With_an_account_saved_the_page_is_not_asked()
    {
        var progress = AtStep("desenvolvedor", asks: false);

        progress.Next();

        Assert.Equal("chip-conta-mi", progress.StepId);
    }

    [Fact]
    public void Change_from_the_step_comes_back_to_it_both_ways()
    {
        var progress = AtStep("chip-conta-mi", asks: false);

        progress.ChangeMiAccount();
        Assert.Equal(SetupPage.MiAccount, progress.Page);
        progress.Back();
        Assert.Equal("chip-conta-mi", progress.StepId);

        progress.ChangeMiAccount();
        progress.Next();
        Assert.Equal("chip-conta-mi", progress.StepId);
    }

    [Theory]
    [InlineData(CableProblem.HasAccounts, true)]
    [InlineData(CableProblem.MiSignOutFailed, true)]
    [InlineData(CableProblem.InstallDeclined, false)]
    public void An_account_the_cable_left_is_removed_by_hand_until_the_next_phone(CableProblem problem, bool byHand)
    {
        var progress = AtStep("cabo", asks: false);
        progress.CableSeen();

        progress.CableFailed(problem);
        Assert.Equal(byHand, progress.SignOutByHand);

        // "Tentar de novo" keeps it; the next phone starts over.
        progress.Next();
        Assert.Equal(byHand, progress.SignOutByHand);
        progress.Again();
        Assert.False(progress.SignOutByHand);
    }

    [Fact]
    public void The_sign_out_item_shows_only_once_reached()
    {
        var progress = AtStep("cabo", asks: false);
        progress.CableSeen();
        progress.CableStageReached(CableStage.Checking);
        Assert.False(progress.MiSignOutShown);

        progress.CableStageReached(CableStage.MiSignOut);
        progress.CableStageReached(CableStage.Installing);
        Assert.True(progress.MiSignOutShown);
    }

    [Fact]
    public void The_chip_only_step_replaces_leaving_the_accounts_only_when_the_cable_signs_out()
    {
        var leave = PhoneSetupGuide.Steps.Single(s => s.Id == "sair-contas");
        Assert.Same(PhoneSetupGuide.ChipOnly, PhoneSetupGuide.StepFor(leave, miAuto: true));
        Assert.Same(leave, PhoneSetupGuide.StepFor(leave, miAuto: false));
        var mi = PhoneSetupGuide.Steps.Single(s => s.Id == "chip-conta-mi");
        Assert.Same(mi, PhoneSetupGuide.StepFor(mi, miAuto: true));
        Assert.Contains(PhoneSetupGuide.ChipOnly.Taps, t => t.Contains("tire o chip"));
        Assert.Contains(leave.Taps, t => t.Contains("tire o chip"));
    }

    [Fact]
    public void The_mi_step_has_no_google_sign_in_left()
    {
        var mi = PhoneSetupGuide.Steps.Single(s => s.Id == "chip-conta-mi");
        Assert.DoesNotContain(mi.Taps, t => t.Contains("conta Google da escola"));
        Assert.Contains(mi.Taps, t => t.Contains("conta Mi da escola"));
        Assert.Contains("account.xiaomi.com", PhoneSetupGuide.MiAccountCreate);
        Assert.Contains("SMS", PhoneSetupGuide.MiAccountCreate);
    }
}
