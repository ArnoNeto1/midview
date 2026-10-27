namespace Midview.Master;

// What the "Desligar"/"Reiniciar" window says (PowerWindow), kept apart
// from it so it can be tested.
public static class PowerText
{
    public static string RunButton(bool restart, bool installUpdates) =>
        (restart ? "Reiniciar" : "Desligar") + (installUpdates ? " instalando as atualizações" : string.Empty);

    // Under "Instalar as atualizações do Windows que estiverem esperando":
    // it is Windows' own option, so it installs only what Windows already
    // downloaded, and the teacher should expect the wait.
    public static string UpdatesHint(bool restart) =>
        $"Como \"{(restart ? "Atualizar e reiniciar" : "Atualizar e desligar")}\" no menu Iniciar do Windows: " +
        "instala só o que o Windows já baixou. Pode levar alguns minutos a mais em cada máquina. " +
        $"Máquinas sem atualização esperando só {(restart ? "reiniciam" : "desligam")}.";

    // One machine's line. An Agent from before refuses the request that
    // installs the updates as invalid (PowerCommand): that says why.
    public static string Detail(bool succeeded, string detail, bool installUpdates) =>
        !succeeded && installUpdates && detail == "pedido inválido"
            ? "Midview antigo nesta máquina: atualize-a pela rede (Máquinas da sala) para instalar as atualizações ao desligar"
            : detail;
}
