namespace Midview.Master;

// The words of "Papel de parede e PIN da escola", the page of "Preparar
// celulares" that keeps the school's defaults (PhoneDefaultsFile) for the
// devices prepared next.
internal static class PhoneDefaultsText
{
    public const string Heading = "Papel de parede, PIN e senha da escola";

    public static string Intro(AndroidDeviceKind kind) =>
        $"Vão sozinhos para cada {DeviceSetupText.Word(kind)} preparado, assim que ele aparecer no Midview. " +
        $"O papel de parede e o PIN podem ficar para depois, em Configurar {DeviceSetupText.Word(kind)}; a senha de administrador é obrigatória.";

    public const string PinHint =
        "4 números que o aluno não troca. Deixe em branco para não pôr PIN. Fica guardado só neste computador, protegido pelo Windows.";

    public const string NoPicture = "Nenhuma imagem: o papel de parede fica como está.";

    public const string Save = "Guardar e seguir";

    public const string Skip = "Pular";

    public const string ChangeLink = "Papel de parede, PIN e senha";

    // The third card: the phones' admin password (PhoneAdminPassword), the
    // same one as in "Wi-Fi e modo avião" of "Configurar celular".
    public const string AdminTitle = "Senha de administrador dos celulares e tablets";

    public const string AdminLine =
        "Para liberar no próprio aparelho, sem internet, o Wi-Fi, a depuração USB e as contas, ou tirar o Midview. " +
        "Pelo menos 6 letras ou números.";

    public const string AdminRepeat = "Repita a senha";

    // Under the boxes, before anything is typed: nothing saved yet, or one saved.
    // The password is required: without it a device that loses the network
    // cannot be freed, neither by the Midview nor on its own screen.
    public const string AdminHint =
        "Obrigatória. Fica guardada neste computador, protegida pelo Windows: dá para vê-la em Máquinas da sala → Senha de administrador.";

    public const string AdminRequired =
        "Digite a senha de administrador para seguir. Sem ela, um aparelho que ficar sem rede não pode ser destravado nem pelo Midview nem na própria tela.";

    public const string AdminSavedHint =
        "Já há uma senha guardada. Deixe em branco para mantê-la, ou digite outra.";

    public const string AdminRemovedHint = "Nenhuma senha vai ser posta nos próximos preparados.";

    // The link beside a saved password.
    public const string AdminRemoveLink = "Não pôr senha";

    public const string AdminMismatch = "As duas senhas de administrador não são iguais. Digite a mesma nas duas.";

    public const string AdminInvalid =
        "A senha de administrador tem de 6 a 12 letras ou números. Complete-a ou apague as duas para não pôr senha.";

    // What a hint under the boxes says while typing (PhoneNetworkText's
    // count of what is missing), or the hint for the saved state.
    public static string AdminTyping(string typed, bool saved, bool removed) =>
        PhoneNetworkText.Clean(typed).Length > 0
            ? PhoneNetworkText.PasswordHint(typed) is { Length: > 0 } missing ? missing : "Senha completa."
            : removed ? AdminRemovedHint : saved ? AdminSavedHint : AdminHint;

    // Null when the two boxes are fine to save (the same valid password, or
    // both empty when one is saved already); else what the page says.
    public static string? AdminProblem(string password, string repeat, bool saved = false)
    {
        if (password.Length == 0 && repeat.Length == 0)
        {
            return saved ? null : AdminRequired;
        }
        if (!PhoneNetworkText.IsPasswordReady(password))
        {
            return AdminInvalid;
        }
        return password == repeat ? null : AdminMismatch;
    }

    // The question "Otimizar" asks when no admin password is saved yet
    // (OptimizeAdminWindow).
    public const string AskLine =
        "Ainda não há senha guardada. Com ela, quando o aparelho ficar sem rede, alguém da escola libera no próprio aparelho " +
        "o Wi-Fi, a depuração USB e as contas, ou tira o Midview. O Otimizar põe a senha em cada celular e tablet.";

    public const string AskHint =
        "De 6 a 12 letras ou números. Fica guardada neste computador, protegida pelo Windows, a mesma do Preparar celulares: dá para vê-la em Máquinas da sala → Senha de administrador.";

    public const string AskSave = "Guardar e otimizar";

    public const string AskSkip = "Otimizar sem senha";

    public const string AskEmpty = "Digite a senha nas duas caixas, ou aperte \"Otimizar sem senha\".";

    // The same question in "Senha de administrador" of "Máquinas da sala"
    // (change), which puts the password on every device of the room.
    public const string ChangeLine =
        "A mesma senha vai para todos os celulares e tablets da sala que estão ligados agora; os outros recebem sozinhos assim que aparecerem online. " +
        "Com ela, alguém da escola libera no próprio aparelho, sem rede, o Wi-Fi, a depuração USB e as contas, ou tira o Midview.";

    public const string ChangeSave = "Guardar e pôr nos aparelhos";

    public const string ChangeCancel = "Cancelar";

    public const string ChangeEmpty = "Digite a senha nas duas caixas.";

    // Null when the two boxes hold the same valid password; else what the question says.
    public static string? AskProblem(string password, string repeat, bool change = false) =>
        password.Length == 0 && repeat.Length == 0 ? (change ? ChangeEmpty : AskEmpty)
        : !PhoneNetworkText.IsPasswordReady(password) ? AskInvalid
        : password == repeat ? null : AdminMismatch;

    public const string AskInvalid = "A senha de administrador tem de 6 a 12 letras ou números.";

    // The card of the phones and tablets in "Senha de administrador" (MachinesWindow).
    public const string CardTitle = "Celulares e tablets";

    public const string CardLine =
        "Para liberar no próprio aparelho, sem rede, o Wi-Fi, a depuração USB e as contas, ou tirar o Midview. " +
        "A mesma senha para todos os celulares e tablets da sala.";

    public static string CardStatus(bool saved) =>
        saved ? "Uma senha está guardada neste computador." : "Nenhuma senha guardada ainda.";

    public const string ShowButton = "Mostrar a senha";

    // "Mostrar a senha": the saved password, or why it can't be shown (one
    // saved before it could be).
    public static string Shown(string? text) =>
        text is null
            ? "Esta senha foi guardada antes de dar para vê-la. Se você não lembra dela, aperte \"Trocar a senha...\" e digite uma nova: ela vai para todos os aparelhos."
            : $"A senha de administrador dos celulares e tablets é:\n\n{text}";

    public const string Sending = "Pondo a senha nos aparelhos...";

    // After the password was saved and sent to the room's devices: put on
    // how many of how many.
    public static string Sent(int put, int total) =>
        total == 0 ? "Senha guardada. Não há celulares nem tablets na sala ainda: cada um recebe ao ser preparado."
        : put == total ? (total == 1 ? "Senha posta no aparelho da sala." : $"Senha posta nos {total} aparelhos da sala.")
        : $"Senha posta em {put} de {total} aparelhos. Os desligados ou fora da rede recebem sozinhos assim que aparecerem online.";

    public const string RemoveQuestion =
        "Parar de pôr a senha nos próximos aparelhos? Os celulares e tablets que já têm a senha continuam com ela.";

    // "a", "a e b", "a, b e c".
    public static string List(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => "",
        1 => parts[0],
        _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} e {parts[^1]}",
    };
}
