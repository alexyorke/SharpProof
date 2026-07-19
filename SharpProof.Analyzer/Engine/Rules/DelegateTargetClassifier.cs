namespace SharpProof.Analyzer.Engine.Rules;

internal enum DelegateTargetKind
{
    Method,
    AnonymousFunction,
    ExistingDelegate,
    Unsupported
}

internal readonly record struct DelegateTargetClassification(
    DelegateTargetKind Kind,
    IOperation Operation);

internal static class DelegateTargetClassifier
{
    internal static DelegateTargetClassification Classify(IOperation target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));

        var current = target;
        while (true)
        {
            var unwrapped = PurityAnalysisEngine.SkipImplicitConversions(current);
            if (unwrapped != null && !ReferenceEquals(unwrapped, current))
            {
                current = unwrapped;
                continue;
            }

            if (current is IConversionOperation conversion)
            {
                current = conversion.Operand;
                continue;
            }

            if (current is IParenthesizedOperation parenthesized)
            {
                current = parenthesized.Operand;
                continue;
            }

            break;
        }

        var kind = current switch
        {
            IMethodReferenceOperation => DelegateTargetKind.Method,
            IAnonymousFunctionOperation or IFlowAnonymousFunctionOperation => DelegateTargetKind.AnonymousFunction,
            ILocalReferenceOperation or
                IParameterReferenceOperation or
                IFieldReferenceOperation or
                IPropertyReferenceOperation => DelegateTargetKind.ExistingDelegate,
            _ => DelegateTargetKind.Unsupported
        };
        return new DelegateTargetClassification(kind, current);
    }
}
