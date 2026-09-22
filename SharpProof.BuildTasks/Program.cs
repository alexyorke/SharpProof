namespace SharpProof.BuildTasks;

internal static class Program
{
    internal const string SupervisorArgument = "--supervise-verifier";
    internal const string WorkerArgument = "--run-verifier-child";

    private static int Main(string[] arguments)
    {
        if (arguments.Length < 2)
        {
            return 2;
        }
        return arguments[0] switch
        {
            SupervisorArgument =>
                VerifierProcessSupervisor.Run(arguments[1..]),
            WorkerArgument when arguments.Length >= 3 &&
                int.TryParse(arguments[1], out var parent) && parent > 1 =>
                VerifierProcessSupervisor.RunWorker(parent, arguments[2..]),
            _ => 2
        };
    }
}
