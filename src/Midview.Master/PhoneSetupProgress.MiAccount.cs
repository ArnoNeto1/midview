namespace Midview.Master;

// The school's Mi account in the assistant: its page ("Conta Mi da escola"),
// asked once per window right before the step that signs in with it when
// none is saved, or opened from that step's "trocar"; and how the end of the
// account goes (by the cable, or by hand after the cable could not).
internal sealed partial class PhoneSetupProgress
{
    private static readonly int MiStep = IndexOf("chip-conta-mi");

    // The step the Mi account page goes on to, and the one its Back returns
    // to (null: the same step, as it was opened from there).
    private int? _afterMi;
    private int? _beforeMi;
    private bool _miAsked;

    // Nothing saved yet: the page comes up once before the Mi step. Set by
    // the window (MiAccountFile), and off once saved.
    public bool AsksMiAccount { get; set; }

    // The cable found an account left (or could not sign out of the Mi
    // account by itself): the step that removes it is shown done by hand.
    public bool SignOutByHand { get; private set; }

    // The cable list shows "Saindo da conta Mi da escola": the cable reached it.
    public bool MiSignOutShown { get; private set; }

    // "trocar" or "Guardar a conta Mi da escola" on a step: the page, then back to the step.
    public void ChangeMiAccount()
    {
        if (Page != SetupPage.Step)
        {
            return;
        }
        _afterMi = StepIndex;
        _beforeMi = null;
        Page = SetupPage.MiAccount;
    }

    private void ShowStepOrMiAccount(int index)
    {
        if (index == MiStep && AsksMiAccount && !_miAsked)
        {
            _miAsked = true;
            _beforeMi = StepIndex;
            _afterMi = index;
            Page = SetupPage.MiAccount;
            return;
        }
        ShowStep(index);
    }

    private void LeaveMiAccount()
    {
        ShowStep(_afterMi ?? MiStep);
        _afterMi = null;
        _beforeMi = null;
    }

    private void BackFromMiAccount()
    {
        ShowStep(_beforeMi ?? _afterMi ?? MiStep);
        _afterMi = null;
        _beforeMi = null;
    }
}
