using Midview.Master;
using Xunit;

public class PhoneSetupProgressTests
{
    private static readonly DateTimeOffset Shown = new(2026, 11, 11, 14, 0, 0, TimeSpan.Zero);

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

    // Through every step up to the six taps, with the QR shown.
    private static PhoneSetupProgress WaitingFor07()
    {
        var progress = AtNumber();
        while (progress.StepId != "seis-toques")
        {
            progress.Next();
        }
        progress.QrShown(Shown);
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
        Assert.Equal("bateria", progress.StepId);
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

        Assert.Equal("conta-mi", progress.StepId);
    }

    // "Apareceu um erro no celular?" on the waiting page: no need to wait 15 minutes.
    [Fact]
    public void Waiting_can_go_to_not_appeared_right_away()
    {
        var progress = WaitingFor07();

        progress.NotAppearedNow();

        Assert.Equal(SetupPage.NotAppeared, progress.Page);
    }

    [Fact]
    public void Not_appeared_now_does_nothing_outside_the_wait()
    {
        var progress = AtNumber();

        progress.NotAppearedNow();

        Assert.Equal("numero", progress.StepId);
    }

    // The Wi-Fi changed after the QR: back to the six taps, for a new QR.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Changing_the_wifi_while_waiting_comes_back_to_the_six_taps(bool late)
    {
        var progress = WaitingFor07();
        if (late)
        {
            progress.Tick(Shown + TimeSpan.FromMinutes(15));
        }

        progress.ChangeWifi();
        progress.Next();

        Assert.Equal("seis-toques", progress.StepId);
    }

    [Fact]
    public void The_six_taps_need_the_qr_before_waiting()
    {
        var progress = AtNumber();
        while (progress.StepId != "seis-toques")
        {
            progress.Next();
        }

        progress.Next();
        Assert.Equal("seis-toques", progress.StepId);

        progress.QrShown(Shown);
        progress.Next();
        Assert.Equal(SetupPage.Waiting, progress.Page);
    }

    [Fact]
    public void Tick_before_15_minutes_keeps_waiting()
    {
        var progress = WaitingFor07();

        progress.Tick(Shown + TimeSpan.FromMinutes(14));

        Assert.Equal(SetupPage.Waiting, progress.Page);
    }

    [Fact]
    public void Tick_after_15_minutes_says_it_has_not_appeared()
    {
        var progress = WaitingFor07();

        progress.Tick(Shown + TimeSpan.FromMinutes(15));

        Assert.Equal(SetupPage.NotAppeared, progress.Page);
    }

    [Fact]
    public void Another_phone_joining_changes_nothing()
    {
        var progress = WaitingFor07();

        Assert.False(progress.Joined("Celular 08"));

        Assert.Equal(SetupPage.Waiting, progress.Page);
    }

    [Fact]
    public void The_phone_being_prepared_joining_makes_it_ready()
    {
        var progress = WaitingFor07();

        Assert.True(progress.Joined("Celular 07"));

        Assert.Equal(SetupPage.Ready, progress.Page);
    }

    [Fact]
    public void It_is_ready_even_from_the_not_appeared_page_or_the_qr()
    {
        var late = WaitingFor07();
        late.Tick(Shown + TimeSpan.FromMinutes(20));
        Assert.True(late.Joined("Celular 07"));
        Assert.Equal(SetupPage.Ready, late.Page);

        var onQr = WaitingFor07();
        onQr.Back();
        Assert.True(onQr.Joined("Celular 07"));
        Assert.Equal(SetupPage.Ready, onQr.Page);
    }

    [Fact]
    public void A_phone_with_the_same_name_before_any_qr_is_not_this_one()
    {
        var progress = AtNumber();
        progress.Next();

        Assert.False(progress.Joined("Celular 07"));
        Assert.Equal("bateria", progress.StepId);
    }

    [Fact]
    public void StartAt_goes_straight_to_the_six_taps()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: true);

        progress.StartAt("07");

        Assert.Equal("seis-toques", progress.StepId);
        Assert.Equal("07", progress.Number);
        Assert.Null(progress.QrShownAt);
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

        Assert.Equal("seis-toques", progress.StepId);
        Assert.True(progress.OffersWipe);
    }

    [Fact]
    public void Confirming_a_phone_that_is_off_follows_the_normal_steps()
    {
        var progress = AtNumber();
        progress.NumberTaken(online: false);

        progress.Next();

        Assert.Equal("bateria", progress.StepId);
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
    public void Restart_goes_back_to_the_reset_with_a_new_qr_to_come()
    {
        var progress = WaitingFor07();
        progress.Tick(Shown + TimeSpan.FromMinutes(15));

        progress.Restart();

        Assert.Equal("restaurar", progress.StepId);
        Assert.Equal("07", progress.Number);
        Assert.Null(progress.QrShownAt);
    }

    [Fact]
    public void Again_starts_the_next_phone_at_the_number()
    {
        var progress = WaitingFor07();
        progress.Joined("Celular 07");

        progress.Again();

        Assert.Equal("numero", progress.StepId);
        Assert.Null(progress.Number);
        Assert.Null(progress.QrShownAt);
        Assert.False(progress.OffersWipe);
    }

    [Fact]
    public void Ready_text_counts_against_the_total()
    {
        var progress = WaitingFor07();
        progress.Joined("Celular 07");

        Assert.Equal("Celular 07 pronto ✓ (3 de 11)", progress.ReadyText(phonesInClass: 3, total: 11));
    }

    [Fact]
    public void Ready_text_without_a_total_counts_the_phones_in_the_class()
    {
        var progress = WaitingFor07();
        progress.Joined("Celular 07");

        Assert.Equal("Celular 07 pronto ✓ (3 celulares na turma)", progress.ReadyText(phonesInClass: 3, total: null));
        Assert.Equal("Celular 07 pronto ✓ (1 celular na turma)", progress.ReadyText(phonesInClass: 1, total: null));
    }

    [Fact]
    public void The_step_caption_counts_the_guide_steps()
    {
        var progress = AtNumber();
        progress.Next();

        Assert.Equal($"Passo 2 de {PhoneSetupGuide.Steps.Count}", progress.StepCaption);
        Assert.Equal(IndexOf("bateria"), progress.StepIndex);
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
    public void A_wiped_phone_does_not_offer_the_wipe_again_after_waiting_and_going_back()
    {
        var progress = OfferedWipe();
        progress.WipeStarted();
        progress.WipeSucceeded();
        progress.QrShown(Shown);

        progress.Next();
        progress.Back();

        Assert.Equal("seis-toques", progress.StepId);
        Assert.Equal(WipeState.Done, progress.Wipe);
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
    public void After_a_failed_wipe_the_way_forward_is_the_reset_step_without_the_offer()
    {
        var progress = OfferedWipe();
        progress.WipeStarted();
        progress.WipeFailed("sem resposta");

        progress.Restart();

        Assert.Equal("restaurar", progress.StepId);
        Assert.False(progress.OffersWipe);
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

        Assert.Equal("seis-toques", progress.StepId);
        Assert.Equal(WipeState.Done, progress.Wipe);
        Assert.Equal("Celular 07", progress.Name);
    }
}
