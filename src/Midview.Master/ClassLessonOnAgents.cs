using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// The class lesson straight on the Agents, read and sent the same way by the
// class folders' home (ClassFolderSync) and by a colleague's teacher PC that
// ends a lesson with the home's Master closed (RemoteClassFolderController):
// what an Agent's answer means, and which of the Agents' lessons counts.
internal static class ClassLessonOnAgents
{
    // A lesson saved further ahead of this PC's clock than this is not believed.
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(1);

    // The newest by SavedAt of those not saved more than Horizon after
    // unixNow (this PC's clock, Unix seconds); null for none.
    public static ClassLesson? Newest(IEnumerable<ClassLesson?> lessons, long unixNow)
    {
        var horizon = unixNow + (long)Horizon.TotalSeconds;
        return lessons.OfType<ClassLesson>().Where(lesson => lesson.SavedAt <= horizon).MaxBy(lesson => lesson.SavedAt);
    }

    // The newest lesson those machines' Agents follow (Newest), each asked on
    // a thread of its own, the caller's blocked meanwhile: what the toolbar
    // of a PC that isn't the home shows (ClassFolderRole). Null for none.
    public static ClassLesson? NewestOf(
        IEnumerable<KnownMachine> machines, X509Certificate2 masterCertificate, int agentPort, Action<string> log, long unixNow) =>
        Newest(
            Task.WhenAll(machines.Select(machine => Task.Factory.StartNew(
                    () => Read(machine, masterCertificate, agentPort, log), CancellationToken.None,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default)))
                .GetAwaiter().GetResult(),
            unixNow);

    // The lesson the machine's Agent follows; null when it gave no answer or
    // is from before the class folder.
    public static ClassLesson? Read(KnownMachine machine, X509Certificate2 masterCertificate, int agentPort, Action<string> log)
    {
        try
        {
            return ClassFolderClient.GetLesson(machine, masterCertificate, agentPort).Lesson;
        }
        catch (Exception ex)
        {
            LogUnexpected(log, ex, $"Reading the lesson of {machine.Name} failed");
            return null;
        }
    }

    // Whether the machine's Agent took the lesson, and what it said. An Agent
    // that keeps a newer lesson answers yes, saying so: it didn't take this
    // one, which is a no (its detail says why, rather than "atualizando a aula").
    public static (bool Ok, string Detail) Send(KnownMachine machine, X509Certificate2 masterCertificate, ClassLesson lesson, int agentPort)
    {
        try
        {
            var (succeeded, detail) = ClassFolderClient.SetLesson(machine, masterCertificate, lesson, agentPort);
            return (succeeded && !detail.StartsWith(ClassLesson.KeptNewerText, StringComparison.Ordinal), detail);
        }
        catch (Exception ex)
        {
            return (false, FailureText.DescribeAgent(ex));
        }
    }

    // A machine off, unreachable or not trusting this teacher is no news.
    public static void LogUnexpected(Action<string> log, Exception ex, string what)
    {
        if (ex is not (SocketException or IOException or AuthenticationException or OperationCanceledException))
        {
            log($"{what}: {ex}");
        }
    }
}
