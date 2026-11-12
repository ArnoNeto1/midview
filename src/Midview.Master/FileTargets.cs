using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Enviar arquivo" and "Recolher arquivos" with phones among the machines.
// A PC's Session (port 5901) saves the teacher's file in "Arquivos do
// professor" and hands over "Entregar ao professor", both on the student's
// Desktop. A phone's agent (port 28900, its own thumbprint) takes the same
// exchanges (SendFile, SendFileWithOptions, CollectFiles,
// ClearDeliveryFolder) for one folder, Download/Midview, which the student
// opens in the Files app: the teacher's files land there, and what is there
// (the teacher's files too) is collected. A phone has no Recycle Bin the
// student can open, so emptying it deletes for good, after the teacher
// says yes. The windows' words and the sending live here, so they can be
// tested.
public static class FileTargets
{
    public const string PhoneFolder = "Download/Midview";

    public static readonly string OldAgentSend = PhoneAgentCommand.OldAgent("recebe arquivos");
    public static readonly string OldAgentCollect = PhoneAgentCommand.OldAgent("entrega arquivos");
    public static readonly string OldAgentClear = PhoneAgentCommand.OldAgent("esvazia a pasta " + PhoneFolder);

    public const string SendPcHint =
        "O arquivo é salvo na pasta \"Arquivos do professor\" na Área de Trabalho do aluno, que recebe um aviso.";

    public const string SendPhoneHint =
        "O arquivo é salvo na pasta Download/Midview do celular, que o aluno abre no app Arquivos, e o aluno recebe um aviso.";

    public const string SendPhoneNote = " Nos celulares, na pasta Download/Midview (app Arquivos).";

    public const string CollectPcHint =
        "Traz o que cada aluno colocou na pasta \"Entregar ao professor\" da Área de Trabalho (subpastas também). Os arquivos continuam no aluno, que recebe um aviso. Até 1000 arquivos e 500 MB por máquina.";

    public const string CollectPhoneHint =
        "Traz o que está na pasta Download/Midview de cada celular (subpastas também), inclusive os arquivos que o professor enviou. Os arquivos continuam no celular, e o aluno recebe um aviso. Até 1000 arquivos e 500 MB por celular.";

    public const string CollectPhoneNote =
        " Nos celulares, a pasta Download/Midview, inclusive os arquivos que o professor enviou.";

    public const string ClearPcHint =
        "Para começar a próxima aula com tudo vazio. Os arquivos vão para a Lixeira, de onde ainda podem ser recuperados.";

    public const string ClearPhoneHint =
        "Para começar a próxima aula com tudo vazio. No celular não há Lixeira: o que está em Download/Midview é apagado de vez.";

    public const string ClearPhoneNote = " No celular não há Lixeira: o que está em Download/Midview é apagado de vez.";

    private static bool OnlyPhones(IReadOnlyCollection<KnownMachine> targets) => CloseAppsTargets.OnlyPhones(targets);

    private static bool SomePhones(IReadOnlyCollection<KnownMachine> targets) => targets.Any(machine => machine.IsPhone);

    // Phones alone: the hint for them; PCs alone: the PC's; both: the PC's
    // and a note for the phones.
    private static string Hint(IReadOnlyCollection<KnownMachine> targets, string pc, string phone, string note) =>
        OnlyPhones(targets) ? phone : SomePhones(targets) ? pc + note : pc;

    public static string SendHint(IReadOnlyCollection<KnownMachine> targets) => Hint(targets, SendPcHint, SendPhoneHint, SendPhoneNote);

    public static string CollectHint(IReadOnlyCollection<KnownMachine> targets) =>
        Hint(targets, CollectPcHint, CollectPhoneHint, CollectPhoneNote);

    public static string ClearHint(IReadOnlyCollection<KnownMachine> targets) => Hint(targets, ClearPcHint, ClearPhoneHint, ClearPhoneNote);

    // "Depois de enviar": the folder and the file, where they are.
    public static string OpenFolderChoice(IReadOnlyCollection<KnownMachine> targets) =>
        OnlyPhones(targets) ? "Abrir a pasta Download/Midview no celular"
        : SomePhones(targets) ? "Abrir a pasta \"Arquivos do professor\" no aluno (Download/Midview no celular)"
        : "Abrir a pasta \"Arquivos do professor\" no aluno";

    public static string OpenFileChoice(IReadOnlyCollection<KnownMachine> targets) =>
        OnlyPhones(targets) ? "Abrir o arquivo no celular" : "Abrir ou executar o arquivo no aluno";

    // Under the heading ("Para 3 celulares: ...").
    public static string Describe(TargetsPreposition preposition, IReadOnlyList<KnownMachine> targets, bool wholeClass) =>
        TargetsText.Describe(preposition, targets, wholeClass, OnlyPhones(targets) ? "celulares" : "máquinas");

    // "Apagar as pastas dos alunos" with phones among them asks first: there
    // it cannot be undone. Null when no phone is among them.
    public static string? ClearQuestion(IReadOnlyCollection<KnownMachine> targets)
    {
        var phones = targets.Count(machine => machine.IsPhone);
        if (phones == 0)
        {
            return null;
        }
        var which = phones == 1 ? "do celular" : $"dos {phones} celulares";
        var pcs = phones < targets.Count ? " Nos computadores, vai para a Lixeira, como sempre." : string.Empty;
        return $"O que está na pasta Download/Midview {which} vai ser apagado de vez (no celular não há Lixeira), inclusive os arquivos que o professor enviou.{pcs}";
    }

    // The question as the window asks it ("Sim, apagar" or "Cancelar",
    // cancelling the default); null when no phone is among them.
    public static QuestionContent? ClearConfirmation(IReadOnlyCollection<KnownMachine> targets) =>
        ClearQuestion(targets) is { } body
            ? new QuestionContent(
                "Apagar as pastas dos alunos", MidviewMessage.IconFor(System.Windows.MessageBoxImage.Warning),
                "Apagar as pastas dos alunos?", "Sim, apagar", "Cancelar", Body: body, NoIsDefault: true,
                Buttons: QuestionButtons.YesFirstDanger)
            : null;

    // Blocks for the connection and the answer: call it off the UI thread.
    // phonePort: only for the tests (a phone's agent is at AgentPort).
    public static (bool Saved, string Detail) Send(
        KnownMachine machine, X509Certificate2 masterCertificate, string filePath, SendFileOptions options,
        int phonePort = ConnectionClient.AgentPort)
    {
        if (machine.IsPhone)
        {
            return PhoneAgentCommand.Send(machine, OldAgentSend, thumbprint => FileSender.Send(
                machine, thumbprint, masterCertificate, filePath, options, phonePort));
        }
        try
        {
            return FileSender.Send(machine, masterCertificate, filePath, options);
        }
        catch (Exception ex)
        {
            return (false, FailureText.Describe(ex));
        }
    }

    // Into destination, as a PC's (FileCollector checks whatever comes).
    public static CollectResult Collect(
        KnownMachine machine, X509Certificate2 masterCertificate, string destination, int phonePort = ConnectionClient.AgentPort)
    {
        if (machine.IsPhone)
        {
            CollectResult? result = null;
            var (succeeded, detail) = PhoneAgentCommand.Send(machine, OldAgentCollect, thumbprint =>
            {
                result = FileCollector.Collect(machine, thumbprint, masterCertificate, destination, phonePort);
                return (result.Succeeded, result.Detail);
            });
            return FromPhone(result, succeeded, detail);
        }
        try
        {
            return FileCollector.Collect(machine, masterCertificate, destination);
        }
        catch (Exception ex)
        {
            return new CollectResult(false, 0, 0, FailureText.Describe(ex));
        }
    }

    // What PhoneAgentCommand made of a phone's collection: the files it
    // saved stay counted, the words are its.
    internal static CollectResult FromPhone(CollectResult? result, bool succeeded, string detail) =>
        new(succeeded, result?.Files ?? 0, result?.Bytes ?? 0, detail);

    public static (bool Succeeded, string Detail) Clear(
        KnownMachine machine, X509Certificate2 masterCertificate, int phonePort = ConnectionClient.AgentPort)
    {
        if (machine.IsPhone)
        {
            return PhoneAgentCommand.Send(machine, OldAgentClear, thumbprint => CommandClient.SendCommandForResult(
                machine.HostAddress, phonePort, thumbprint, masterCertificate,
                MessageType.ClearDeliveryFolder, Array.Empty<byte>()));
        }
        try
        {
            return CommandClient.SendCommandForResult(
                machine.HostAddress, ConnectionClient.SessionPort, machine.SessionThumbprint, masterCertificate,
                MessageType.ClearDeliveryFolder, Array.Empty<byte>());
        }
        catch (Exception ex)
        {
            return (false, FailureText.Describe(ex));
        }
    }
}
