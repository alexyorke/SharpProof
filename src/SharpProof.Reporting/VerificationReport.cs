using System.Globalization;
using SharpProof.Worker.Protocol;

namespace SharpProof.Reporting;

internal static class VerificationReport
{
    internal static string Summary(WorkerVerifyResponse response)
    {
        if (response == null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        return string.Format(CultureInfo.InvariantCulture, "SharpProof: {0} selected callables, {1} claims: {2} Proven, {3} Refuted, {4} Unknown; {5} incomplete callables, {6} vacuous claims, {7} declared user/trust assumptions. Run: {8} ({11}); cache: {9}; {10} ms. Unselected methods are not proved.", response.Manifest.Callables.Length, response.Manifest.Claims.Length, response.ClaimResults.Count(static claim =>
        {
            global::SharpProof.Internal.InputValidation.Require(claim, nameof(claim));
            return claim.Outcome == WorkerClaimOutcome.Proven;
        }), response.ClaimResults.Count(static claim =>
        {
            global::SharpProof.Internal.InputValidation.Require(claim, nameof(claim));
            return claim.Outcome == WorkerClaimOutcome.Refuted;
        }), response.ClaimResults.Count(static claim =>
        {
            global::SharpProof.Internal.InputValidation.Require(claim, nameof(claim));
            return claim.Outcome == WorkerClaimOutcome.Unknown;
        }), response.CallableResults.Count(static callable =>
        {
            global::SharpProof.Internal.InputValidation.Require(callable, nameof(callable));
            return callable.Coverage == WorkerCallableCoverage.Incomplete;
        }), response.ClaimResults.Count(static claim =>
        {
            global::SharpProof.Internal.InputValidation.Require(claim, nameof(claim));
            return claim.Vacuity != WorkerVacuityKind.None;
        }), response.CallableResults.SelectMany(static callable =>
        {
            global::SharpProof.Internal.InputValidation.Require(callable, nameof(callable));
            return callable.Assumptions;
        }).Where(static assumption =>
        {
            global::SharpProof.Internal.InputValidation.Require(assumption, nameof(assumption));
            return assumption.Kind is WorkerAssumptionKind.UserAssume or WorkerAssumptionKind.TrustedBoundary;
        }).Select(static assumption =>
        {
            global::SharpProof.Internal.InputValidation.Require(assumption, nameof(assumption));
            return (assumption.Kind, assumption.Id);
        }).Distinct().Count(), response.RunStatus, response.Summary.CacheStatus, response.Summary.ElapsedMilliseconds, response.FailureReason);
    }

    internal static void Write(TextWriter writer, WorkerVerifyResponse response, string? claimId = null)
    {
        if (writer == null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        if (response == null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        var claims = response.Manifest.Claims.ToDictionary(static claim =>
        {
            global::SharpProof.Internal.InputValidation.Require(claim, nameof(claim));
            return claim.ClaimId;
        }, StringComparer.Ordinal);
        if (claimId != null && !claims.ContainsKey(claimId))
        {
            throw new ArgumentException("The requested claim is absent from this result.", nameof(claimId));
        }

        writer.WriteLine(Summary(response));
        foreach (var result in response.ClaimResults.Where(result =>
        {
            global::SharpProof.Internal.InputValidation.Require(result, nameof(result));
            return claimId == null || result.ClaimId == claimId;
        }))
        {
            var claim = claims[result.ClaimId];
            var displayName = claim.CallableId.StartsWith("M:", StringComparison.Ordinal) ? claim.CallableId.Substring(2).Replace("~", " -> ") : claim.CallableId;
            if (result.Presentation is { } presentation)
            {
                displayName = presentation.Name + "(" + string.Join(", ", presentation.ParameterNames) + ")";
            }
            writer.WriteLine($"{claim.Location.Path}({claim.Location.Line},{claim.Location.Column}): {result.Outcome} {displayName} [{claim.Kind}] {claim.ClaimId}");
            if (result.Outcome == WorkerClaimOutcome.Unknown)
            {
                var explanation = ReasonCatalog.Explain(result.Reason);
                writer.WriteLine($"  {explanation.Category}: {explanation.Description} ({result.Reason})");
                writer.WriteLine("  Next: " + explanation.NextAction);
                writer.WriteLine("  Help: " + ReasonExplanation.Documentation);
                if (result.Diagnostic is { } diagnostic)
                {
                    writer.WriteLine($"  Origin: {diagnostic.Stage}/{diagnostic.Code} at {diagnostic.Location.Path}({diagnostic.Location.Line},{diagnostic.Location.Column})");
                }
            }

            foreach (var value in result.Model)
            {
                var label = result.Outcome == WorkerClaimOutcome.Refuted ? "Counterexample" : "Candidate model";
                writer.WriteLine($"  {label} {VariableName(result, value.Variable)}: {value.Value} ({value.Kind})");
            }
            if (claim.Kind == WorkerClaimKind.Effect)
            {
                writer.WriteLine("  Effect evidence: " + result.EffectCertainty);
                if (result.EffectWitness is { } witness)
                {
                    writer.WriteLine($"  Violation witness {witness.Kind}: {witness.Detail} at {witness.Location.Path}({witness.Location.Line},{witness.Location.Column})");
                }
            }
            foreach (var assumption in result.Assumptions)
            {
                global::SharpProof.Internal.InputValidation.Require(assumption, nameof(assumption));
                writer.WriteLine($"  Assumption {assumption.Kind}: {assumption.Id} ({(assumption.Used ? "used" : "declared")})");
            }

            if (result.Vacuity != WorkerVacuityKind.None)
            {
                writer.WriteLine("  Review vacuity: " + result.Vacuity);
            }
        }

        var callables = response.Manifest.Callables.ToDictionary(static callable =>
        {
            global::SharpProof.Internal.InputValidation.Require(callable, nameof(callable));
            return callable.CallableId;
        }, StringComparer.Ordinal);
        foreach (var result in response.CallableResults)
        {
            global::SharpProof.Internal.InputValidation.Require(result, nameof(result));
            if (result.Coverage != WorkerCallableCoverage.Incomplete ||
                (claimId != null && claims[claimId].CallableId != result.CallableId))
            {
                continue;
            }
            var callable = callables[result.CallableId];
            var explanation = ReasonCatalog.Explain(result.Reason);
            writer.WriteLine($"{callable.Location.Path}({callable.Location.Line},{callable.Location.Column}): Incomplete {result.CallableId} ({result.Reason})");
            writer.WriteLine("  " + explanation.Description);
            writer.WriteLine("  Next: " + explanation.NextAction);
            writer.WriteLine("  Help: " + ReasonExplanation.Documentation);
        }

        foreach (var error in response.Errors)
        {
            writer.WriteLine($"Run error {error.Code}: {error.Message}");
        }
    }

    internal static string VariableName(WorkerClaimResult result, string variable)
    {
        if (result == null)
        { throw new ArgumentNullException(nameof(result)); }
        if (variable == null)
        { throw new ArgumentNullException(nameof(variable)); }
        const string prefix = "parameter:";
        if (result.Presentation is { } presentation && variable.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(variable.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) &&
            ordinal >= 0 && ordinal < presentation.ParameterNames.Length)
        {
            return presentation.ParameterNames[ordinal];
        }
        return variable;
    }
}
