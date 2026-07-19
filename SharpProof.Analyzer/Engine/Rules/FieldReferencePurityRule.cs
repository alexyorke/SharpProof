namespace SharpProof.Analyzer.Engine.Rules;

internal class FieldReferencePurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.FieldReference);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (!(operation is IFieldReferenceOperation fieldReferenceOperation))
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);

        var fieldSymbol = fieldReferenceOperation.Field;
        if (fieldSymbol == null)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(fieldReferenceOperation.Syntax);


        if (RuleAnalysisHelper.IsWriteOnlyAssignmentTarget(fieldReferenceOperation))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;


        if (fieldSymbol.IsVolatile) return ImpureFieldRead(fieldReferenceOperation, "volatile");


        if (fieldSymbol.IsConst) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (ImpurityCatalog.IsConfiguredKnownPureMember(fieldSymbol))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;


        if (!fieldSymbol.IsStatic && fieldReferenceOperation.Instance != null)
        {
            var instanceResult =
                PurityAnalysisEngine.CheckSingleOperation(fieldReferenceOperation.Instance, context, currentState);
            if (!instanceResult.IsPure)
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    instanceResult.ImpureSyntaxNode ?? fieldReferenceOperation.Syntax,
                    instanceResult.Evidence);

            if (PurityResourceStateFacts.TryCreateUseAfterDisposeEvidence(
                    fieldReferenceOperation,
                    fieldReferenceOperation.Instance,
                    fieldSymbol,
                    currentState,
                    context.CancellationToken,
                    nameof(FieldReferencePurityRule),
                    out var useAfterDisposeEvidence))
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    fieldReferenceOperation.Syntax,
                    useAfterDisposeEvidence);
        }


        if (fieldSymbol.IsStatic)
        {
            var hasTrustedGeneratedFieldPurity = PurityAnalysisEngine.TryGetTrustedDefinitiveGeneratedFieldPurity(
                fieldSymbol,
                context.SemanticModel.Compilation,
                out var generatedPurity);

            if (fieldSymbol.IsReadOnly && IsStableStaticBclValueField(fieldSymbol))
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var staticCtorResult =
                PurityAnalysisEngine.CheckStaticConstructorPurity(fieldSymbol.ContainingType, context);
            if (!staticCtorResult.IsPure)
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    staticCtorResult.ImpureSyntaxNode ?? fieldReferenceOperation.Syntax,
                    staticCtorResult.Evidence);

            if (fieldSymbol.IsReadOnly)
            {
                var knownImpureMemberSource = PurityCalleeResolver.GetKnownImpureMemberSource(fieldSymbol);
                var hasConfiguredKnownImpureMember = string.Equals(
                    knownImpureMemberSource,
                    "config_known_impure",
                    StringComparison.Ordinal);

                if (hasConfiguredKnownImpureMember)
                    return ImpureFieldRead(fieldReferenceOperation, "known_impure_member", knownImpureMemberSource);

                if (hasTrustedGeneratedFieldPurity && generatedPurity.IsPure)
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;

                if (knownImpureMemberSource != null)
                    return ImpureFieldRead(fieldReferenceOperation, "known_impure_member", knownImpureMemberSource);

                if (hasTrustedGeneratedFieldPurity && generatedPurity.IsNonPure)
                    return ImpureFieldRead(fieldReferenceOperation, generatedPurity.PrimaryCategory,
                        "generated_purity_summary");

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.TryCreateBclFallbackImpurity(
                    fieldSymbol,
                    fieldReferenceOperation.Syntax,
                    fieldReferenceOperation,
                    nameof(FieldReferencePurityRule),
                    out var staticFieldFallbackResult))
                return staticFieldFallbackResult;

            return ImpureFieldRead(fieldReferenceOperation);
        }


        if (fieldReferenceOperation.Instance != null)
        {
            var instanceOperation = fieldReferenceOperation.Instance;

            if (instanceOperation is IParameterReferenceOperation paramRef)
            {
                var isReadOnlyRef = paramRef.Parameter.RefKind == RefKind.In ||
                                    paramRef.Parameter.RefKind == RefKind.RefReadOnly ||
                                    paramRef.Parameter.RefKind == RefKind.RefReadOnlyParameter;
                var isValueStruct = paramRef.Parameter.RefKind == RefKind.None && paramRef.Parameter.Type.IsValueType;

                if (isReadOnlyRef || isValueStruct) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

                if (PurityAnalysisEngine.TryCreateBclFallbackImpurity(
                        fieldSymbol,
                        fieldReferenceOperation.Syntax,
                        fieldReferenceOperation,
                        nameof(FieldReferencePurityRule),
                        out var parameterFieldFallbackResult))
                    return parameterFieldFallbackResult;

                return ImpureFieldRead(fieldReferenceOperation);
            }

            if (instanceOperation is IInstanceReferenceOperation instanceRef &&
                instanceRef.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance)
            {
                var isReadonlyStruct = context.ContainingMethodSymbol.ContainingType.IsReadOnly &&
                                       context.ContainingMethodSymbol.ContainingType.IsValueType;

                if (isReadonlyStruct) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

                if (fieldSymbol.IsReadOnly) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

                if (ImpurityCatalog.IsStrictPurityProfile)
                    return ImpureFieldRead(fieldReferenceOperation, "strict_profile");

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var unwrappedInstance =
                PurityAnalysisEngine.SkipImplicitConversions(instanceOperation) ?? instanceOperation;
            if (IsByValueValueTypeReceiver(unwrappedInstance)) return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            if (fieldSymbol.IsReadOnly &&
                OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableReadonlyFieldReference(fieldReferenceOperation,
                    fieldReferenceOperation.Syntax, context.SemanticModel, context.CancellationToken))
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            if (OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableObjectReference(instanceOperation,
                    fieldReferenceOperation.Syntax, context, currentState))
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;


            var receiverResult = PurityAnalysisEngine.CheckSingleOperation(instanceOperation, context, currentState);
            if (!receiverResult.IsPure)
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    receiverResult.ImpureSyntaxNode ?? instanceOperation.Syntax,
                    receiverResult.Evidence);


            var fieldPureSig = fieldSymbol.OriginalDefinition.ToDisplayString();
            var fieldKnownPure = ImpurityCatalog.IsKnownPureBCLMember(
                fieldSymbol,
                context.SemanticModel.Compilation);

            if (fieldKnownPure) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

            if (PurityAnalysisEngine.TryCreateBclFallbackImpurity(
                    fieldSymbol,
                    fieldReferenceOperation.Syntax,
                    fieldReferenceOperation,
                    nameof(FieldReferencePurityRule),
                    out var instanceFieldFallbackResult))
                return instanceFieldFallbackResult;

            return ImpureFieldRead(fieldReferenceOperation);
        }


        return PurityAnalysisEngine.PurityAnalysisResult.Impure(fieldReferenceOperation.Syntax);
    }

    private static bool IsStableStaticBclValueField(IFieldSymbol fieldSymbol)
    {
        if (!fieldSymbol.IsStatic || !fieldSymbol.IsReadOnly) return false;

        var containingType = fieldSymbol.ContainingType?.OriginalDefinition.ToDisplayString();
        var name = fieldSymbol.Name;

        return containingType switch
        {
            "System.Guid" => name is "Empty",
            "System.TimeSpan" => name is "Zero" or "MinValue" or "MaxValue",
            "System.DateTime" => name is "MinValue" or "MaxValue" or "UnixEpoch",
            "System.DateTimeOffset" => name is "MinValue" or "MaxValue" or "UnixEpoch",
            "System.EventArgs" => name is "Empty",
            "System.DBNull" => name is "Value",
            "System.Reflection.Missing" => name is "Value",
            "System.Net.IPAddress" => name is "Any" or "Broadcast" or "Loopback" or "None" or "IPv6Any"
                or "IPv6Loopback" or "IPv6None",
            "System.Net.Http.HttpVersion" => name.StartsWith("Version", StringComparison.Ordinal),
            _ => false
        };
    }

    private static PurityAnalysisEngine.PurityAnalysisResult ImpureFieldRead(
        IFieldReferenceOperation fieldReferenceOperation,
        string? catalogSource = null)
    {
        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
            fieldReferenceOperation.Syntax,
            PurityAnalysisEngine.PurityEvidence.Create(
                "mutable_state_read",
                nameof(FieldReferencePurityRule),
                fieldReferenceOperation,
                fieldReferenceOperation.Syntax,
                fieldReferenceOperation.Field,
                catalogSource));
    }

    private static PurityAnalysisEngine.PurityAnalysisResult ImpureFieldRead(
        IFieldReferenceOperation fieldReferenceOperation,
        string category,
        string? catalogSource)
    {
        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
            fieldReferenceOperation.Syntax,
            PurityAnalysisEngine.PurityEvidence.Create(
                category,
                nameof(FieldReferencePurityRule),
                fieldReferenceOperation,
                fieldReferenceOperation.Syntax,
                fieldReferenceOperation.Field,
                catalogSource));
    }
    private static bool IsByValueValueTypeReceiver(IOperation operation)
    {
        if (operation.Type == null || !operation.Type.IsValueType) return false;

        return operation switch
        {
            IObjectCreationOperation => true,
            IDefaultValueOperation => true,
            ILocalReferenceOperation localReference => localReference.Local.RefKind == RefKind.None,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter.RefKind == RefKind.None,
            _ => false
        };
    }
}
