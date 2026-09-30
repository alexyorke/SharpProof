namespace SharpProof.Worker;

internal sealed record WorkerVcShadowAssumption(string Id, WorkerAssumptionKind Kind, bool Used);

internal sealed record WorkerVcShadowRow(string CallableId, string ClaimId,
    WorkerClaimOutcome OldOutcome, WorkerVacuityKind OldVacuity, ImmutableArray<WorkerVcShadowAssumption> OldAssumptions,
    bool TotalPresent, bool Enrolled, bool Checked, WorkerClaimOutcome NewOutcome, WorkerClaimReason NewReason,
    WorkerVacuityKind NewVacuity, PassiveCallableFeasibilityKind Feasibility,
    ImmutableArray<WorkerVcShadowAssumption> NewAssumptions, bool HasBodyAssumptions)
{
    public bool OldConditional => Conditional(OldAssumptions);
    public bool NewConditional => HasBodyAssumptions || Conditional(NewAssumptions);
    public bool SoundnessDisagreement => OldVacuity == WorkerVacuityKind.None && NewVacuity == WorkerVacuityKind.None &&
        !OldConditional && !NewConditional && Checked &&
        (OldOutcome == WorkerClaimOutcome.Proven && NewOutcome == WorkerClaimOutcome.Refuted ||
         OldOutcome == WorkerClaimOutcome.Refuted && NewOutcome == WorkerClaimOutcome.Proven);
    public bool PrecisionGain => Checked && NewVacuity == WorkerVacuityKind.None && !NewConditional &&
        NewOutcome is WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted &&
        OldOutcome == WorkerClaimOutcome.Unknown;
    public bool PrecisionLoss => OldVacuity == WorkerVacuityKind.None && !OldConditional &&
        OldOutcome is WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted &&
        (!Checked || NewOutcome == WorkerClaimOutcome.Unknown);

    private static bool Conditional(ImmutableArray<WorkerVcShadowAssumption> assumptions)
    {
        return assumptions.Any(assumption => assumption.Used && assumption.Kind is
            WorkerAssumptionKind.UserAssume or WorkerAssumptionKind.TrustedBoundary or WorkerAssumptionKind.ApiSpecification);
    }
}

// Observational diagnostics only. No model, source text or candidate claim is
// inserted into the authoritative response or cache entry.
internal sealed record WorkerVcShadowReport(string InputHash, string RequestHash, WorkerCacheStatus CacheStatus, ImmutableArray<WorkerVcShadowRow> Rows)
{
    internal const string Prefix = "SharpProof vc-shadow ";
    public int SchemaVersion { get; } = 1;
    public string Authority { get; } = "legacy";
    public int Postconditions => Rows.Length;
    public int Enrolled => Rows.Count(row => row.Enrolled);
    // Checked means a completed Ensures query or kernel-validated vacuity.
    // Completed bounded witness searches may abstain. Skipped Ensures and
    // interrupted/refused queries stay unchecked.
    public int Checked => Rows.Count(row => row.Checked);
    public int Unchecked => Postconditions - Checked;
    public int Unenrolled => Postconditions - Enrolled;
    public int Unknown => Rows.Count(row => row.NewOutcome == WorkerClaimOutcome.Unknown);
    public int OldProven => Rows.Count(row => row.OldOutcome == WorkerClaimOutcome.Proven);
    public int NewProven => Rows.Count(row => row.NewOutcome == WorkerClaimOutcome.Proven);
    public int SoundnessDisagreements => Rows.Count(row => row.SoundnessDisagreement);
    public int PrecisionGains => Rows.Count(row => row.PrecisionGain);
    public int PrecisionLosses => Rows.Count(row => row.PrecisionLoss);
    public int OldVacuous => Rows.Count(row => row.OldVacuity != WorkerVacuityKind.None);
    public int NewVacuous => Rows.Count(row => row.NewVacuity != WorkerVacuityKind.None);
    public int OldConditional => Rows.Count(row => row.OldConditional);
    public int NewConditional => Rows.Count(row => row.NewConditional);
    public bool CoverageComplete => Postconditions != 0 && Checked == Postconditions && Enrolled == Postconditions;

    internal string Serialize()
    {
        return Prefix + JsonSerializer.Serialize(this, WorkerProtocolJson.SharedOptions);
    }
}
