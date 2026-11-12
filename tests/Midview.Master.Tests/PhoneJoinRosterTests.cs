using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PhoneJoinRosterTests
{
    private static readonly string Agent = new('A', 40);

    private static KnownMachine Joined() => new("Celular 07", "10.0.0.7", new string('5', 40), Agent, null, DeviceKind.Android);

    [Fact]
    public void AddAndSave_says_nothing_when_it_works() =>
        Assert.Null(PhoneJoinRoster.AddAndSave(() => { }));

    [Fact]
    public void AddAndSave_turns_a_save_failure_into_a_status_text()
    {
        var text = PhoneJoinRoster.AddAndSave(() => throw new IOException("disco cheio"));
        Assert.NotNull(text);
        Assert.Contains("entrou na turma", text);
        Assert.Contains("disco cheio", text);
    }

    [Fact]
    public void ReplacingSameName_swaps_the_phone_but_not_a_computer()
    {
        var oldPhone = new KnownMachine("Celular 07", "10.0.0.1", new string('1', 40), Agent, null, DeviceKind.Android);
        var pcSameName = new KnownMachine("Celular 07", "10.0.0.2", new string('2', 40), null);
        var other = new KnownMachine("Celular 08", "10.0.0.3", new string('3', 40), Agent, null, DeviceKind.Android);

        var result = PhoneJoinRoster.ReplacingSameName(new[] { oldPhone, pcSameName, other }, Joined());

        Assert.Equal(new[] { pcSameName, other, Joined() }, result);
    }

    [Fact]
    public void ReplacingSameName_keeps_another_phone_of_the_class()
    {
        var other = new KnownMachine("Celular 03", "10.0.0.3", new string('3', 40), Agent, null, DeviceKind.Android);

        var after = PhoneJoinRoster.ReplacingSameName(new[] { other }, Joined());

        Assert.Equal(new[] { "Celular 03", "Celular 07" }, after.Select(m => m.Name).ToArray());
    }
}
