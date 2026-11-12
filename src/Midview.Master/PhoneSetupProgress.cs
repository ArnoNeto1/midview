namespace Midview.Master;

// The pages of the "Preparar celulares" assistant (PreparePhonesWindow).
internal enum SetupPage
{
    Wifi,
    Gather,
    Step,
    ConfirmRedo,
    Cable,
    CableFailed,
    Ready,
    NotAppeared,
}

// Where "Apagar e preparar de novo" stands on the "ligar" page of a phone
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
// which page, which step of PhoneSetupGuide, the phone's number and how the
// cable part goes. The window draws the page and calls these as the teacher
// clicks, as the cable answers and as a timer ticks.
internal sealed class PhoneSetupProgress
{
    // How long after "Pronto" the assistant waits before "ainda não apareceu".
    public static readonly TimeSpan NotAppearedAfter = TimeSpan.FromMinutes(5);

    private static readonly int NumberStep = IndexOf("numero");
    private static readonly int TurnOnStep = IndexOf("ligar");
    private static readonly int CableStep = IndexOf("cabo");

    // The step to come back to after the Wi-Fi page ("trocar").
    private int? _afterWifi;

    // "Antes de começar, separe:" was shown (or the teacher did it before).
    private bool _gathered;

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

    // The last item of the cable list reached (null before the first).
    public CableStage? Stage { get; private set; }

    // Why the cable part stopped (on SetupPage.CableFailed), in plain words,
    // and the step that fixes it (null: none does).
    public CableProblem? Problem { get; private set; }

    public string? ProblemText { get; private set; }

    public string? ProblemStepId { get; private set; }

    public int? ProblemStepNumber => ProblemStepId is { } id ? IndexOf(id) + 1 : null;

    // When the phone was ready ("Pronto. Pode tirar o cabo."), and whether it
    // showed up in the class since.
    public DateTimeOffset? ReadyAt { get; private set; }

    public bool HasAppeared { get; private set; }

    // The "ligar" page opens with "Apareceu Bem-vindo" in sight: the phone was
    // just wiped (or restored by hand) and is on that screen.
    public bool OpensWelcomeHelp { get; private set; }

    // The "ligar" page offers "Apagar e preparar de novo": the number was
    // already a phone of the class, online, and the teacher confirmed.
    public bool OffersWipe => Wipe != WipeState.None;

    // Kept here, not in the window, so that redrawing the page does not offer
    // the wipe again.
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
            OpensWelcomeHelp = true;
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

    // hasWifi: the lab Wi-Fi is saved. gathered: the teacher already went
    // through "Antes de começar, separe:" (it is shown only once).
    public void Start(bool hasWifi, bool gathered)
    {
        Reset();
        _gathered = gathered;
        if (!hasWifi)
        {
            Page = SetupPage.Wifi;
        }
        else if (!gathered)
        {
            ShowGather();
        }
        else
        {
            ShowStep(NumberStep);
        }
    }

    // What the teacher typed on the number page; false (and no number) when
    // it is not 1 to 3 digits.
    public bool SetNumber(string typed)
    {
        Number = PhoneNumber.Normalize(typed);
        return Number is not null;
    }

    // "trocar": the way back is the step the teacher was on.
    public void ChangeWifi()
    {
        _afterWifi = Page == SetupPage.Step ? StepIndex : NumberStep;
        Page = SetupPage.Wifi;
    }

    public void Next()
    {
        switch (Page)
        {
            case SetupPage.Wifi when _afterWifi is { } step:
                ShowStep(step);
                _afterWifi = null;
                break;
            case SetupPage.Wifi when !_gathered:
                ShowGather();
                break;
            case SetupPage.Wifi:
            case SetupPage.Gather:
                ShowStep(NumberStep);
                break;
            case SetupPage.ConfirmRedo:
                SetWipe(_takenOnline ? WipeState.Offered : WipeState.None);
                ShowStep(TurnOnStep);
                break;
            case SetupPage.Step when StepIndex == NumberStep:
                if (Number is not null)
                {
                    ShowStep(StepIndex + 1);
                }
                break;
            // The cable step moves on by itself, when the cable sees the phone.
            case SetupPage.Step when StepIndex == CableStep:
                break;
            case SetupPage.Step:
                ShowStep(StepIndex + 1);
                break;
            // "Tentar de novo".
            case SetupPage.CableFailed:
                ShowCable();
                break;
            // "Preparar o próximo".
            case SetupPage.Ready or SetupPage.NotAppeared:
                Again();
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
            case SetupPage.Step when StepIndex > NumberStep:
                ShowStep(StepIndex - 1);
                break;
            // "Voltar ao passo 7": the step that fixes what the cable found.
            case SetupPage.CableFailed when ProblemStepId is { } id:
                ShowStep(IndexOf(id));
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

    // The cable step saw one phone, allowed ("Permitir"): the Midview finishes
    // it now. False (nothing changes) on any other page.
    public bool CableSeen()
    {
        if (Page != SetupPage.Step || StepIndex != CableStep)
        {
            return false;
        }
        ShowCable();
        return true;
    }

    public void CableStageReached(CableStage stage)
    {
        if (Page == SetupPage.Cable)
        {
            Stage = stage;
        }
    }

    public void CableFailed(CableProblem problem)
    {
        if (Page != SetupPage.Cable)
        {
            return;
        }
        var (text, step) = PhoneCable.Describe(problem);
        Problem = problem;
        ProblemText = text;
        ProblemStepId = step;
        Page = SetupPage.CableFailed;
    }

    // The Midview could not start finishing the phone (the phone app is
    // missing): back to the cable step, which says what lacks.
    public void CableStopped()
    {
        if (Page == SetupPage.Cable)
        {
            Stage = null;
            ShowStep(CableStep);
        }
    }

    // The phone is the school's own: "Pronto. Pode tirar o cabo."
    public void CablePrepared(DateTimeOffset at)
    {
        if (Page != SetupPage.Cable)
        {
            return;
        }
        ReadyAt = at;
        HasAppeared = false;
        Page = SetupPage.Ready;
    }

    // Five minutes after ready with no sign of the phone: a network problem.
    public void Tick(DateTimeOffset now)
    {
        if (Page == SetupPage.Ready && !HasAppeared && ReadyAt is { } ready && now - ready >= NotAppearedAfter)
        {
            Page = SetupPage.NotAppeared;
        }
    }

    // The phone just prepared answers in the class.
    public void Appeared()
    {
        if (Page is not (SetupPage.Ready or SetupPage.NotAppeared))
        {
            return;
        }
        HasAppeared = true;
        Page = SetupPage.Ready;
    }

    // Straight to turning the phone on, for a phone just wiped by "Apagar e
    // preparar de novo": it is on "Bem-vindo", so that help opens. wiped: the
    // page also says the wipe was sent.
    public void StartAt(string number, bool wiped = false)
    {
        Reset();
        _gathered = true;
        Number = PhoneNumber.Normalize(number) ?? number;
        SetWipe(wiped ? WipeState.Done : WipeState.None);
        ShowStep(TurnOnStep);
        OpensWelcomeHelp = true;
    }

    // After a failed wipe, the teacher restored the phone by hand (recovery
    // mode): the same page, now with the steps and "Apareceu Bem-vindo".
    public void RestoredByHand()
    {
        SetWipe(WipeState.None);
        OpensWelcomeHelp = true;
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
        OpensWelcomeHelp = false;
    }

    private void ShowGather()
    {
        Page = SetupPage.Gather;
        _gathered = true;
    }

    private void ShowCable()
    {
        Stage = null;
        Problem = null;
        ProblemText = null;
        ProblemStepId = null;
        Page = SetupPage.Cable;
    }

    private void Reset()
    {
        Number = null;
        SetWipe(WipeState.None);
        _takenOnline = false;
        _afterWifi = null;
        Stage = null;
        Problem = null;
        ProblemText = null;
        ProblemStepId = null;
        ReadyAt = null;
        HasAppeared = false;
        OpensWelcomeHelp = false;
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
