namespace SharpProof.Worker;

// One per request: retirement and target admission share a linearization point.
internal sealed class VerificationTargetScheduler(int targetCount)
{
    private readonly int _targetCount = RequireTargetCount(targetCount);
    private readonly object _synchronization = new();
    private int _nextTarget;
    private bool _hasRetirementReason;
    private int _retirementRank = -1;
    private WorkerCallableCoverageReason _callableReason = WorkerCallableCoverageReason.InfrastructureFailure;
    private WorkerClaimReason _claimReason = WorkerClaimReason.InfrastructureFailure;

    internal bool TryClaim(out int index)
    {
        lock (_synchronization)
        {
            index = -1;
            if (_hasRetirementReason || _nextTarget >= _targetCount)
            {
                return false;
            }
            index = _nextTarget++;
            return true;
        }
    }

    internal void RecordRetirement(WorkerCallableCoverageReason callableReason, WorkerClaimReason claimReason)
    {
        lock (_synchronization)
        {
            // Preserve the strongest failure independently of lane completion order.
            var rank = claimReason switch
            {
                WorkerClaimReason.BackendUnavailable => 2,
                WorkerClaimReason.InfrastructureFailure => 1,
                _ => 0
            };
            if (_hasRetirementReason && rank <= _retirementRank)
            {
                return;
            }
            _callableReason = callableReason;
            _claimReason = claimReason;
            _hasRetirementReason = true;
            _retirementRank = rank;
        }
    }

    internal (WorkerCallableCoverageReason CallableReason, WorkerClaimReason ClaimReason) SnapshotRetirement()
    {
        lock (_synchronization)
        {
            return (_callableReason, _claimReason);
        }
    }

    private static int RequireTargetCount(int targetCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetCount);
        return targetCount;
    }
}
