using System.Globalization;

namespace SharpProof.BuildTasks;

internal static class Program
{
    private const string SupervisorArgument = "--supervise-verifier";
    private const string SupervisorParentArgument = "--supervisor-parent-pid";
    private const string WorkerArgument = "--run-verifier-child";

    private static int Main(string[] arguments)
    {
        if (arguments.Length < 2)
        {
            return 2;
        }
        return arguments[0] switch
        {
            SupervisorArgument => RunSupervisor(arguments[1..]),
            WorkerArgument =>
                VerifierProcessSupervisor.RunWorker(arguments[1..]),
            _ => 2
        };
    }

    private static int RunSupervisor(string[] arguments)
    {
        if (arguments.Length < 3 ||
            !string.Equals(
                arguments[0],
                SupervisorParentArgument,
                StringComparison.Ordinal) ||
            !int.TryParse(
                arguments[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parentProcessId))
        {
            return 125;
        }

        return VerifierProcessSupervisor.Run(
            arguments[2..],
            parentProcessId);
    }
}
