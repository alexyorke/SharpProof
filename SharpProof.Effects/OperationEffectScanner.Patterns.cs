namespace SharpProof.Effects;

internal sealed partial class OperationEffectScanner
{
    internal ImmutableArray<EffectDirectWitness> DirectWitnesses =>
        _handlerReachability.AnalysisIncomplete
            ? []
            : _directWitnesses.ToImmutable();

    internal EffectSummary ScanUsingDisposalEffects(IOperation root)
    {
        var operations = ReferenceEquals(root, _root)
            ? _operations
            : default;
        return IncludeHandlerReachabilityCompleteness(
            new UsingDisposalEffectResolver(
                _session.Compilation,
                _method,
                _callResolver,
                _abstractFlow,
                _conversionOwnership.ClassifyRegion,
                _completionEvaluator.CanCompleteNormally,
                _completionEvaluator.CanMethodCompleteNormally,
                _handlerReachability.CanMethodThrow,
                _handlerReachability.CanExitAbruptly).Scan(root, operations));
    }

    private EffectSummary IncludeHandlerReachabilityCompleteness(
        EffectSummary summary)
    {
        return _handlerReachability.AnalysisIncomplete
            ? EffectSummaryOperations.Join(
                summary,
                EffectSummaryOperations.IncompleteAnalysis(
                    EffectAnalysisIncompleteReason.OperationBudgetExceeded))
            : summary;
    }

    internal EffectSummary ScanLexicalControlEffects(IOperation root)
    {
        var result = EffectSummary.Empty;
        IEnumerable<IOperation> operations = ReferenceEquals(root, _root)
            ? _operations
            : root.DescendantsAndSelf();
        foreach (var operation in operations
                     .Where(operation =>
                         (operation is ILockOperation or IThrowOperation or
                             ISwitchExpressionOperation) &&
                         !ConversionOwnershipClassifier.IsInsideNestedCallable(
                             operation,
                             root)))
        {
            if (!IsReachable(operation))
            {
                continue;
            }

            var canReachThrow = operation is IThrowOperation throwOperation &&
                CanReachThrow(throwOperation);
            var canReachSwitchThrow = operation is
                    ISwitchExpressionOperation switchThrowExpression &&
                SwitchExpressionFacts.HasReachableUnmatchedPath(
                    switchThrowExpression,
                    _completionEvaluator.CanCompleteNormally,
                    _nullnessEvaluator.IsProvenNonNull(
                        switchThrowExpression.Value,
                        switchThrowExpression),
                    valueAlreadyComplete: true);

            if (IsDirectSyntax(operation))
            {
                if (operation is ILockOperation directLock)
                {
                    RecordDirectLock(directLock);
                }
                else if (operation is IThrowOperation thrown &&
                         canReachThrow)
                {
                    RecordDirect(operation);
                }
            }
            var lexical = operation switch
            {
                ILockOperation @lock
                    when _completionEvaluator.CanCompleteNormally(
                        @lock.LockedValue) => EffectSummaryOperations.Join(
                            PotentialNullLock(@lock.LockedValue, @lock),
                            EffectSummaryOperations.Capability(
                                EffectCapabilityKind.Synchronization)),
                IThrowOperation thrown when IsSourceThrow(thrown) &&
                    canReachThrow => EffectExceptionFlow.KeepEscaping(
                    IsUnmodeledExternalExceptionConstruction(thrown.Exception)
                        ? ScanUnmodeledExternalExceptionThrow(thrown)
                        : IsExternalExceptionConstructionWithoutSpec(
                            thrown.Exception)
                            ? EffectSummaryOperations.ExceptionConstructionThrow(
                                EffectSummary.Empty,
                                ResolveThrownException(thrown))
                        : EffectSummaryOperations.Throw(
                            ResolveThrownException(thrown)),
                    thrown, _session.Compilation),
                ISwitchExpressionOperation switchExpression
                    when canReachSwitchThrow =>
                    EffectExceptionFlow.KeepEscaping(
                        Throw(FrameworkTypeMetadataNames.SwitchExpressionException),
                        switchExpression,
                        _session.Compilation),
                _ => EffectSummary.Empty
            };
            result = EffectSummaryDomain.Instance.Join(result, lexical);
        }
        return IncludeHandlerReachabilityCompleteness(result);
    }

    private bool TryGetPatternAllocation(
        IPatternOperation pattern,
        out EffectAllocationKind allocation)
    {
        allocation = EffectAllocationKind.None;
        if (pattern is not (IDeclarationPatternOperation or
            IRecursivePatternOperation))
        {
            return false;
        }

        var governingValue = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (_nullnessEvaluator.IsProvenNull(governingValue, pattern))
        {
            return false;
        }

        var inputType = pattern.InputType;
        var matchedType = pattern switch
        {
            IDeclarationPatternOperation declaration => declaration.MatchedType,
            IRecursivePatternOperation recursive =>
                recursive.MatchedType ?? recursive.NarrowedType,
            _ => null
        };
        if (inputType == null || matchedType?.IsReferenceType != true ||
            _session.Compilation is not Microsoft.CodeAnalysis.CSharp.CSharpCompilation
                csharpCompilation)
        {
            return false;
        }

        var conversion = csharpCompilation.ClassifyConversion(
            inputType,
            matchedType);
        var typeParameterBoxing = inputType is ITypeParameterSymbol &&
            conversion.IsUnboxing;
        if (!conversion.IsBoxing && !typeParameterBoxing)
        {
            return false;
        }

        allocation = typeParameterBoxing &&
            inputType is ITypeParameterSymbol
            {
                HasValueTypeConstraint: false,
                HasReferenceTypeConstraint: false
            }
                ? EffectAllocationKind.Unknown
                : EffectAllocationKind.Managed;
        if (ManagedAbstractValue.IsNullableType(inputType))
        {
            if (governingValue == null ||
                _abstractFlow?.TryEvaluate(
                    pattern.Parent ?? pattern,
                    governingValue,
                    out var value) != true)
            {
                allocation = EffectAllocationKind.Unknown;
            }
            else if (value.IsDefinitelyNull)
            {
                return false;
            }
        }

        return true;
    }

    private EffectSummary ScanDefaultPattern(IOperation pattern)
    {
        if (_nullnessEvaluator.IsProvenNull(
                SwitchExpressionFacts.GetGoverningValue(
                    (IPatternOperation)pattern),
                pattern))
        {
            return EffectSummary.Empty;
        }

        return pattern is IRecursivePatternOperation recursivePattern
            ? ScanRecursivePattern(recursivePattern)
            : ScanChildren(pattern);
    }

    private EffectSummary ScanRecursivePattern(
        IRecursivePatternOperation pattern)
    {
        if (SwitchExpressionFacts.IsITuplePattern(pattern))
        {
            return ScanITuplePattern(pattern);
        }

        if (pattern.DeconstructSymbol is not IMethodSymbol deconstruct)
        {
            return ScanChildren(pattern);
        }

        var instance = SwitchExpressionFacts.GetGoverningValue(pattern);
        var receiver = _conversionOwnership.ClassifyRegion(
            instance,
            aliasSource: true);
        var result = ScanImplicitPatternCall(
            deconstruct,
            receiver,
            pattern,
            instance);
        return result.CompletesNormally
            ? result.Then(ScanSequence(pattern.ChildOperations)).Summary
            : result.Summary;
    }

    private EffectSummary ScanITuplePattern(
        IRecursivePatternOperation pattern)
    {
        var instance = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (_nullnessEvaluator.IsProvenNull(instance, pattern))
        {
            return EffectSummary.Empty;
        }

        var receiver = _conversionOwnership.ClassifyRegion(
            instance,
            aliasSource: true);
        var result = EffectStep.Empty;
        if (SwitchExpressionFacts.GetITupleLengthMember(pattern) is { } length)
        {
            result = result.Then(ScanImplicitPatternCall(
                length,
                receiver,
                pattern,
                instance));
            if (!result.CompletesNormally)
            {
                return result.Summary;
            }
        }

        var indexer = SwitchExpressionFacts.GetITupleIndexerMember(pattern);
        foreach (var subpattern in pattern.DeconstructionSubpatterns)
        {
            if (indexer is { } indexerMethod)
            {
                result = result.Then(ScanImplicitPatternCall(
                    indexerMethod,
                    receiver,
                    pattern,
                    instance));
                if (!result.CompletesNormally)
                {
                    return result.Summary;
                }
            }

            result = result.Then(ScanStep(subpattern));
            if (!result.CompletesNormally)
            {
                return result.Summary;
            }
        }

        return result.Summary;
    }
}
