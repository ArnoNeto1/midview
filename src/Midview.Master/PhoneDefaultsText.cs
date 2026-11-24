namespace Midview.Master;

// The words of "Papel de parede e PIN da escola", the page of "Preparar
// celulares" that keeps the school's defaults (PhoneDefaultsFile) for the
// devices prepared next.
internal static class PhoneDefaultsText
{
    public const string Heading = "Papel de parede e PIN da escola";

    public static string Intro(AndroidDeviceKind kind) =>
        $"Vão sozinhos para cada {DeviceSetupText.Word(kind)} preparado, assim que ele aparecer no Midview. " +
        $"Não quer agora? Pule: dá para escolher depois em Configurar {DeviceSetupText.Word(kind)}.";

    public const string PinHint =
        "4 números que o aluno não troca. Deixe em branco para não pôr PIN. Fica guardado só neste computador, protegido pelo Windows.";

    public const string NoPicture = "Nenhuma imagem: o papel de parede fica como está.";

    public const string Save = "Guardar e seguir";

    public const string Skip = "Pular";

    public const string ChangeLink = "Papel de parede e PIN";

    // The third card: the phones' admin password (PhoneAdminPassword), the
    // same one as in "Wi-Fi e modo avião" of "Configurar celular".
    public const string AdminTitle = "Senha de administrador dos celulares";

    public const string AdminLine =
        "Para liberar no próprio aparelho, sem internet, o Wi-Fi, a depuração USB e as contas, ou tirar o Midview. " +
        "Pelo menos 6 letras ou números.";

    public const string AdminRepeat = "Repita a senha";

    // Under the boxes, before anything is typed: nothing saved yet, or one saved.
    public const string AdminHint =
        "Deixe em branco para não pôr senha. Fica guardada só uma impressão dela neste computador: anote-a.";

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

    // Null when the two boxes are fine to save (both empty, or the same
    // valid password); else what the page says.
    public static string? AdminProblem(string password, string repeat)
    {
        if (password.Length == 0 && repeat.Length == 0)
        {
            return null;
        }
        if (!PhoneNetworkText.IsPasswordReady(password))
        {
            return AdminInvalid;
        }
        return password == repeat ? null : AdminMismatch;
    }

    // "a", "a e b", "a, b e c".
    public static string List(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => "",
        1 => parts[0],
        _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} e {parts[^1]}",
    };
}
