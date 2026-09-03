using System.Collections.Immutable;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal static class WorkerTestData
{
    internal static readonly ImmutableArray<WorkerAssumptionKind>
        UserAndTrustedAssumptions = [
            WorkerAssumptionKind.UserAssume,
            WorkerAssumptionKind.TrustedBoundary
        ];
}
