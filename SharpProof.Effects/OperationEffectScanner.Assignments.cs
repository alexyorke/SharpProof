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
                valueIsStoredDirectly
                    ? ScanProperty(
                        property,
                        EffectAccess.Write,
                        assignedValue: value)
                    : ScanProperty(
                        property,
                        EffectAccess.Write,
                        assignedValueRegion: EffectRegionSet.Unknown),
            IParameterReferenceOperation parameter
                when parameter.Parameter.RefKind is RefKind.Ref or RefKind.Out ||
                     PrimaryConstructorParameterOwnership.IsReceiverBacked(
                         parameter.Parameter,
                         _method) =>
                EffectSummaryOperations.Write(
                    _conversionOwnership.ClassifyParameter(parameter.Parameter)),
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
        var result = new EffectStep(
            Scan(assignment.Target, EffectAccess.Read),
            _completionEvaluator.CanCompleteNormally(assignment.Target));
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        result = result.Then(new EffectStep(
            ResolveOperatorEffects(
                assignment.InConversion.MethodSymbol,
                [assignment.Target],
                assignment),
            _completionEvaluator.CanCompleteCompoundInConversion(
                assignment)));
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        result = result.Then(ScanStep(assignment.Value));
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        result = result.Then(new EffectStep(
            EffectSummaryOperations.Join(
                _conversionEffects.SkipsLiftedOperator(assignment)
                    ? EffectSummary.Empty
                    : ResolveCompoundOperatorEffects(assignment),
                IntegralDivisionExceptions(
                    assignment.OperatorKind,
                    assignment.Type,
                    assignment.Target,
                    assignment.Value,
                    assignment),
                _conversionEffects.CheckedOverflow(
                    assignment.IsChecked,
                    assignment)),
            _completionEvaluator.CanCompleteCompoundOperator(assignment)));
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        // The operator result has no standalone IOperation. Passing a null
        // actual keeps call-precondition projection fail-closed. Its ownership
        // is unknown because a user-defined operator may return any region.
        result = result.Then(new EffectStep(
            ResolveOperatorEffects(
                assignment.OutConversion.MethodSymbol,
                [EffectRegionSet.Unknown],
                [null],
                assignment),
            _completionEvaluator.CanCompleteCompoundOutConversion(
                assignment)));
        return !result.CompletesNormally
            ? result.Summary
            : result.Then(new EffectStep(
                ScanWriteTarget(
                    assignment.Target,
                    assignment.Value,
                    valueIsStoredDirectly: false),
                true)).Summary;
    }

    private EffectSummary ResolveCompoundOperatorEffects(
        ICompoundAssignmentOperation assignment)
    {
        if (assignment.InConversion.MethodSymbol == null)
        {
            return ResolveOperatorEffects(
                assignment.OperatorMethod,
                [assignment.Target, assignment.Value],
                assignment);
        }

        // A user-defined in-conversion can return a value from any region.
        // Roslyn has no IOperation for that synthetic result, so neither its
        // ownership nor call preconditions may be projected from the target.
        return ResolveOperatorEffects(
            assignment.OperatorMethod,
            [
                EffectRegionSet.Unknown,
                _conversionOwnership.ClassifyCallArgumentRegion(
                    assignment.Value)
            ],
            [null, assignment.Value],
            assignment);
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
