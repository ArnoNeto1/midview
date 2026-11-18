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
}
