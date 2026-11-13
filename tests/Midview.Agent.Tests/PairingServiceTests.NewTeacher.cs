using System.Text;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

// "Permitir novo professor" with its code (NewTeacherInvite) and "Esquecer os
// outros professores": the window a trusted teacher opens, who gets in by it,
// and how it closes.
public partial class PairingServiceTests
{
    private const string Code = "K7PQ2M94";

    // A new teacher who typed this code: its proof, from the nonce the
    // Agent revealed.
    private static Exchange ConfirmingWithCode(string master, string code = Code)
    {
        var exchange = new Exchange();
        return exchange
            .Then(MessageType.PairNonce, MasterNonce)
            .Then(() => new Message(MessageType.PairConfirm, NewTeacherInvite.Proof(
                NewTeacherInvite.Key(code, AgentThumbprint), master, AgentThumbprint, exchange.Sent[1].Payload, MasterNonce)));
    }

    private static Exchange Cancelling() =>
        new Exchange().Then(MessageType.PairNonce, MasterNonce).Then(MessageType.PairCancel);

    private static (bool, string) Allow(PairingService service, string code = Code) =>
        service.AllowNewTeacher(NewTeacherInvite.Encode(code));

    [Fact]
    public void InsideTheNewTeacherWindow_OneMoreTeacherPairsWithTheCode_AndTheCodeIsUsedUp()
    {
        Trust(TeacherA);
        var service = NewService();

        var (succeeded, detail) = Allow(service);
        Assert.True(succeeded);
        Assert.Equal("liberada até 14:32 com o código", detail);
        Assert.True(service.NewTeacherOpen);

        var second = ConfirmingWithCode(TeacherB);
        Pair(service, TeacherB, second);
        Assert.Equal(MessageType.PairDone, second.Sent.Last().Type);
        Assert.Equal(new[] { TeacherA, TeacherB }, TrustedMastersFile.Load(_path));
        Assert.False(service.NewTeacherOpen);

        var third = ConfirmingWithCode(TeacherC);
        Pair(service, TeacherC, third);
        Assert.Equal(new[] { MessageType.PairRefused }, third.SentTypes);
    }

    // A Master from before the code: opening for it would let anyone in.
    [Fact]
    public void AllowNewTeacher_WithoutACode_OpensNothing()
    {
        Trust(TeacherA);
        var service = NewService();

        Assert.Equal((false, PairingService.OldTeacherProgram), service.AllowNewTeacher(Array.Empty<byte>()));
        Assert.Equal((false, "pedido inválido"), service.AllowNewTeacher(Encoding.ASCII.GetBytes("K7PQ2M9U")));
        Assert.False(service.NewTeacherOpen);
    }

    [Fact]
    public void InsideTheWindow_ATeacherWithoutTheCode_IsRefused()
    {
        Trust(TeacherA);
        var service = NewService();
        Allow(service);

        var exchange = Confirming();
        Pair(service, TeacherB, exchange);

        Assert.Equal(new[] { MessageType.PairCommit, MessageType.PairReveal, MessageType.PairRefused }, exchange.SentTypes);
        Assert.Equal(PairingService.NeedsCode, Pairing.RefusalText(exchange.Sent[2].Payload));
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
        Assert.True(service.NewTeacherOpen);
    }

    [Fact]
    public void InsideTheWindow_AWrongCode_IsRefused()
    {
        Trust(TeacherA);
        var service = NewService();
        Allow(service);

        var exchange = ConfirmingWithCode(TeacherB, "K7PQ2M95");
        Pair(service, TeacherB, exchange);

        Assert.Equal(PairingService.WrongCode, Pairing.RefusalText(exchange.Sent.Last().Payload));
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
        Assert.True(service.NewTeacherOpen);

        // The right teacher still gets in.
        var right = ConfirmingWithCode(TeacherC);
        Pair(service, TeacherC, right);
        Assert.Equal(MessageType.PairDone, right.Sent.Last().Type);
    }

    // A proof made for another teacher's certificate is no proof.
    [Fact]
    public void InsideTheWindow_AProofForAnotherTeacher_IsRefused()
    {
        Trust(TeacherA);
        var service = NewService();
        Allow(service);

        var exchange = ConfirmingWithCode(TeacherC);
        Pair(service, TeacherB, exchange);

        Assert.Equal(PairingService.WrongCode, Pairing.RefusalText(exchange.Sent.Last().Payload));
        Assert.False(TrustStore.FromFile(_path).IsTrusted(TeacherB));
    }

    // Wrong codes, cancels and dropped connections after the code came up
    // all count: the window closes before anyone can guess at it, or flash
    // codes on the student's screen for ten minutes.
    [Fact]
    public void FiveTriesWithoutTheCode_CloseTheWindow()
    {
        Trust(TeacherA);
        var service = NewService();
        Allow(service);

        Pair(service, TeacherB, ConfirmingWithCode(TeacherB, "K7PQ2M95"));
        Pair(service, TeacherB, Confirming());
        Pair(service, TeacherB, Cancelling());
        Assert.Throws<IOException>(() => Pair(service, TeacherB, new Exchange().Then(MessageType.PairNonce, MasterNonce)));
        Assert.True(service.NewTeacherOpen);
        Pair(service, TeacherB, ConfirmingWithCode(TeacherB, "AAAAAAAA"));

        Assert.False(service.NewTeacherOpen);
        var right = ConfirmingWithCode(TeacherC);
        Pair(service, TeacherC, right);
        Assert.Equal(new[] { MessageType.PairRefused }, right.SentTypes);
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
    }

    // A code the student never saw is no try.
    [Fact]
    public void ACodeThatCantBeShown_IsNoTry()
    {
        Trust(TeacherA);
        var service = NewService();
        Allow(service);
        _display.Fails = true;

        for (var i = 0; i < NewTeacherInvite.MaxTries + 1; i++)
        {
            Pair(service, TeacherB, ConfirmingWithCode(TeacherB));
        }

        Assert.True(service.NewTeacherOpen);
    }

    // Asking again draws a new code and starts the tries over.
    [Fact]
    public void AllowingAgain_ReplacesTheCode_AndTheTries()
    {
        Trust(TeacherA);
        var service = NewService();
        Allow(service, "AAAAAAAA");
        for (var i = 0; i < NewTeacherInvite.MaxTries - 1; i++)
        {
            Pair(service, TeacherB, Cancelling());
        }

        Allow(service, "BBBBBBBB");
        Pair(service, TeacherB, ConfirmingWithCode(TeacherB, "AAAAAAAA"));
        Assert.True(service.NewTeacherOpen);
        var exchange = ConfirmingWithCode(TeacherB, "BBBBBBBB");
        Pair(service, TeacherB, exchange);

        Assert.Equal(MessageType.PairDone, exchange.Sent.Last().Type);
    }

    // Only a new teacher needs the code: a first one's payload means nothing.
    [Fact]
    public void WithNoTeacher_AProofIsIgnored()
    {
        var exchange = ConfirmingWithCode(TeacherA, "AAAAAAAA");

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(MessageType.PairDone, exchange.Sent.Last().Type);
    }

    [Fact]
    public void TheNewTeacherWindow_Expires()
    {
        Trust(TeacherA);
        var service = NewService();
        Allow(service);

        _now += Pairing.NewTeacherWindow + TimeSpan.FromSeconds(1);

        Assert.False(service.NewTeacherOpen);
        var exchange = ConfirmingWithCode(TeacherB);
        Pair(service, TeacherB, exchange);
        Assert.Equal(new[] { MessageType.PairRefused }, exchange.SentTypes);
    }

    [Fact]
    public void ForgetOtherTeachers_KeepsTheSenderOnly_AndClosesTheWindow()
    {
        Trust(TeacherA, TeacherB, TeacherC);
        var service = NewService();
        Allow(service);

        Assert.Equal((true, "2 professores esquecidos"), service.ForgetOtherTeachers(TeacherB.ToLowerInvariant()));

        Assert.Equal(new[] { TeacherB }, TrustedMastersFile.Load(_path));
        Assert.False(service.NewTeacherOpen);
        Assert.Equal(TeacherStatus.Others, service.TeachersSeenBy(TeacherA));
        var exchange = ConfirmingWithCode(TeacherA);
        Pair(service, TeacherA, exchange);
        Assert.Equal(new[] { MessageType.PairRefused }, exchange.SentTypes);
    }

    [Fact]
    public void ForgetOtherTeachers_WithNoOther_ChangesNothing()
    {
        Trust(TeacherA);

        Assert.Equal((true, "nenhum outro professor"), NewService().ForgetOtherTeachers(TeacherA));
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
    }

    [Fact]
    public void ForgetOtherTeachers_WithAnUnreadableList_IsRefused_AndLeavesItAlone()
    {
        File.WriteAllText(_path, "not json");

        var (succeeded, detail) = NewService().ForgetOtherTeachers(TeacherA);

        Assert.False(succeeded);
        Assert.StartsWith("não foi possível gravar a lista de professores", detail);
        Assert.Equal("not json", File.ReadAllText(_path));
    }
}
