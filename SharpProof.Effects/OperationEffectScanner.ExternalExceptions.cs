using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed partial class OperationEffectScanner
{
    private EffectSummary ScanUnmodeledExternalExceptionThrow(
        IThrowOperation thrown)
    {
        if (!TryGetUnmodeledExternalExceptionConstruction(
                thrown.Exception,
                out var creation))
        {
            return EffectSummaryOperations.Throw(
                ResolveThrownException(thrown));
        }

        var result = ScanObjectConstruction(
            creation,
            EffectSummary.Empty,
            suppressExternalConstruction: false,
            out var argumentsCompleted);
        if (!argumentsCompleted)
        {
            return result.Summary;
        }

        return EffectSummaryOperations.ExceptionConstructionThrow(
            result.Summary,
            result.CompletesNormally
                ? ResolveThrownException(thrown)
                : EffectThrowSet.Empty);
    }

    private bool IsUnmodeledExternalExceptionConstruction(IOperation? operation)
    {
        return TryGetUnmodeledExternalExceptionConstruction(
            operation,
            out _);
    }

    private bool IsExternalExceptionConstructionWithoutSpec(
        IOperation? operation)
    {
        return operation != null &&
            DefiniteOperationFacts.UnwrapHarmlessValue(operation)
                is IObjectCreationOperation creation &&
            IsExternalExceptionConstruction(creation) &&
            !HasNonThrowingConstructorSpec(creation);
    }

    private bool TryGetUnmodeledExternalExceptionConstruction(
        IOperation? operation,
        out IObjectCreationOperation creation)
    {
        if (operation != null &&
            DefiniteOperationFacts.UnwrapHarmlessValue(operation)
                is IObjectCreationOperation candidate &&
            candidate.Type is INamedTypeSymbol
            {
                DeclaringSyntaxReferences.Length: 0
            } &&
            IsExternalExceptionConstruction(candidate) &&
            !HasNonThrowingConstructorSpec(candidate) &&
            !IsTrustedFrameworkExceptionConstruction(candidate))
        {
            creation = candidate;
            return true;
        }

        creation = null!;
        return false;
    }

    private bool IsExternalExceptionConstruction(
        IObjectCreationOperation creation)
    {
        return
            creation.Type is INamedTypeSymbol type &&
            _exceptionType is { } exceptionType &&
            EffectTypeFacts.IsDerivedFrom(type, exceptionType) &&
            creation.Constructor is
            { DeclaringSyntaxReferences.Length: 0 };
    }

    private bool IsTrustedFrameworkExceptionConstruction(
        IObjectCreationOperation creation)
    {
        return creation.Constructor is { Parameters.Length: 0 } constructor &&
            _exceptionType is { } exceptionType &&
            SymbolEqualityComparer.Default.Equals(
                constructor.ContainingAssembly,
                exceptionType.ContainingAssembly);
    }
}
