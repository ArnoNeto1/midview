using Midview.Master;
using Xunit;

public class PhoneSetupProgressTests
{
    private static int IndexOf(string id) =>
        PhoneSetupGuide.Steps.Select(step => step.Id).ToList().IndexOf(id);

    // On the number page with "07" typed.
    private static PhoneSetupProgress AtNumber(string typed = "7")
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: true);
        progress.SetNumber(typed);
        return progress;
    }

    // Through every step up to the six taps (the last one of the guide).
    private static PhoneSetupProgress AtSixTaps()
    {
        var progress = AtNumber();
        while (progress.StepId != "cabo")
        {
            progress.Next();
        }
        return progress;
    }

    // Past the last step: the phone 07 is ready.
    private static PhoneSetupProgress ReadyFor07()
    {
        var progress = AtSixTaps();
        progress.Next();
        return progress;
    }

    [Fact]
    public void Without_wifi_it_starts_on_the_wifi_page()
    {
        var progress = new PhoneSetupProgress();

        progress.Start(hasWifi: false);

        Assert.Equal(SetupPage.Wifi, progress.Page);
        Assert.False(progress.ReturnsAfterWifi);
    }

    [Fact]
    public void With_wifi_it_starts_on_the_number()
    {
        var progress = new PhoneSetupProgress();

        progress.Start(hasWifi: true);

        Assert.Equal(SetupPage.Step, progress.Page);
        Assert.Equal("numero", progress.StepId);
    }

    [Fact]
    public void The_wifi_page_leads_to_the_number()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: false);

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
    public void A_valid_number_is_kept_with_two_digits_and_leads_to_the_battery()
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
    public void Next_on_the_last_step_of_the_guide_is_ready()
    {
        var progress = AtSixTaps();

        progress.Next();

        Assert.Equal(SetupPage.Ready, progress.Page);
        Assert.Equal("Celular 07", progress.Name);
    }

    [Fact]
    public void Changing_the_wifi_on_the_last_step_comes_back_to_it()
    {
        var progress = AtSixTaps();

        progress.ChangeWifi();
        progress.Next();

        Assert.Equal("cabo", progress.StepId);
    }

    [Fact]
    public void StartAt_goes_straight_to_the_six_taps()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: true);

        progress.StartAt("07");

        Assert.Equal("cabo", progress.StepId);
        Assert.Equal("07", progress.Number);
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
    public void Confirming_an_online_phone_offers_the_wipe_and_goes_to_the_six_taps()
    {
        var progress = AtNumber();
        progress.NumberTaken(online: true);

        progress.Next();

        Assert.Equal("cabo", progress.StepId);
        Assert.True(progress.OffersWipe);
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

    // After "Sim, preparar de novo": on the six taps with the wipe offered.
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
    public void A_phone_restored_by_hand_after_a_failed_wipe_stays_on_the_six_taps()
    {
        var progress = OfferedWipe();
        progress.WipeStarted();
        progress.WipeFailed("sem resposta");

        progress.RestoredByHand();

        Assert.Equal("cabo", progress.StepId);
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
        progress.Start(hasWifi: true);

        progress.StartAt("07", wiped: true);

        Assert.Equal("cabo", progress.StepId);
        Assert.Equal(WipeState.Done, progress.Wipe);
        Assert.Equal("Celular 07", progress.Name);
    }
}
