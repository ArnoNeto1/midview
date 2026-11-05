using System.IO;
using System.Net.Http;
using System.Windows;

namespace Midview.Master;

// The two questions around "Atualizar professor" (TeacherUpdate) that the
// classroom window asks on its own: a newer version on GitHub as the Midview
// opens, and, once the update put it on this PC, the students' machines.
public partial class MainWindow
{
    // Asked every time the Midview opens, until the teacher updates: a "no"
    // is only for now. Quiet when this copy isn't an installed teacher's
    // (a build run from the source), when GitHub can't be reached in 20 s,
    // or when this PC has the newest version: the teacher sees nothing then.
    public async void OfferTeacherUpdate()
    {
        if (TeacherUpdate.InstalledProgram(out _) is null)
        {
            return;
        }

        TeacherUpdate.Release release;
        try
        {
            using var client = TeacherUpdate.CreateClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            release = await TeacherUpdate.FetchLatestAsync(client, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or OperationCanceledException)
        {
            return;
        }

        var current = TeacherUpdate.CurrentVersion;
        if (!IsVisible || TeacherUpdate.Compare(current, release.Version) != TeacherUpdate.Standing.Newer)
        {
            return;
        }
        if (QuestionWindow.Ask(this, TeacherUpdate.NewVersionContent(current, release.Version)))
        {
            OpenMachinesWindow(search: false);
            _machinesWindow?.StartTeacherUpdate(release);
        }
    }

    // After an update that worked: the students' machines get the new
    // version only from here, so the teacher is asked right away.
    public void OfferStudentUpdate()
    {
        if (QuestionWindow.Ask(this, "Atualizar professor", "Quer atualizar as máquinas dos alunos agora?",
                "Atualizar os alunos", "Agora não"))
        {
            OpenMachinesWindow(search: false);
            _machinesWindow?.OpenStudentUpdate();
        }
    }
}
