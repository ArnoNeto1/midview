using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PhoneAppBlockTextTests
{
    private const string YouTube = "com.google.android.youtube";
    private const string Chrome = "com.android.chrome";
    private const string Calculator = "com.miui.calculator";

    private static readonly byte[] IconA = { 1 };
    private static readonly byte[] IconB = { 2 };

    private static PhoneBlockState State(string name, PhoneAppPolicy? policy, bool oldAgent = false) =>
        new(name, policy is null && !oldAgent ? null : new PhoneAppBlockClient.StatusResult(policy, oldAgent));

    private static PhoneAppPolicy Block(long savedAt, params string[] packages) => new(savedAt, AppBlockMode.BlockList, packages);

    private static PhoneAppPolicy Allow(long savedAt, params string[] packages) => new(savedAt, AppBlockMode.AllowOnly, packages);

    private static string Badge(PhoneAppPolicy? policy, bool oldAgent = false, bool classBlock = false) =>
        PhoneAppBlockText.Badge(new PhoneAppBlockClient.StatusResult(policy, oldAgent), classBlock);

    [Fact]
    public void Badge_CountsTheBlockedApps_InSingularAndPlural()
    {
        Assert.Equal("🔒 3 apps bloqueados", Badge(Block(1, YouTube, Chrome, Calculator)));
        Assert.Equal("🔒 1 app bloqueado", Badge(Block(1, YouTube)));
    }

    [Fact]
    public void Badge_CountsTheAllowedApps_InSingularAndPlural()
    {
        Assert.Equal("🔒 só 2 apps liberados", Badge(Allow(1, Chrome, Calculator)));
        Assert.Equal("🔒 só 1 app liberado", Badge(Allow(1, Chrome)));
        Assert.Equal("🔒 só o essencial", Badge(Allow(1)));
    }

    [Fact]
    public void Badge_IsEmpty_WithNothingBlocked_OrNoAnswer()
    {
        Assert.Equal(string.Empty, Badge(PhoneAppPolicy.Lifted(1)));
        Assert.Equal(string.Empty, PhoneAppBlockText.Badge(null, classBlockExpected: true));
    }

    [Fact]
    public void Badge_WarnsAboutAnOldMidview_OnlyWhenABlockIsExpected()
    {
        Assert.Equal("⚠ Midview antigo: sem bloqueio de apps", Badge(null, oldAgent: true, classBlock: true));
        Assert.Equal(string.Empty, Badge(null, oldAgent: true, classBlock: false));
    }

    // What an older phone agent answers to a command it does not know.
    [Theory]
    [InlineData("")]
    [InlineData("Ainda não funciona em celular.")]
    public void ApplyLine_AnOlderPhone_SaysSo(string detail) =>
        Assert.Equal(PhoneAppBlockText.OldAgent, PhoneAppBlockText.ApplyLine(false, detail));

    [Fact]
    public void ApplyLine_WhatThePhoneSaid_IsShownAsIs()
    {
        Assert.Equal("bloqueando 2 app(s)", PhoneAppBlockText.ApplyLine(true, "bloqueando 2 app(s)"));
        Assert.Equal("pedido inválido", PhoneAppBlockText.ApplyLine(false, "pedido inválido"));
    }

    [Theory]
    [InlineData("mantido o bloqueio mais novo, salvo em 11/11 21:30: permitindo só o essencial", true, true)]
    [InlineData("bloqueando 1 app(s)", true, false)]
    [InlineData("mantido o bloqueio mais novo, salvo em 11/11 21:30: x", false, false)]
    public void KeptNewer_IsAnAckThatDidNotApplyWhatWasAsked(string detail, bool succeeded, bool expected) =>
        Assert.Equal(expected, PhoneAppBlockText.KeptNewer(succeeded, detail));

    [Fact]
    public void Summarize_GroupsPhonesByWhatTheyFollow_WithTheAppsNames()
    {
        var labels = new Dictionary<string, string> { [YouTube] = "YouTube", [Chrome] = "Chrome" };
        var text = PhoneAppBlockText.Summarize(new[]
        {
            State("Cel 01", Block(1_700_000_000, YouTube, Chrome)),
            State("Cel 02", Block(1_700_000_000, Chrome, YouTube)),
            State("Cel 03", PhoneAppPolicy.Lifted(5)),
            State("Cel 04", null, oldAgent: true),
            State("Cel 05", null),
        }, package => labels.GetValueOrDefault(package, package));

        var lines = text.Split('\n');
        Assert.StartsWith("Bloqueando YouTube, Chrome em 2 celulares (salvo em ", lines[0]);
        Assert.Equal("Nenhum app bloqueado em 1 celular", lines[1]);
        Assert.Equal("Midview de versão anterior (atualize para bloquear apps): Cel 04", lines[2]);
        Assert.Equal("Sem resposta: Cel 05", lines[3]);
    }

    [Fact]
    public void Summarize_AllowOnly_SaysTheEssentialAndTheListed()
    {
        var text = PhoneAppBlockText.Summarize(new[] { State("Cel 01", Allow(1_700_000_000, Chrome)), State("Cel 02", Allow(1_700_000_000)) });

        Assert.Contains($"Liberando só o essencial e {Chrome} em 1 celular", text);
        Assert.Contains("Liberando só o essencial em 1 celular", text);
    }

    [Fact]
    public void Summarize_NobodyAnswered()
    {
        Assert.Equal("Nenhum celular respondeu.", PhoneAppBlockText.Summarize(Array.Empty<PhoneBlockState>()));
        Assert.Equal("Sem resposta: Cel 01, Cel 02", PhoneAppBlockText.Summarize(new[] { State("Cel 01", null), State("Cel 02", null) }));
    }

    // Newer than every phone's block and than the class block this PC keeps,
    // even with this PC's clock behind.
    [Fact]
    public void NextSavedAt_BeatsEveryReportedBlock_AndTheClassBlock()
    {
        var reported = new[] { State("Cel 01", Block(2000, YouTube)), State("Cel 02", PhoneAppPolicy.Lifted(3000)), State("Cel 03", null) };

        Assert.Equal(3001, PhoneAppBlockText.NextSavedAt(1000, reported, null));
        Assert.Equal(5000, PhoneAppBlockText.NextSavedAt(5000, reported, null));
        Assert.Equal(4001, PhoneAppBlockText.NextSavedAt(1000, reported, Block(4000, Chrome)));
        Assert.Equal(1000, PhoneAppBlockText.NextSavedAt(1000, Array.Empty<PhoneBlockState>(), null));
    }

    [Fact]
    public void CommonActivePolicy_IsTheBlockEveryAnsweringPhoneFollows()
    {
        var common = PhoneAppBlockText.CommonActivePolicy(new[]
        {
            State("Cel 01", Block(10, YouTube, Chrome)),
            State("Cel 02", Block(20, Chrome, YouTube)),
            State("Cel 03", null),
            State("Cel 04", null, oldAgent: true),
        });

        Assert.NotNull(common);
        Assert.Equal(20, common!.SavedAt);
        Assert.Null(PhoneAppBlockText.CommonActivePolicy(new[] { State("Cel 01", Block(1, YouTube)), State("Cel 02", Allow(1, YouTube)) }));
        Assert.Null(PhoneAppBlockText.CommonActivePolicy(new[] { State("Cel 01", PhoneAppPolicy.Lifted(1)) }));
    }

    // With several phones the window lists every app any of them has, once,
    // named and drawn as the first phone that has it says.
    [Fact]
    public void Union_JoinsThePhonesLists_ByPackage_SortedByName()
    {
        var rows = PhoneAppBlockText.Union(new IReadOnlyList<PhoneApp>?[]
        {
            new[] { new PhoneApp(YouTube, "YouTube", IconA), new PhoneApp(Chrome, "Chrome", IconA) },
            null,
            new[] { new PhoneApp(Chrome, "Chrome (outro)", IconB), new PhoneApp(Calculator, "calculadora", IconB) },
        });

        Assert.Equal(new[] { "calculadora", "Chrome", "YouTube" }, rows.Select(row => row.App.Label));
        var chrome = rows.Single(row => row.App.Package == Chrome);
        Assert.Equal(IconA, chrome.App.IconPng);
        Assert.Equal(2, chrome.Phones);
        Assert.Equal(1, rows.Single(row => row.App.Package == YouTube).Phones);
    }

    [Fact]
    public void Union_AnAppWithNoLabel_IsNamedByItsPackage()
    {
        var row = Assert.Single(PhoneAppBlockText.Union(new IReadOnlyList<PhoneApp>?[] { new[] { new PhoneApp(Chrome, "", IconA) } }));

        Assert.Equal(Chrome, row.Name);
    }

    [Fact]
    public void RowDetail_SaysOnHowManyPhones_OnlyWhenNotOnAll()
    {
        Assert.Equal(string.Empty, PhoneAppBlockText.RowDetail(3, 3));
        Assert.Equal(string.Empty, PhoneAppBlockText.RowDetail(1, 1));
        Assert.Equal("em 2 de 3 celulares", PhoneAppBlockText.RowDetail(2, 3));
        Assert.Equal("não está na lista dos celulares", PhoneAppBlockText.RowDetail(0, 3));
    }

    [Fact]
    public void ListLine_SaysWhichPhonesSentTheirApps()
    {
        Assert.Equal("Apps que o aluno pode abrir neste celular.", PhoneAppBlockText.ListLine(1, 1));
        Assert.Equal("Apps de todos os 4 celulares.", PhoneAppBlockText.ListLine(4, 4));
        Assert.Equal("Apps de 3 de 4 celulares (os outros não responderam).", PhoneAppBlockText.ListLine(3, 4));
        Assert.Equal("Nenhum celular mandou a lista de apps. Confira se estão ligados e use \"Atualizar lista\".", PhoneAppBlockText.ListLine(0, 2));
    }

    [Fact]
    public void Targets_NamesOne_CountsTheClass_OrNamesAFew()
    {
        Assert.Equal("Em: Cel 01", PhoneAppBlockText.Targets(new[] { "Cel 01" }, allPhones: true));
        Assert.Equal("Em todos os celulares da sala (3)", PhoneAppBlockText.Targets(new[] { "Cel 01", "Cel 02", "Cel 03" }, allPhones: true));
        Assert.Equal("Em 2 celulares: Cel 01, Cel 02", PhoneAppBlockText.Targets(new[] { "Cel 01", "Cel 02" }, allPhones: false));
    }

    // The toolbar's "Bloquear programas" reaches the whole class, as it always
    // did, except a selection of phones only, or the "Celulares" filter: then
    // the phones (and so the phones' window).
    [Fact]
    public void ToolbarReach_IsTheClass_UnlessOnlyPhonesAreChosen()
    {
        var pc = new KnownMachine("PC 01", "192.0.2.1", "s", "a");
        var cel1 = new KnownMachine("Cel 01", "192.0.2.2", "s", "a", Kind: DeviceKind.Android);
        var cel2 = new KnownMachine("Cel 02", "192.0.2.3", "s", "a", Kind: DeviceKind.Android);
        var room = new[] { pc, cel1, cel2 };

        Assert.Equal(room, PhoneAppBlockText.ToolbarReach(Array.Empty<KnownMachine>(), room, DeviceFilter.All));
        Assert.Equal(room, PhoneAppBlockText.ToolbarReach(new[] { pc }, room, DeviceFilter.All));
        Assert.Equal(room, PhoneAppBlockText.ToolbarReach(new[] { pc, cel1 }, room, DeviceFilter.All));
        Assert.Equal(room, PhoneAppBlockText.ToolbarReach(Array.Empty<KnownMachine>(), room, DeviceFilter.Computers));
        Assert.Equal(new[] { cel2 }, PhoneAppBlockText.ToolbarReach(new[] { cel2 }, room, DeviceFilter.All));
        Assert.Equal(new[] { cel1, cel2 }, PhoneAppBlockText.ToolbarReach(Array.Empty<KnownMachine>(), room, DeviceFilter.Phones));
    }

    // "Bloquear programas" with PCs and phones together opens the PCs' window.
    [Fact]
    public void LeftOutNotice_TellsHowToReachThePhones()
    {
        Assert.Null(PhoneAppBlockText.LeftOutNotice(0));
        Assert.Equal("1 celular ficou de fora: para bloquear apps no celular, selecione só os celulares.", PhoneAppBlockText.LeftOutNotice(1));
        Assert.Equal("3 celulares ficaram de fora: para bloquear apps nos celulares, selecione só os celulares.", PhoneAppBlockText.LeftOutNotice(3));
    }
}
