using System.Text.Json;

namespace SharpProof.CompilerArtifact;

internal static class CompilerFeatureScopeFingerprint
{
    private const string Domain = "SharpProof.CompilerFeatureScope";
    private const int Version = 1;

    internal static string ComputeSha256(CompilerManifestArtifact artifact)
    {
        artifact = ArgumentNullGuard.NotNull(artifact, nameof(artifact));

        using var hash = new CanonicalHashWriter();
        hash.Add(Domain, Version, (int)artifact.Features);
        AddJson(hash, artifact.Manifest);

        var callables = artifact.Callables;
        hash.Add(callables?.Length ?? -1);
        foreach (var callable in (callables ?? [])
                     .OrderBy(static item => item?.CallableId, StringComparer.Ordinal))
        {
            AddCallable(hash, callable);
        }

        return hash.Finish();
    }

    private static void AddCallable(
        CanonicalHashWriter hash,
        CompilerCallableArtifact? callable)
    {
        if (callable == null)
        {
            hash.Add((string?)null);
            return;
        }

        hash.Add(
            callable.CallableId,
            callable.FailureReason,
            callable.Graph != null,
            callable.Body?.Kind ?? CompilerPreparedBodyKind.Trivial);

        var clauses = callable.Clauses;
        hash.Add(clauses?.Length ?? -1);
        foreach (var clause in clauses ?? [])
        {
            if (clause == null)
            {
                hash.Add((string?)null);
                continue;
            }

            hash.Add(
                clause.Kind,
                clause.Evidence,
                clause.ClaimId,
                clause.AssumptionId,
                clause.PredicateSha256);
        }

        var variables = callable.Variables;
        hash.Add(variables?.Length ?? -1);
        foreach (var variable in variables ?? [])
        {
            if (variable == null)
            {
                hash.Add((string?)null);
                continue;
            }

            hash.Add(
                variable.Role,
                variable.Ordinal,
                variable.Minimum.HasValue,
                variable.Minimum ?? 0L,
                variable.Maximum.HasValue,
                variable.Maximum ?? 0L,
                variable.ModelLabel);
        }

        var body = callable.Body;
        hash.Add(body != null);
        if (body != null)
        {
            AddCallIdentities(hash, body.Calls);
            AddSpecCalls(hash, body.SpecCalls);
            AddSummaryCalls(hash, body.SummaryCalls);
        }

        var effects = callable.EffectClaims;
        hash.Add(effects?.Length ?? -1);
        foreach (var effect in effects ?? [])
        {
            AddJson(hash, effect);
        }
    }

    private static void AddCallIdentities(
        CanonicalHashWriter hash,
        CompilerCallIdentityArtifact[]? calls)
    {
        hash.Add(calls?.Length ?? -1);
        foreach (var call in calls ?? [])
        {
            if (call == null)
            {
                hash.Add((string?)null);
                continue;
            }

            hash.Add(call.Instruction, call.Identity);
        }
    }

    private static void AddSpecCalls(
        CanonicalHashWriter hash,
        CompilerSpecCallArtifact[]? calls)
    {
        hash.Add(calls?.Length ?? -1);
        foreach (var call in calls ?? [])
        {
            if (call == null)
            {
                hash.Add((string?)null);
                continue;
            }

            hash.Add(call.Instruction, call.WitnessIdentifier, call.ConsumesMemoryHavoc);
        }
    }

    private static void AddSummaryCalls(
        CanonicalHashWriter hash,
        CompilerSummaryCallArtifact[]? calls)
    {
        hash.Add(calls?.Length ?? -1);
        foreach (var call in calls ?? [])
        {
            if (call == null)
            {
                hash.Add((string?)null);
                continue;
            }

            hash.Add(
                call.Instruction,
                call.Identity,
                call.Origin,
                call.EvidenceSha256,
                call.EvidenceIdentity);
            var dependencies = call.DependencyEvidence;
            hash.Add(dependencies?.Length ?? -1);
            foreach (var dependency in dependencies ?? [])
            {
                if (dependency == null)
                {
                    hash.Add((string?)null);
                    continue;
                }

                hash.Add(
                    dependency.Origin,
                    dependency.EvidenceSha256,
                    dependency.EvidenceIdentity);
            }
        }
    }

    private static void AddJson(CanonicalHashWriter hash, object? value)
    {
        if (value is null)
        {
            hash.Add((string?)null);
        }
        else
        {
            hash.Add(JsonSerializer.SerializeToUtf8Bytes(
                value,
                WorkerProtocolJson.Options));
        }
    }
}
