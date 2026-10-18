using SharpProof.Fuzz;
using SharpProof.Host;

using var cancellation = new ConsoleCancellationScope();
return await FuzzRunner.RunCliAsync(
    args,
    Console.Out,
    Console.Error,
    cancellation.Token);
