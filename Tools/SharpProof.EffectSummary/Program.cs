using SharpProof.Tools.Shared;

return ToolCommandHost.Run(
    () => EffectSummaryCli.Run(args),
    argumentErrorExitCode: 2,
    Console.Error);
