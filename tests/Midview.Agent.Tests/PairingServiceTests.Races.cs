using System.Text;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

// What changes while a pairing waits for the teacher to compare the codes:
// right before trusting, the Agent checks again that what let the pairing in
// still holds.
public partial class PairingServiceTests
{
    // Confirms (with a proof of code, when given) after something happened
    // on the machine while the teacher compared the codes.
    private static Exchange ConfirmingAfter(Action meanwhile, string master, string? code = null)
    {
        var exchange = new Exchange();
        return exchange
            .Then(MessageType.PairNonce, MasterNonce)
            .Then(() =>
            {
                meanwhile();
                var proof = code is null
                    ? Array.Empty<byte>()
                    : NewTeacherInvite.Proof(
                        NewTeacherInvite.Key(code, AgentThumbprint), master, AgentThumbprint, exchange.Sent[1].Payload, MasterNonce);
                return new Message(MessageType.PairConfirm, proof);
            });
    }

    // A trusted teacher about to be forgotten keeps a pairing pending: it
    // must not come back once forgotten.
    [Fact]
    public void ATeacherForgottenWhilePairingAgain_IsNotAddedBack()
    {
        Trust(TeacherA, TeacherB);
        var service = NewService();

        var exchange = ConfirmingAfter(() => service.ForgetOtherTeachers(TeacherA), TeacherB);
        Pair(service, TeacherB, exchange);

        Assert.Equal(PairingService.NoLongerAllowed, Pairing.RefusalText(exchange.Sent.Last().Payload));
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
    }

    [Fact]
    public void ACodeDrawnAgainWhilePairing_TurnsTheOldOneAway()
    {
        Trust(TeacherA);
        var service = NewService();
        Allow(service, "AAAAAAAA");

        var exchange = ConfirmingAfter(() => Allow(service, "BBBBBBBB"), TeacherB, "AAAAAAAA");
        Pair(service, TeacherB, exchange);

        Assert.Equal(PairingService.NoLongerAllowed, Pairing.RefusalText(exchange.Sent.Last().Payload));
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
        // The new code still opens the machine.
        Assert.True(service.NewTeacherOpen);
    }

    [Fact]
    public void AWindowClosedWhilePairing_TurnsTheCodeHolderAway()
    {
        Trust(TeacherA);
        var service = NewService();
        Allow(service);

        var exchange = ConfirmingAfter(() => service.ForgetOtherTeachers(TeacherA), TeacherB, Code);
        Pair(service, TeacherB, exchange);

        Assert.Equal(PairingService.NoLongerAllowed, Pairing.RefusalText(exchange.Sent.Last().Payload));
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
    }

    [Fact]
    public void AWindowThatEndedWhilePairing_TurnsTheCodeHolderAway()
    {
        Trust(TeacherA);
        var service = NewService();
        Allow(service);
        _now += Pairing.NewTeacherWindow - TimeSpan.FromMinutes(1);

        var exchange = ConfirmingAfter(() => _now += TimeSpan.FromMinutes(2), TeacherB, Code);
        Pair(service, TeacherB, exchange);

        Assert.Equal(PairingService.NoLongerAllowed, Pairing.RefusalText(exchange.Sent.Last().Payload));
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
    }

    // Two teachers forget each other at once: the one forgotten first must
    // not end up the only teacher.
    [Fact]
    public void ForgetOtherTeachers_FromATeacherForgottenAlready_ChangesNothing()
    {
        Trust(TeacherA, TeacherB);
        var service = NewService();
        service.ForgetOtherTeachers(TeacherA);

        Assert.Equal((false, OtherTeachers.NotOnList), service.ForgetOtherTeachers(TeacherB));
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
    }

    // Until the teacher's nonce is in, the connection keeps its short
    // timeout: holding the one pairing costs a code on the screen.
    [Fact]
    public void TheLongWait_StartsOnlyWithTheTeachersNonce()
    {
        var service = NewService();
        var longWaits = 0;

        service.Handle(TeacherA, AgentThumbprint, Encoding.UTF8.GetBytes("PROF-01"),
            new Exchange().Then(MessageType.PairCancel).Receive, _ => { }, null, () => longWaits++);
        Assert.Equal(0, longWaits);
        Assert.Throws<IOException>(() => service.Handle(TeacherA, AgentThumbprint, Encoding.UTF8.GetBytes("PROF-01"),
            new Exchange().Receive, _ => { }, null, () => longWaits++));
        Assert.Equal(0, longWaits);

        var exchange = Confirming();
        service.Handle(TeacherA, AgentThumbprint, Encoding.UTF8.GetBytes("PROF-01"), exchange.Receive, exchange.Send, null, () => longWaits++);
        Assert.Equal(1, longWaits);
        Assert.Equal(MessageType.PairDone, exchange.Sent.Last().Type);
    }
}
