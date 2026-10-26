using System.Collections.Immutable;
using SharpProof.Fuzz;
using SharpProof.Host;

using var cancellation = new ConsoleCancellationScope();
return await FuzzRunner.RunCliAsync(
    args.ToImmutableArray(),
    Console.Out,
    Console.Error,
    cancellation.Token);
