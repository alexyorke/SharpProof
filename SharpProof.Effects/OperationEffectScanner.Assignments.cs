namespace SharpProof.Effects;

internal sealed partial class OperationEffectScanner
{
    private EffectSummary ScanWriteTarget(
        IOperation target,
        IOperation value,
        bool valueIsStoredDirectly = true)
    {
        target = _coalesceCaptures.Resolve(target);
        return target switch
        {
            IFieldReferenceOperation field => ScanField(field, EffectAccess.Write),
            IArrayElementReferenceOperation element =>
                ScanArrayElement(
                    element,
                    EffectAccess.Write,
                    valueIsStoredDirectly ? value : null),
            IPropertyReferenceOperation property =>
                ScanProperty(
                    property,
                    EffectAccess.Write,
                    valueIsStoredDirectly ? value : null),
            IParameterReferenceOperation parameter
                when parameter.Parameter.RefKind is RefKind.Ref or RefKind.Out ||
                    PrimaryConstructorParameterOwnership.IsReceiverBacked(
                        parameter.Parameter,
                        _method) =>
                EffectSummaryOperations.Write(
                    _conversionOwnership.ClassifyParameter(parameter.Parameter)),
            ILocalReferenceOperation local
                when local.Local.RefKind != RefKind.None =>
                EffectSummaryOperations.Unsupported(),
            ILocalReferenceOperation or IParameterReferenceOperation or IDiscardOperation =>
                EffectSummary.Empty,
            _ => EffectSummaryOperations.Join(
                Scan(target),
                EffectSummaryOperations.Unsupported())
        };
    }

    private EffectSummary ScanSimpleAssignment(
        ISimpleAssignmentOperation assignment)
    {
        var result = ScanWriteTargetEvaluation(assignment.Target);
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        result = result.Then(ScanStep(assignment.Value));
        return !result.CompletesNormally
            ? result.Summary
            : result.Then(new EffectStep(
                ScanWriteTarget(assignment.Target, assignment.Value),
                true)).Summary;
    }

    private EffectStep ScanWriteTargetEvaluation(IOperation target)
    {
        target = _coalesceCaptures.Resolve(target);
        return target switch
        {
            IFieldReferenceOperation { Instance: { } instance } =>
                ScanStep(instance),
            IArrayElementReferenceOperation element =>
                ScanStep(element.ArrayReference).Then(
                    ScanSequence(element.Indices)),
            IPropertyReferenceOperation property =>
                ScanSequence(
                    property.Instance == null
                        ? property.Arguments.Select(
                            static argument => argument.Value)
                        : new[] { property.Instance }.Concat(
                            property.Arguments.Select(
                                static argument => argument.Value))),
            IFieldReferenceOperation or
                ILocalReferenceOperation or
                IParameterReferenceOperation or
                IDiscardOperation => EffectStep.Empty,
            _ => ScanStep(target)
        };
    }

    private EffectSummary ScanCompoundAssignment(
        ICompoundAssignmentOperation assignment)
    {
        return ScanReadModifyWrite(
            assignment.Target,
            () => ScanStep(assignment.Value),
            () => EffectSummaryOperations.Join(
                ResolveOperatorEffects(
                    assignment.OperatorMethod,
                    [assignment.Target, assignment.Value],
                    assignment),
                IntegralDivisionExceptions(
                    assignment.OperatorKind,
                    assignment.Type,
                    assignment.Target,
                    assignment.Value,
                    assignment),
                _conversionEffects.CheckedOverflow(
                    assignment.IsChecked,
                    assignment)),
            () => _completionEvaluator.CanCompleteCompoundValue(assignment),
            assignment.Value);
    }

    private EffectSummary ScanReadModifyWrite(
        IOperation target,
        Func<EffectStep> scanValue,
        Func<EffectSummary> scanOperation,
        Func<bool> canCompleteOperation,
        IOperation storedValue)
    {
        var result = new EffectStep(
            Scan(target, EffectAccess.Read),
            _completionEvaluator.CanCompleteNormally(target));
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        result = result.Then(scanValue());
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        result = result.Then(new EffectStep(
            scanOperation(),
            canCompleteOperation()));
        return !result.CompletesNormally
            ? result.Summary
            : result.Then(new EffectStep(
                ScanWriteTarget(
                    target,
                    storedValue,
                    valueIsStoredDirectly: false),
                true)).Summary;
    }

    private EffectSummary ScanCoalesceAssignment(
        ICoalesceAssignmentOperation assignment)
    {
        var result = new EffectStep(
            Scan(assignment.Target, EffectAccess.Read),
            _completionEvaluator.CanCompleteNormally(assignment.Target));
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        if (_abstractFlow?.ProvesNonNull(
                assignment,
                assignment.Target) == true)
        {
            return result.Summary;
        }

        result = result.Then(ScanStep(assignment.Value));
        return !result.CompletesNormally
            ? result.Summary
            : result.Then(new EffectStep(
                ScanWriteTarget(assignment.Target, assignment.Value),
                true)).Summary;
    }
}
