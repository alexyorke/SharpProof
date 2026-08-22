namespace SharpProof.BuildTasks;

internal static class Program
{
    private const string SupervisorArgument = "--supervise-verifier";
    private const string WorkerArgument = "--run-verifier-child";

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
            WorkerArgument =>
                VerifierProcessSupervisor.RunWorker(arguments[1..]),
            _ => 2
        };
    }
}
