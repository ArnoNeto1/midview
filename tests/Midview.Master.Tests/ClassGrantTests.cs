using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class ClassGrantTests
{
    private static readonly string Session = new('5', 40);

    private static KnownMachine Machine(int number, char agent, string? mac = null) =>
        new($"Aluno-{number}", $"10.0.0.{number}", Session, new string(agent, 40), mac);

    [Fact]
    public void Give_ListsWhoTookIt_InClassroomOrder_AndWhyTheOthersDidNot()
    {
        var offline = new SocketException((int)SocketError.TimedOut);
        var roster = new[]
        {
            Machine(1, 'A', "00-1A-2B-3C-4D-5E"),
            Machine(2, 'B'),
            Machine(3, 'C'),
            Machine(4, 'D'),
            new KnownMachine("Aluno-5", "10.0.0.5", Session, null),
            Machine(6, 'E'),
        };
        var told = new List<string>();

        var result = ClassGrant.Give(roster, machine =>
        {
            lock (told)
            {
                told.Add(machine.Name);
            }
            return machine.Name switch
            {
                "Aluno-2" => (false, string.Empty),
                "Aluno-3" => throw offline,
                "Aluno-4" => (false, "não foi possível gravar o professor nesta máquina: disco cheio"),
                _ => (true, "liberada"),
            };
        });

        Assert.Equal(new[]
        {
            new InvitedMachine("Aluno-1", "10.0.0.1", Session, new string('A', 40), "00-1A-2B-3C-4D-5E"),
            new InvitedMachine("Aluno-6", "10.0.0.6", Session, new string('E', 40), null),
        }, result.Given);
        Assert.Equal(new[]
        {
            new LeftOutMachine("Aluno-2", ClassGrant.OldAgent),
            new LeftOutMachine("Aluno-3", FailureText.DescribeAgent(offline)),
            new LeftOutMachine("Aluno-4", "não foi possível gravar o professor nesta máquina: disco cheio"),
            new LeftOutMachine("Aluno-5", ClassGrant.NoAgentIdentity),
        }, result.LeftOut);
        Assert.DoesNotContain("Aluno-5", told);
    }

    [Fact]
    public void Give_SendsThumbprintsUpperCase_AndAddressesAsTheyAre()
    {
        var roster = new[] { new KnownMachine("Aluno-1", "PC-ALUNO-1", new string('e', 40), new string('a', 40)) };

        var result = ClassGrant.Give(roster, _ => (true, "liberada"));

        Assert.Equal(new InvitedMachine("Aluno-1", "PC-ALUNO-1", new string('E', 40), new string('A', 40), null), result.Given.Single());
    }

    [Fact]
    public void Give_LeavesOutAMachineWhoseThumbprintsAreNotOnes()
    {
        var roster = new[] { new KnownMachine("Aluno-1", "10.0.0.1", "not a thumbprint", new string('A', 40)) };

        var result = ClassGrant.Give(roster, _ => throw new InvalidOperationException("must not be told"));

        Assert.Equal(new[] { new LeftOutMachine("Aluno-1", ClassGrant.NoAgentIdentity) }, result.LeftOut);
    }

    [Fact]
    public void Give_WithAnEmptyList_GivesNothing()
    {
        Assert.Equal(0, ClassGrant.Give(Array.Empty<KnownMachine>(), _ => (true, "liberada")).Total);
    }
}
