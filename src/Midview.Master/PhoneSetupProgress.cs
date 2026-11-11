namespace Midview.Master;

// The pages of the "Preparar celulares" assistant (PreparePhonesWindow).
internal enum SetupPage
{
    Wifi,
    Step,
    ConfirmRedo,
    Waiting,
    NotAppeared,
    Ready,
}

// Where "Apagar e preparar de novo" stands on the six taps page of a phone
// that was already in the class.
internal enum WipeState
{
    None,
    Offered,
    Wiping,
    Done,
    Failed,
}

// Where the assistant is for the phone being prepared, without any window:
// which page, which step of PhoneSetupGuide, the phone's number and when its
// QR was shown. The window draws the page and calls these as the teacher
// clicks; the class's own events (Joined) and a timer (Tick) move it too.
internal sealed class PhoneSetupProgress
{
    // How long after the QR the assistant waits before "ainda não apareceu".
    public static readonly TimeSpan NotAppearedAfter = TimeSpan.FromMinutes(15);

    private static readonly int NumberStep = IndexOf("numero");
    private static readonly int BatteryStep = IndexOf("bateria");
    private static readonly int ResetStep = IndexOf("restaurar");
    private static readonly int SixTapsStep = IndexOf("seis-toques");

    // The step to come back to after the Wi-Fi page ("trocar").
    private int? _afterWifi;

    // After "Já existe um Celular 07": whether that one was online and can be wiped.
    private bool _takenOnline;

    public SetupPage Page { get; private set; } = SetupPage.Wifi;

    // The guide step shown (meaningful on SetupPage.Step).
    public int StepIndex { get; private set; }

    public GuideStep? Step => Page == SetupPage.Step ? PhoneSetupGuide.Steps[StepIndex] : null;

    public string? StepId => Step?.Id;

    public string StepCaption => $"Passo {StepIndex + 1} de {PhoneSetupGuide.Steps.Count}";

    // "07": set once the teacher typed a valid one.
    public string? Number { get; private set; }

    public string? Name => Number is null ? null : PhoneNumber.NameOf(Number);

    // When this phone's QR was first shown; null until then, and again after
    // Restart/Again/StartAt (a new QR is due).
    public DateTimeOffset? QrShownAt { get; private set; }

    // The six taps page offers "Apagar e preparar de novo": the number was
    // already a phone of the class, online, and the teacher confirmed.
    public bool OffersWipe => Wipe != WipeState.None;

    // Kept here, not in the window, so that redrawing the page (going Next to
    // the waiting page and Back) does not offer the wipe again.
    public WipeState Wipe { get; private set; }

    // Why the wipe failed, for the page; null unless Wipe is Failed.
    public string? WipeFailure { get; private set; }

    public void WipeStarted()
    {
        if (OffersWipe)
        {
            Wipe = WipeState.Wiping;
            WipeFailure = null;
        }
    }

    public void WipeSucceeded()
    {
        if (OffersWipe)
        {
            Wipe = WipeState.Done;
            WipeFailure = null;
        }
    }

    public void WipeFailed(string reason)
    {
        if (OffersWipe)
        {
            Wipe = WipeState.Failed;
            WipeFailure = reason;
        }
    }

    // The Wi-Fi page was opened with "trocar": Back returns to the step.
    public bool ReturnsAfterWifi => Page == SetupPage.Wifi && _afterWifi is not null;

    public string ConfirmRedoText => $"Já existe um {Name}. Vai preparar de novo o {Number}?";

    public void Start(bool hasWifi)
    {
        Reset();
        Page = hasWifi ? SetupPage.Step : SetupPage.Wifi;
        StepIndex = NumberStep;
    }

    // What the teacher typed on the number page; false (and no number) when
    // it is not 1 to 3 digits.
    public bool SetNumber(string typed)
    {
        Number = PhoneNumber.Normalize(typed);
        return Number is not null;
    }

    // "trocar": while waiting for the phone, the way back is the six taps,
    // where a QR with the new Wi-Fi is shown.
    public void ChangeWifi()
    {
        _afterWifi = Page switch
        {
            SetupPage.Step => StepIndex,
            SetupPage.Waiting or SetupPage.NotAppeared => SixTapsStep,
            _ => NumberStep,
        };
        Page = SetupPage.Wifi;
    }

    public void Next()
    {
        switch (Page)
        {
            case SetupPage.Wifi:
                ShowStep(_afterWifi ?? NumberStep);
                _afterWifi = null;
                break;
            case SetupPage.ConfirmRedo:
                SetWipe(_takenOnline ? WipeState.Offered : WipeState.None);
                ShowStep(_takenOnline ? SixTapsStep : BatteryStep);
                break;
            case SetupPage.Step when StepIndex == NumberStep:
                if (Number is not null)
                {
                    ShowStep(StepIndex + 1);
                }
                break;
            case SetupPage.Step when StepIndex == SixTapsStep:
                if (QrShownAt is not null)
                {
                    Page = SetupPage.Waiting;
                }
                break;
            case SetupPage.Step:
                ShowStep(StepIndex + 1);
                break;
        }
    }

    public void Back()
    {
        switch (Page)
        {
            case SetupPage.Wifi when _afterWifi is { } step:
                ShowStep(step);
                _afterWifi = null;
                break;
            case SetupPage.ConfirmRedo:
                ShowStep(NumberStep);
                break;
            case SetupPage.Waiting or SetupPage.NotAppeared:
                ShowStep(SixTapsStep);
                break;
            case SetupPage.Step when StepIndex > NumberStep:
                ShowStep(StepIndex - 1);
                break;
        }
    }

    // In place of Next on the number page when the class already has a phone
    // with that name. online: it answers now and the window can wipe it.
    public void NumberTaken(bool online)
    {
        if (Number is null)
        {
            return;
        }
        _takenOnline = online;
        Page = SetupPage.ConfirmRedo;
    }

    // A new QR (a new proof) was shown: the 15 minutes count from here.
    public void QrShown(DateTimeOffset at) => QrShownAt = at;

    public void Tick(DateTimeOffset now)
    {
        if (Page == SetupPage.Waiting && QrShownAt is { } shown && now - shown >= NotAppearedAfter)
        {
            Page = SetupPage.NotAppeared;
        }
    }

    // "Apareceu um erro no celular?" while waiting: the list of what to check, now.
    public void NotAppearedNow()
    {
        if (Page == SetupPage.Waiting)
        {
            Page = SetupPage.NotAppeared;
        }
    }

    // A prepared phone joined the class. True when it is the one this QR was
    // for (the page is then Ready); any other phone changes nothing.
    public bool Joined(string name)
    {
        var waiting = Page is SetupPage.Waiting or SetupPage.NotAppeared
                      || (Page == SetupPage.Step && StepIndex == SixTapsStep);
        if (!waiting || QrShownAt is null || Name is null || !string.Equals(name, Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        Page = SetupPage.Ready;
        return true;
    }

    // Straight to the six taps for a phone just wiped by "Apagar e preparar de
    // novo" (wiped: the page says so instead of offering the wipe).
    public void StartAt(string number, bool wiped = false)
    {
        Reset();
        Number = PhoneNumber.Normalize(number) ?? number;
        SetWipe(wiped ? WipeState.Done : WipeState.None);
        ShowStep(SixTapsStep);
    }

    // "Recomeçar este celular": the same number, from the factory reset, with a new QR.
    public void Restart()
    {
        QrShownAt = null;
        SetWipe(WipeState.None);
        ShowStep(ResetStep);
    }

    // "Preparar o próximo": a new phone, from its number.
    public void Again()
    {
        Reset();
        ShowStep(NumberStep);
    }

    // "Celular 07 pronto ✓ (3 de 11)", or "(3 celulares na turma)" with no total.
    public string ReadyText(int phonesInClass, int? total)
    {
        var count = total is { } of
            ? $"{phonesInClass} de {of}"
            : phonesInClass == 1 ? "1 celular na turma" : $"{phonesInClass} celulares na turma";
        return $"{Name} pronto ✓ ({count})";
    }

    private void ShowStep(int index)
    {
        Page = SetupPage.Step;
        StepIndex = index;
    }

    private void Reset()
    {
        Number = null;
        QrShownAt = null;
        SetWipe(WipeState.None);
        _takenOnline = false;
        _afterWifi = null;
    }

    private void SetWipe(WipeState state)
    {
        Wipe = state;
        WipeFailure = null;
    }

    private static int IndexOf(string id)
    {
        for (var i = 0; i < PhoneSetupGuide.Steps.Count; i++)
        {
            if (PhoneSetupGuide.Steps[i].Id == id)
            {
                return i;
            }
        }
        throw new InvalidOperationException("Passo sem roteiro: " + id);
    }
}
