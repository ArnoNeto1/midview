using Midview.Master;
using Xunit;

public class PhoneSetupProgressTests
{
    private static readonly DateTimeOffset ReadyAt = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static int IndexOf(string id) =>
        PhoneSetupGuide.Steps.Select(step => step.Id).ToList().IndexOf(id);

    // On the number page with "07" typed.
    private static PhoneSetupProgress AtNumber(string typed = "7")
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: true, gathered: true);
        progress.SetNumber(typed);
        return progress;
    }

    // Through every step up to the cable (the last one of the guide).
    private static PhoneSetupProgress AtCable()
    {
        var progress = AtNumber();
        while (progress.StepId != "cabo")
        {
            progress.Next();
        }
        return progress;
    }

    // The cable saw the phone and the Midview is finishing it.
    private static PhoneSetupProgress Finishing()
    {
        var progress = AtCable();
        Assert.True(progress.CableSeen());
        return progress;
    }

    // Finished over the cable: the phone 07 is ready.
    private static PhoneSetupProgress ReadyFor07()
    {
        var progress = Finishing();
        progress.CablePrepared(ReadyAt);
        return progress;
    }

    [Fact]
    public void Without_wifi_it_starts_on_the_wifi_page()
    {
        var progress = new PhoneSetupProgress();

        progress.Start(hasWifi: false, gathered: false);

        Assert.Equal(SetupPage.Wifi, progress.Page);
        Assert.False(progress.ReturnsAfterWifi);
    }

    [Fact]
    public void The_first_time_the_wifi_page_leads_to_what_to_separate_and_then_the_number()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: false, gathered: false);

        progress.Next();
        Assert.Equal(SetupPage.Gather, progress.Page);
        Assert.Null(progress.Step);

        progress.Next();
        Assert.Equal("numero", progress.StepId);
    }

    [Fact]
    public void With_wifi_but_the_first_time_it_starts_on_what_to_separate()
    {
        var progress = new PhoneSetupProgress();

        progress.Start(hasWifi: true, gathered: false);

        Assert.Equal(SetupPage.Gather, progress.Page);
    }

    [Fact]
    public void With_wifi_and_already_separated_it_starts_on_the_number()
    {
        var progress = new PhoneSetupProgress();

        progress.Start(hasWifi: true, gathered: true);

        Assert.Equal(SetupPage.Step, progress.Page);
        Assert.Equal("numero", progress.StepId);
    }

    [Fact]
    public void Without_wifi_but_already_separated_the_wifi_page_leads_to_the_number()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: false, gathered: true);

        progress.Next();

        Assert.Equal("numero", progress.StepId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1234")]
    public void Next_does_not_leave_the_number_without_a_valid_one(string typed)
    {
        var progress = AtNumber(typed);

        progress.Next();

        Assert.Equal("numero", progress.StepId);
        Assert.Null(progress.Number);
    }

    [Fact]
    public void A_valid_number_is_kept_with_two_digits_and_leads_to_turning_the_phone_on()
    {
        var progress = AtNumber("7");

        progress.Next();

        Assert.Equal("07", progress.Number);
        Assert.Equal("Celular 07", progress.Name);
        Assert.Equal("ligar", progress.StepId);
    }

    [Fact]
    public void Back_returns_to_the_previous_step_and_stops_at_the_number()
    {
        var progress = AtNumber();
        progress.Next();

        progress.Back();
        Assert.Equal("numero", progress.StepId);
        progress.Back();
        Assert.Equal("numero", progress.StepId);
    }

    [Fact]
    public void Changing_the_wifi_comes_back_to_the_same_step()
    {
        var progress = AtNumber();
        progress.Next();
        progress.Next();

        progress.ChangeWifi();
        Assert.Equal(SetupPage.Wifi, progress.Page);
        Assert.True(progress.ReturnsAfterWifi);
        progress.Next();

        Assert.Equal("conta-google", progress.StepId);
    }

    [Fact]
    public void The_cable_step_waits_for_the_phone_on_the_cable()
    {
        var progress = AtCable();

        progress.Next();

        Assert.Equal("cabo", progress.StepId);
    }

    [Fact]
    public void Seeing_the_phone_on_the_cable_starts_finishing_it()
    {
        var progress = AtCable();

        Assert.True(progress.CableSeen());

        Assert.Equal(SetupPage.Cable, progress.Page);
        Assert.Null(progress.Stage);
        Assert.Equal("Celular 07", progress.Name);
    }

    [Fact]
    public void The_cable_only_counts_on_the_cable_step()
    {
        var progress = AtNumber();
        progress.Next();

        Assert.False(progress.CableSeen());

        Assert.Equal("ligar", progress.StepId);
    }

    [Fact]
    public void Each_stage_reached_is_kept_for_the_list()
    {
        var progress = Finishing();

        progress.CableStageReached(CableStage.Installing);
        Assert.Equal(CableStage.Installing, progress.Stage);
        progress.CableStageReached(CableStage.Owner);

        Assert.Equal(CableStage.Owner, progress.Stage);
    }

    [Fact]
    public void A_stage_outside_the_cable_page_changes_nothing()
    {
        var progress = AtCable();

        progress.CableStageReached(CableStage.Installing);

        Assert.Null(progress.Stage);
        Assert.Equal("cabo", progress.StepId);
    }

    [Fact]
    public void A_phone_with_an_account_says_so_and_goes_back_to_leaving_the_accounts()
    {
        var progress = Finishing();
        progress.CableStageReached(CableStage.Checking);

        progress.CableFailed(CableProblem.HasAccounts);

        Assert.Equal(SetupPage.CableFailed, progress.Page);
        Assert.Equal(CableProblem.HasAccounts, progress.Problem);
        Assert.Equal("Ainda há uma conta neste celular.", progress.ProblemText);
        Assert.Equal("sair-contas", progress.ProblemStepId);
        Assert.Equal(IndexOf("sair-contas") + 1, progress.ProblemStepNumber);
        progress.Back();
        Assert.Equal("sair-contas", progress.StepId);
    }

    [Fact]
    public void A_phone_too_old_offers_no_step_to_go_back_to()
    {
        var progress = Finishing();

        progress.CableFailed(CableProblem.TooOld);

        Assert.Equal(SetupPage.CableFailed, progress.Page);
        Assert.Null(progress.ProblemStepId);
        Assert.Null(progress.ProblemStepNumber);
        progress.Back();
        Assert.Equal(SetupPage.CableFailed, progress.Page);
    }

    [Fact]
    public void Trying_again_after_a_failure_finishes_over_the_cable_again()
    {
        var progress = Finishing();
        progress.CableStageReached(CableStage.Installing);
        progress.CableFailed(CableProblem.InstallFailed);

        progress.Next();

        Assert.Equal(SetupPage.Cable, progress.Page);
        Assert.Null(progress.Stage);
        Assert.Null(progress.Problem);
    }

    [Fact]
    public void A_failure_outside_the_cable_page_changes_nothing()
    {
        var progress = AtCable();

        progress.CableFailed(CableProblem.HasAccounts);

        Assert.Equal("cabo", progress.StepId);
    }

    [Fact]
    public void Prepared_over_the_cable_is_ready()
    {
        var progress = ReadyFor07();

        Assert.Equal(SetupPage.Ready, progress.Page);
        Assert.Equal(ReadyAt, progress.ReadyAt);
        Assert.False(progress.HasAppeared);
        Assert.Equal("Celular 07", progress.Name);
    }

    [Fact]
    public void Prepared_outside_the_cable_page_changes_nothing()
    {
        var progress = AtCable();

        progress.CablePrepared(ReadyAt);

        Assert.Equal("cabo", progress.StepId);
    }

    [Fact]
    public void Four_minutes_after_ready_it_still_waits()
    {
        var progress = ReadyFor07();

        progress.Tick(ReadyAt + TimeSpan.FromMinutes(4));

        Assert.Equal(SetupPage.Ready, progress.Page);
    }

    [Fact]
    public void Five_minutes_after_ready_without_the_phone_it_has_not_appeared()
    {
        var progress = ReadyFor07();

        progress.Tick(ReadyAt + TimeSpan.FromMinutes(5));

        Assert.Equal(SetupPage.NotAppeared, progress.Page);
    }

    [Fact]
    public void A_phone_that_appeared_never_turns_into_not_appeared()
    {
        var progress = ReadyFor07();

        progress.Appeared();
        progress.Tick(ReadyAt + TimeSpan.FromMinutes(30));

        Assert.Equal(SetupPage.Ready, progress.Page);
        Assert.True(progress.HasAppeared);
    }

    [Fact]
    public void A_phone_appearing_late_goes_back_to_ready()
    {
        var progress = ReadyFor07();
        progress.Tick(ReadyAt + TimeSpan.FromMinutes(6));

        progress.Appeared();

        Assert.Equal(SetupPage.Ready, progress.Page);
        Assert.True(progress.HasAppeared);
    }

    [Fact]
    public void Next_after_not_appeared_starts_the_next_phone_at_the_number()
    {
        var progress = ReadyFor07();
        progress.Tick(ReadyAt + TimeSpan.FromMinutes(6));

        progress.Next();

        Assert.Equal("numero", progress.StepId);
        Assert.Null(progress.Number);
        Assert.Null(progress.ReadyAt);
    }

    [Fact]
    public void Changing_the_wifi_on_the_last_step_comes_back_to_it()
    {
        var progress = AtCable();

        progress.ChangeWifi();
        progress.Next();

        Assert.Equal("cabo", progress.StepId);
    }

    [Fact]
    public void StartAt_goes_to_turning_the_phone_on_with_the_welcome_help()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: true, gathered: false);

        progress.StartAt("07");

        Assert.Equal("ligar", progress.StepId);
        Assert.Equal("07", progress.Number);
        Assert.True(progress.OpensWelcomeHelp);
        progress.Next();
        Assert.False(progress.OpensWelcomeHelp);
    }

    [Fact]
    public void A_number_already_in_the_class_asks_first()
    {
        var progress = AtNumber();

        progress.NumberTaken(online: false);

        Assert.Equal(SetupPage.ConfirmRedo, progress.Page);
        Assert.Equal("Já existe um Celular 07. Vai preparar de novo o 07?", progress.ConfirmRedoText);
    }

    [Fact]
    public void Confirming_an_online_phone_offers_the_wipe_on_turning_the_phone_on()
    {
        var progress = AtNumber();
        progress.NumberTaken(online: true);

        progress.Next();

        Assert.Equal("ligar", progress.StepId);
        Assert.True(progress.OffersWipe);
        Assert.False(progress.OpensWelcomeHelp);
    }

    [Fact]
    public void Confirming_a_phone_that_is_off_follows_the_normal_steps()
    {
        var progress = AtNumber();
        progress.NumberTaken(online: false);

        progress.Next();

        Assert.Equal("ligar", progress.StepId);
        Assert.False(progress.OffersWipe);
    }

    [Fact]
    public void Not_confirming_goes_back_to_the_number()
    {
        var progress = AtNumber();
        progress.NumberTaken(online: true);

        progress.Back();

        Assert.Equal("numero", progress.StepId);
    }

    [Fact]
    public void Again_starts_the_next_phone_at_the_number()
    {
        var progress = ReadyFor07();

        progress.Again();

        Assert.Equal("numero", progress.StepId);
        Assert.Null(progress.Number);
        Assert.False(progress.OffersWipe);
        Assert.Null(progress.Stage);
    }

    [Fact]
    public void Ready_text_counts_against_the_total()
    {
        var progress = ReadyFor07();

        Assert.Equal("Celular 07 pronto ✓ (3 de 11)", progress.ReadyText(phonesInClass: 3, total: 11));
    }

    [Fact]
    public void Ready_text_without_a_total_counts_the_phones_in_the_class()
    {
        var progress = ReadyFor07();

        Assert.Equal("Celular 07 pronto ✓ (3 celulares na turma)", progress.ReadyText(phonesInClass: 3, total: null));
        Assert.Equal("Celular 07 pronto ✓ (1 celular na turma)", progress.ReadyText(phonesInClass: 1, total: null));
    }

    [Fact]
    public void The_step_caption_counts_the_guide_steps()
    {
        var progress = AtNumber();
        progress.Next();

        Assert.Equal($"Passo 2 de {PhoneSetupGuide.Steps.Count}", progress.StepCaption);
        Assert.Equal(IndexOf("ligar"), progress.StepIndex);
    }

    // After "Sim, preparar de novo": on turning the phone on, with the wipe offered.
    private static PhoneSetupProgress OfferedWipe()
    {
        var progress = AtNumber();
        progress.NumberTaken(online: true);
        progress.Next();
        return progress;
    }

    [Fact]
    public void A_wipe_offered_is_not_started()
    {
        var progress = OfferedWipe();

        Assert.Equal(WipeState.Offered, progress.Wipe);
        Assert.Null(progress.WipeFailure);
    }

    [Fact]
    public void A_failed_wipe_keeps_its_reason_and_can_try_again()
    {
        var progress = OfferedWipe();
        progress.WipeStarted();
        Assert.Equal(WipeState.Wiping, progress.Wipe);

        progress.WipeFailed("o celular não respondeu");

        Assert.Equal(WipeState.Failed, progress.Wipe);
        Assert.Equal("o celular não respondeu", progress.WipeFailure);
        progress.WipeStarted();
        Assert.Null(progress.WipeFailure);
    }

    [Fact]
    public void A_wipe_that_worked_opens_the_welcome_help()
    {
        var progress = OfferedWipe();
        progress.WipeStarted();

        progress.WipeSucceeded();

        Assert.Equal("ligar", progress.StepId);
        Assert.True(progress.OpensWelcomeHelp);
    }

    [Fact]
    public void A_phone_restored_by_hand_after_a_failed_wipe_stays_on_turning_it_on_with_the_welcome_help()
    {
        var progress = OfferedWipe();
        progress.WipeStarted();
        progress.WipeFailed("sem resposta");

        progress.RestoredByHand();

        Assert.Equal("ligar", progress.StepId);
        Assert.True(progress.OpensWelcomeHelp);
        Assert.Equal(WipeState.None, progress.Wipe);
        Assert.Null(progress.WipeFailure);
    }

    [Fact]
    public void The_wipe_state_goes_with_the_phone()
    {
        var progress = OfferedWipe();
        progress.WipeStarted();
        progress.WipeSucceeded();

        progress.Again();
        Assert.Equal(WipeState.None, progress.Wipe);

        progress.StartAt("07");
        Assert.Equal(WipeState.None, progress.Wipe);
    }

    [Fact]
    public void Wipe_results_without_an_offer_change_nothing()
    {
        var progress = AtNumber();

        progress.WipeStarted();
        progress.WipeSucceeded();

        Assert.Equal(WipeState.None, progress.Wipe);
    }

    [Fact]
    public void Starting_at_a_phone_wiped_from_its_menu_shows_the_wipe_done()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: true, gathered: true);

        progress.StartAt("07", wiped: true);

        Assert.Equal("ligar", progress.StepId);
        Assert.Equal(WipeState.Done, progress.Wipe);
        Assert.Equal("Celular 07", progress.Name);
    }
}
