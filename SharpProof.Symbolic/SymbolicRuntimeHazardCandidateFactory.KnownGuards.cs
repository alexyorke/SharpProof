using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Ir;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;

namespace SharpProof.Symbolic;

internal static partial class SymbolicRuntimeHazardCandidateFactory
{
    private static bool TryCreateArgumentOutOfRangeGuardCandidate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!SymbolicKnownGuardFacts.TryCreateArgumentOutOfRangeGuardConditions(
                invocation,
                semanticModel,
                cancellationToken,
                out var subject,
                out var triggerCondition,
                out _,
                out var guardKey) ||
            !TryCreateIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.ArgumentOutOfRange,
                subject,
                triggerCondition,
                invocation,
                "ir.runtime-hazard.argument-out-of-range.guard." + guardKey,
                out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            invocation,
            SymbolicRuntimeHazardKind.ArgumentOutOfRange,
            trigger,
            ExceptionTypes.ArgumentOutOfRangeException,
            ExceptionCategories.DefiniteArgumentOutOfRangeGuard);
        return true;
    }
}
