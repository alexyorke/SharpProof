namespace SharpProof.Frontend;

// One actual region-work account is shared by the IR lowerer and its planner.
// The source-call session, when present, remains the owner of the shared budget.
internal sealed class TotalRegionWork(CancellationToken cancellationToken, TotalSourceCallSession? calls = null)
{
    private int _remaining = RoslynTotalProgramLowerer.MaximumRegionSteps;
    internal bool ConstructionLimitExceeded { get; private set; }

    internal void Spend()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (calls != null ? !calls.Spend() : --_remaining < 0)
        {
            ConstructionLimitExceeded = true;
            throw new RoslynTotalProgramLowerer.RegionIncompleteException();
        }
    }
}
