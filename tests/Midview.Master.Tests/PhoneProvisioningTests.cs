using Midview.Master;
using Xunit;

public class PhoneProvisioningTests
{
    private const string Placeholder = "PREENCHER-NA-TASK-9";

    // The signing checksum is a SHA-256: 32 bytes in Base64 URL-safe without
    // "=" are 43 characters. Skipped while the value is still the placeholder.
    [Fact]
    public void Signature_checksum_is_base64_url_safe_sha256()
    {
        if (PhoneProvisioning.SignatureChecksum == Placeholder)
        {
            return;
        }
        Assert.Matches("^[A-Za-z0-9_-]{43}$", PhoneProvisioning.SignatureChecksum);
    }

    // Opt-in guard: with MIDVIEW_RELEASE set (no workflow sets it; android.yml
    // and pacote.yml gate on the placeholder themselves), a placeholder
    // checksum fails, since prepared phones would refuse the app.
    [Fact]
    public void Placeholder_checksum_does_not_ship_in_a_release_build()
    {
        var release = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MIDVIEW_RELEASE"));
        Assert.False(release && PhoneProvisioning.SignatureChecksum == Placeholder,
            "PhoneProvisioning.SignatureChecksum ainda é o texto provisório; fixe o checksum do APK assinado antes de publicar.");
    }

    [Fact]
    public void Placeholder_checksum_is_not_ready() =>
        Assert.False(PhoneProvisioning.IsReadyWith("PREENCHER-NA-TASK-9"));

    [Fact]
    public void Empty_checksum_is_not_ready() =>
        Assert.False(PhoneProvisioning.IsReadyWith(""));

    [Fact]
    public void Real_looking_checksum_is_ready() =>
        Assert.True(PhoneProvisioning.IsReadyWith(new string('A', 43)));

    [Fact]
    public void IsReady_follows_the_constant() =>
        Assert.Equal(PhoneProvisioning.IsReadyWith(PhoneProvisioning.SignatureChecksum), PhoneProvisioning.IsReady);

    [Theory]
    [InlineData("PREENCHER-NA-TASK-9", false, false)]
    [InlineData("PREENCHER-NA-TASK-9", true, false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false, false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true, true)]
    public void Wizard_needs_both_gates(string checksum, bool cableReady, bool expected) =>
        Assert.Equal(expected, PhoneProvisioning.WizardReadyWith(checksum, cableReady));

    [Fact]
    public void WizardReady_follows_both_constants() =>
        Assert.Equal(
            PhoneProvisioning.WizardReadyWith(PhoneProvisioning.SignatureChecksum, PhoneProvisioning.CableReady),
            PhoneProvisioning.WizardReady);

    // Google blocks the QR for device-policy apps off its approved list: the
    // QR path is gone, and nothing offers it.
    [Fact]
    public void There_is_no_qr_offer_any_more() =>
        Assert.DoesNotContain("Qr", Enum.GetNames<PhoneSetupOffer>());

    [Fact]
    public void Setup_is_em_breve_until_the_phone_app_is_published()
    {
        Assert.Equal(PhoneSetupOffer.Soon, PhoneProvisioning.SetupWith(appReady: false, wizardReady: false));
        Assert.Equal(PhoneSetupOffer.Soon, PhoneProvisioning.SetupWith(appReady: false, wizardReady: true));
    }

    [Fact]
    public void Published_phone_app_without_the_assistant_is_prepared_by_cable_by_hand() =>
        Assert.Equal(PhoneSetupOffer.Cable, PhoneProvisioning.SetupWith(appReady: true, wizardReady: false));

    [Fact]
    public void Assistant_is_offered_only_with_the_app_published_and_the_wizard_ready() =>
        Assert.Equal(PhoneSetupOffer.Wizard, PhoneProvisioning.SetupWith(appReady: true, wizardReady: true));

    // Setup follows the two gates: the assistant exactly when WizardReady.
    [Fact]
    public void Setup_follows_the_gates()
    {
        Assert.Equal(PhoneProvisioning.SetupWith(PhoneProvisioning.IsReady, PhoneProvisioning.WizardReady), PhoneProvisioning.Setup);
        Assert.Equal(PhoneProvisioning.WizardReady, PhoneProvisioning.Setup == PhoneSetupOffer.Wizard);
    }

    // Nobody is sent to a preparation that is not out yet.
    [Theory]
    [InlineData(PhoneSetupOffer.Soon)]
    [InlineData(PhoneSetupOffer.Cable)]
    public void No_phone_hint_sends_nobody_to_the_assistant_unless_it_is_out(PhoneSetupOffer setup) =>
        Assert.DoesNotContain("Preparar celular", PhoneProvisioning.NoPhoneYet(setup));

    [Fact]
    public void Wizard_hint_points_to_the_assistant()
    {
        Assert.Contains("\"Preparar celulares\"", PhoneProvisioning.NoPhoneYet(PhoneSetupOffer.Wizard));
        Assert.Equal("Nenhum tablet nesta sala ainda. Para colocar o primeiro, escolha \"Tablets\" e use \"Preparar tablets\".",
            PhoneProvisioning.NoDeviceYet(AndroidDeviceKind.Tablet, PhoneSetupOffer.Wizard));
    }

    [Fact]
    public void Cable_hint_says_where_the_steps_are()
    {
        Assert.Contains("cabo USB", PhoneProvisioning.NoPhoneYet(PhoneSetupOffer.Cable));
        Assert.Equal("Nenhum celular nesta sala ainda.", PhoneProvisioning.NoPhoneYet(PhoneSetupOffer.Soon));
        Assert.Equal("https://github.com/ArnoNeto1/midview#celulares", PhoneProvisioning.CableStepsUrl);
    }
}
