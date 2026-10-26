using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace SharpProof.Worker.Protocol;

// Semantic consistency of resource-bound claim evidence in a worker response.
internal static partial class WorkerProtocolJson
{
    private static bool HasValidResourceEvidence(WorkerClaimManifestEntry? claim, WorkerClaimResult value)
    {
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        var resource = claim?.ResourceUpperBound;
        if (resource is null)
        { return value.ResourceBound is null; }
        if (!WorkerProtocolMetadata.HasValidResourceConstraint(claim!.EffectContractKind, resource))
        { return false; }
        var evidence = value.ResourceBound;
        if (evidence is null)
        { return value.Outcome == WorkerClaimOutcome.Unknown || value.Outcome == WorkerClaimOutcome.Proven && value.Vacuity != WorkerVacuityKind.None; }
        if (evidence.Metric != resource.Metric || evidence.CostModel != resource.CostModel ||
            evidence.ExpressionModel != WorkerResourceModels.Expression || evidence.ProgramModel != WorkerResourceModels.Program ||
            evidence.DeclaredBoundSha256 != ComputeSha256(resource.UpperBound.ToImmutableArray()))
        { return false; }
        if (evidence.Metric is WorkerResourceMetric.SourceActivationDepth or WorkerResourceMetric.SourceStackBytes && value.Outcome == WorkerClaimOutcome.Refuted)
        { return false; }
        if (evidence.Metric is WorkerResourceMetric.AllocatedBytes or WorkerResourceMetric.PeakAuxiliaryBytes or WorkerResourceMetric.RetainedBytes && value.Outcome is WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted &&
            value.Vacuity == WorkerVacuityKind.None && (evidence.MetadataAssumptions is null ||
                !evidence.MetadataAssumptions.Any(static dependency => dependency is not null &&
                    dependency.WitnessIdentifier == "runtime-layout.coreclr-x64" && dependency.RuntimeModel == WorkerResourceModels.AllocationLayout)))
        { return false; }
        if (value.Outcome == WorkerClaimOutcome.Proven && value.Vacuity == WorkerVacuityKind.None
            ? !HasValidResourceAsymptotic(evidence.Asymptotic) || !HasValidResourceLowerAsymptotic(evidence.Asymptotic?.Lower, evidence.Asymptotic) : evidence.Asymptotic is not null)
        { return false; }
        if (!HasValidResourceMetadata(claim!, value, evidence) || !HasValidResourceScope(value, evidence) ||
            !HasValidTaskRuntimePremises(claim!, value, evidence))
        { return false; }
        if (!HasValidResourceExpected(value, evidence.Expected))
        { return false; }
        if (!HasValidResourceAccounting(value, evidence.Accounting))
        { return false; }
        return value.Outcome switch
        {
            WorkerClaimOutcome.Proven => evidence.ObservedLowerBound is null && evidence.EvaluatedUpperBound is null && evidence.UnknownReason is null && !string.IsNullOrWhiteSpace(evidence.InferredUpperBound) && evidence.InferredUpperBound!.Length <= 16384,
            WorkerClaimOutcome.Refuted => evidence.InferredUpperBound is null && evidence.UnknownReason is null &&
                (evidence.Metric == WorkerResourceMetric.LoopBodyEntries ? value.EffectWitness?.Effects == WorkerEffectSet.None :
                    value.EffectWitness?.Effects == WorkerEffectSet.Allocates) && HasValidResourceViolation(evidence),
            WorkerClaimOutcome.Unknown => evidence.ObservedLowerBound is null && evidence.EvaluatedUpperBound is null && evidence.InferredUpperBound is null && !string.IsNullOrWhiteSpace(evidence.UnknownReason) && evidence.UnknownReason!.Length <= 256 && !evidence.UnknownReason.Any(static character => char.IsControl(character)),
            _ => false
        };
    }
    private static bool HasValidResourceScope(WorkerClaimResult result, WorkerResourceBoundEvidence evidence)
    {
        result = ArgumentNullGuard.NotNull(result, nameof(result));
        evidence = ArgumentNullGuard.NotNull(evidence, nameof(evidence));
        var dependencies = evidence.MetadataAssumptions.Where(static dependency => dependency is not null &&
            dependency.RuntimeModel is WorkerResourceModels.TaskJoinModel or WorkerResourceModels.ConfiguredTaskJoinModel).ToArray();
        var storage = evidence.MetadataAssumptions.Where(static dependency => dependency is not null &&
            dependency.RuntimeModel is WorkerResourceModels.TaskStorageModel or WorkerResourceModels.ConfiguredTaskStorageModel).ToArray();
        var tokens = EmptyIfDefault(result.ProofCore).Where(static item => item is not null && item.StartsWith("resource-task-", StringComparison.Ordinal)).ToArray();
        if (evidence.ExecutionScope == WorkerResourceModels.CallableScope)
        { return dependencies.Length == 0 && storage.Length == 0 && tokens.Length == 0; }
        if (result.Outcome != WorkerClaimOutcome.Proven || dependencies.Length != 1 || dependencies[0].WitnessIdentifier != "bcl.task.run-action.when-all-array" ||
            evidence.Asymptotic?.Lower?.UnknownReason is null || evidence.Accounting is not { Length: 0 } || evidence.Expected?.UnknownReason is null ||
            !OneTaskToken(tokens, "resource-task-model:", dependencies[0].RuntimeModel))
        { return false; }
        const string prefix = "resource-task-projection:";
        var projections = tokens.Where(static item => item is not null && item.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        if (projections.Length != 1 || !IsSha256(projections[0].Substring(prefix.Length)))
        { return false; }
        if (evidence.ExecutionScope == WorkerResourceModels.TaskJoinScope)
        { return evidence.Metric == WorkerResourceMetric.LoopBodyEntries && evidence.MetadataAssumptions.Length == 1 && storage.Length == 0 && tokens.Length == 2; }
        if (evidence.ExecutionScope == WorkerResourceModels.TaskSourceAllocationScope)
        { return evidence.Metric is WorkerResourceMetric.ArrayAllocations or WorkerResourceMetric.AllocatedElements && evidence.MetadataAssumptions.Length == 1 && storage.Length == 0 && tokens.Length == 2; }
        if (evidence.ExecutionScope == WorkerResourceModels.TaskRuntimeScope)
        {
            var runtimeLayouts = evidence.MetadataAssumptions.Where(static dependency => dependency is not null &&
                dependency.RuntimeModel == WorkerResourceModels.AllocationLayout).ToArray();
            return evidence.Metric == WorkerResourceMetric.AllocatedBytes && evidence.MetadataAssumptions.Length == 2 &&
                storage.Length == 0 && runtimeLayouts.Length == 1 && tokens.Length == 4 &&
                OneTaskToken(tokens, "resource-task-runtime-model:", WorkerResourceModels.TaskRuntimeCostModel) &&
                OneTaskToken(tokens, "resource-task-compiler-carriers:", WorkerResourceModels.TaskCompilerCarrierModel);
        }
        if (evidence.ExecutionScope != WorkerResourceModels.TaskStorageScope ||
            evidence.Metric is not (WorkerResourceMetric.PeakAuxiliaryBytes or WorkerResourceMetric.RetainedBytes) ||
            evidence.MetadataAssumptions.Length != 3 || storage.Length != 1 || tokens.Length != 4 ||
            storage[0].WitnessIdentifier != "bcl.task.run-action.when-all-array.normal-owned-storage" ||
            storage[0].RuntimeModel != (dependencies[0].RuntimeModel == WorkerResourceModels.ConfiguredTaskJoinModel ?
                WorkerResourceModels.ConfiguredTaskStorageModel : WorkerResourceModels.TaskStorageModel) ||
            storage[0].ReferenceAssembly != dependencies[0].ReferenceAssembly ||
            !string.Equals(storage[0].ReferenceSha256, dependencies[0].ReferenceSha256, StringComparison.OrdinalIgnoreCase) ||
            !OneTaskToken(tokens, "resource-task-storage-model:", storage[0].RuntimeModel) ||
            !OneTaskToken(tokens, "resource-task-compiler-carriers:", WorkerResourceModels.TaskCompilerCarrierModel))
        { return false; }
        var layouts = evidence.MetadataAssumptions.Where(static dependency => dependency is not null &&
            dependency.RuntimeModel == WorkerResourceModels.AllocationLayout && dependency.WitnessIdentifier == "runtime-layout.coreclr-x64").ToArray();
        return layouts.Length == 1 && layouts[0].ReferenceAssembly == dependencies[0].ReferenceAssembly &&
            string.Equals(layouts[0].ReferenceSha256, dependencies[0].ReferenceSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool OneTaskToken(string[] tokens, string prefix, string value)
    {
        tokens = ArgumentNullGuard.NotNull(tokens, nameof(tokens));
        prefix = ArgumentNullGuard.NotNull(prefix, nameof(prefix));
        value = ArgumentNullGuard.NotNull(value, nameof(value));
        return tokens.Count(item => item is not null && item.StartsWith(prefix, StringComparison.Ordinal)) == 1 &&
            tokens.Contains(prefix + value, StringComparer.Ordinal);
    }

    private static bool HasValidResourceExpected(WorkerClaimResult result, WorkerResourceExpectedEvidence? evidence)
    {
        result = ArgumentNullGuard.NotNull(result, nameof(result));
        var core = EmptyIfDefault(result.ProofCore).Contains("resource-input-model:" + WorkerResourceModels.Expected, StringComparer.Ordinal) == true;
        var records = 0;
        string? record = null;
        foreach (var item in EmptyIfDefault(result.ProofCore))
        {
            if (item?.StartsWith("resource-input-record:", StringComparison.Ordinal) == true)
            { records++; record = item; }
        }
        if (evidence is null)
        { return !core && records == 0; }
        if (evidence.Model != WorkerResourceModels.Expected || result.Outcome != WorkerClaimOutcome.Proven || result.Vacuity != WorkerVacuityKind.None)
        { return false; }
        if (evidence.UnknownReason is { } reason)
        {
            return !core && records == 0 && !string.IsNullOrWhiteSpace(reason) && reason.Length <= 256 && !reason.Any(char.IsControl) &&
                evidence.InputOrdinal is null && evidence.InputName is null && evidence.TrueWeight is null && evidence.FalseWeight is null &&
                evidence.FalseUpperBound is null && evidence.TrueUpperBound is null && evidence.UpperBound is null;
        }
        return core && records == 1 && record == "resource-input-record:" + ComputeSha256(JsonSerializer.SerializeToUtf8Bytes(evidence, Options).ToImmutableArray()) &&
            (result.ResourceBound?.Metric is WorkerResourceMetric.LoopBodyEntries or WorkerResourceMetric.ArrayAllocations or WorkerResourceMetric.AllocatedElements or WorkerResourceMetric.AllocatedBytes) &&
            evidence.InputOrdinal is >= 0 && ValidText(evidence.InputName, 256) &&
            evidence.TrueWeight is >= 0 and <= 1000000 && evidence.FalseWeight is >= 0 and <= 1000000 && evidence.TrueWeight + evidence.FalseWeight > 0 &&
            ValidText(evidence.FalseUpperBound, 16384) && ValidText(evidence.TrueUpperBound, 16384) && ValidText(evidence.UpperBound, 16384);

        static bool ValidText(string? value, int maximum)
        {
            return value is not null && !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
        }
    }

    private static bool HasValidResourceAccounting(WorkerClaimResult result, WorkerResourceAccountingEvidence[]? rows)
    {
        result = ArgumentNullGuard.NotNull(result, nameof(result));
        if (rows is null || rows.Length > 16 ||
            (result.Outcome != WorkerClaimOutcome.Proven || result.Vacuity != WorkerVacuityKind.None) && rows.Length != 0)
        { return false; }
        var core = EmptyIfDefault(result.ProofCore).Contains("resource-accounting:" + WorkerResourceModels.Accounting, StringComparer.Ordinal) == true;
        if (core != (rows.Length != 0))
        { return false; }
        foreach (var row in rows)
        {
            if (row is null || row.Model != WorkerResourceModels.Accounting || row.CounterWidth is not (32 or 64) ||
                !long.TryParse(row.InitialCredit, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var initial) ||
                initial.ToString(System.Globalization.CultureInfo.InvariantCulture) != row.InitialCredit ||
                !long.TryParse(row.DepositPerIteration, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var deposit) || deposit <= 0 ||
                deposit.ToString(System.Globalization.CultureInfo.InvariantCulture) != row.DepositPerIteration ||
                row.CounterWidth == 32 && (initial > int.MaxValue || deposit > int.MaxValue) ||
                string.IsNullOrWhiteSpace(row.MaximumSpend) || row.MaximumSpend.Length > 16384 || row.MaximumSpend.Any(static character => char.IsControl(character)))
            { return false; }
        }
        return true;
    }

    private static bool HasValidResourceLowerAsymptotic(WorkerResourceLowerAsymptoticEvidence? evidence, WorkerResourceAsymptoticEvidence? upper)
    {
        if (evidence is null || upper is null || evidence.Model != WorkerResourceModels.NormalDomain)
        { return false; }
        if (evidence.UnknownReason is { } reason)
        {
            return !string.IsNullOrWhiteSpace(reason) && reason.Length <= 256 && !reason.Any(static character => char.IsControl(character)) &&
                evidence.InferredBound is null && evidence.LowerBound is null && evidence.TightBound is null && evidence.TightUnknownReason is null && evidence.Growth is null;
        }
        if (upper.UnknownReason is not null || evidence.Growth is not { Lower: null, UnknownReason: null } growth ||
            !HasValidResourceAsymptotic(growth) || string.IsNullOrWhiteSpace(evidence.InferredBound) ||
            evidence.InferredBound!.Length > 16384 || evidence.InferredBound.Any(static character => char.IsControl(character)) ||
            growth.IsZero != upper.IsZero && (growth.IsZero == true || upper.IsZero == true) ||
            growth.PolynomialDegree > upper.PolynomialDegree ||
            growth.PolynomialDegree == upper.PolynomialDegree && growth.LogarithmicDegree > upper.LogarithmicDegree ||
            growth.InputOrdinal is not null && (growth.InputOrdinal != upper.InputOrdinal || growth.InputKind != upper.InputKind || growth.InputName != upper.InputName))
        { return false; }
        return evidence.LowerBound == "Omega" + growth.UpperBound!.Substring(1) &&
            evidence.TightBound == (growth.UpperBound == upper.UpperBound ? "Theta" + upper.UpperBound!.Substring(1) : null) &&
            evidence.TightUnknownReason == (growth.UpperBound == upper.UpperBound ? null : "ResourceTightnessUnproved");
    }

    private static bool HasValidResourceAsymptotic(WorkerResourceAsymptoticEvidence? evidence)
    {
        if (evidence is null || evidence.Model != WorkerResourceModels.Asymptotic)
        { return false; }
        if (evidence.UnknownReason is { } reason)
        {
            return !string.IsNullOrWhiteSpace(reason) && reason.Length <= 256 && !reason.Any(static character => char.IsControl(character)) &&
                evidence.UpperBound is null && evidence.PolynomialDegree is null && evidence.LogarithmicDegree is null &&
                evidence.IsZero is null && evidence.InputOrdinal is null && evidence.InputKind is null && evidence.InputName is null;
        }
        if (evidence.PolynomialDegree is not (>= 0 and <= WorkerResourceModels.MaximumAsymptoticDegree) ||
            evidence.LogarithmicDegree is not (>= 0 and <= WorkerResourceModels.MaximumAsymptoticDegree) ||
            evidence.IsZero is not { } zero)
        { return false; }
        var polynomial = evidence.PolynomialDegree.Value;
        var logarithmic = evidence.LogarithmicDegree.Value;
        if (zero && (polynomial != 0 || logarithmic != 0))
        { return false; }
        var hasInput = evidence.InputOrdinal is not null || evidence.InputKind is not null || evidence.InputName is not null;
        if (hasInput && (evidence.InputOrdinal is not >= 0 ||
            evidence.InputKind is not ("NonNegativeScalar" or "ArrayLength" or "StringLength") ||
            string.IsNullOrWhiteSpace(evidence.InputName) || evidence.InputName!.Length > 16384 ||
            evidence.InputName.Any(static character => char.IsControl(character))) ||
            !hasInput && (polynomial != 0 || logarithmic != 0))
        { return false; }
        var power = polynomial == 0 ? "" : polynomial == 1 ? "n" :
            "n^" + polynomial.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var log = logarithmic == 0 ? "" : logarithmic == 1 ? "log(n)" :
            "log(n)^" + logarithmic.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var order = zero ? "0" : power.Length == 0 ? log.Length == 0 ? "1" : log :
            log.Length == 0 ? power : power + " * " + log;
        return evidence.UpperBound == "O(" + order + ")";
    }

    private static bool HasValidResourceMetadata(WorkerClaimManifestEntry claim, WorkerClaimResult result, WorkerResourceBoundEvidence evidence)
    {
        claim = ArgumentNullGuard.NotNull(claim, nameof(claim));
        result = ArgumentNullGuard.NotNull(result, nameof(result));
        evidence = ArgumentNullGuard.NotNull(evidence, nameof(evidence));
        if (evidence.MetadataAssumptions is not { Length: <= 16 } metadata || result.Assumptions.IsDefault)
        { return false; }
        var used = new HashSet<string>(result.Assumptions.Where(static assumption => assumption is not null && assumption.Used &&
                assumption.Kind == WorkerAssumptionKind.TrustedBoundary && assumption.Id.StartsWith("resource-metadata-", StringComparison.Ordinal))
            .Select(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id)
            , StringComparer.Ordinal);
        if (metadata.Length != used.Count || result.Outcome == WorkerClaimOutcome.Unknown && metadata.Length != 0)
        { return false; }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in metadata)
        {
            if (dependency is null || !IsResourceCatalogSha256(dependency.CatalogSha256) || !IsSha256(dependency.ReferenceSha256) ||
                !ResourceMetadataText(dependency.WitnessIdentifier, 128) || !ResourceMetadataText(dependency.ReferenceAssembly, 512) ||
                !ResourceMetadataText(dependency.RuntimeModel, 64) || !ResourceMetadataText(dependency.EvidenceSource, 1024) ||
                !Uri.TryCreate(dependency.EvidenceSource, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)
            { return false; }
            var owner = ComputeSha256(Encoding.UTF8.GetBytes(claim.CallableId + "\n" + dependency.ReferenceAssembly).ToImmutableArray()).ToUpperInvariant();
            var expected = "resource-metadata-" + dependency.RuntimeModel + ":" + dependency.CatalogSha256 + ":" +
                dependency.WitnessIdentifier + ":" + dependency.EvidenceSource + ":owner:" + owner;
            if (dependency.AssumptionId != expected || !seen.Add(expected) || !used.Contains(expected))
            { return false; }
        }
        return true;
    }

    // Resource catalog identities predate native evidence and use uppercase hex.
    // Reference-image and other protocol digests keep their lowercase encoding.
    private static bool IsResourceCatalogSha256(string? value)
    {
        return value is { Length: 64 } && value.All(static character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
    }
    private static bool ResourceMetadataText(string? value, int maximum)
    {
        if (value is null)
        { return false; }
        return !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(static character => char.IsControl(character));
    }

    private static bool HasValidResourceViolation(WorkerResourceBoundEvidence evidence)
    {
        evidence = ArgumentNullGuard.NotNull(evidence, nameof(evidence));
        return evidence.ObservedLowerBound is { Length: > 0 and <= 20 } && evidence.EvaluatedUpperBound is { Length: > 0 and <= 20 } &&
            ulong.TryParse(evidence.ObservedLowerBound, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var observed) &&
            observed.ToString(System.Globalization.CultureInfo.InvariantCulture) == evidence.ObservedLowerBound &&
            ulong.TryParse(evidence.EvaluatedUpperBound, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var limit) &&
            limit.ToString(System.Globalization.CultureInfo.InvariantCulture) == evidence.EvaluatedUpperBound && observed > limit;
    }
}
