using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class FieldReferencePurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.FieldReference);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IFieldReferenceOperation fieldReferenceOperation))
            {
                PurityAnalysisEngine.LogDebug($"WARNING: FieldReferencePurityRule called with unexpected operation type: {operation.Kind}. Assuming Impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);
            }

            var fieldSymbol = fieldReferenceOperation.Field;
            if (fieldSymbol == null)
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Impure due to null fieldSymbol for {fieldReferenceOperation.Syntax.ToString()}");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(fieldReferenceOperation.Syntax);
            }


            if (IsPartOfAssignmentTarget(fieldReferenceOperation))
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Skipping field read {fieldSymbol.Name} as it's an assignment target.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }


            if (fieldSymbol.IsVolatile)
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Field '{fieldSymbol.Name}' is volatile - Impure read.");
                return ImpureFieldRead(fieldReferenceOperation, "volatile");
            }


            if (fieldSymbol.IsConst)
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Field '{fieldSymbol.Name}' is const - Pure");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.IsConfiguredKnownPureMember(fieldSymbol))
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Field '{fieldSymbol.Name}' is configured known pure. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }


            if (!fieldSymbol.IsStatic && fieldReferenceOperation.Instance != null)
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Checking instance expression for field '{fieldSymbol.Name}': {fieldReferenceOperation.Instance.Syntax} ({fieldReferenceOperation.Instance.Kind})");
                var instanceResult = PurityAnalysisEngine.CheckSingleOperation(fieldReferenceOperation.Instance, context, currentState);
                if (!instanceResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance expression is IMPURE. Field reference is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        instanceResult.ImpureSyntaxNode ?? fieldReferenceOperation.Syntax,
                        instanceResult.Evidence);
                }

                if (PurityAnalysisEngine.TryCreateUseAfterDisposeEvidence(
                        fieldReferenceOperation,
                        fieldReferenceOperation.Instance,
                        fieldSymbol,
                        currentState,
                        context.SemanticModel,
                        context.CancellationToken,
                        nameof(FieldReferencePurityRule),
                        out var useAfterDisposeEvidence))
                {
                    PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Field '{fieldSymbol.Name}' reads a resource already marked disposed by symbolic ownership facts.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        fieldReferenceOperation.Syntax,
                        useAfterDisposeEvidence);
                }
            }


            if (fieldSymbol.IsStatic)
            {
                var hasTrustedGeneratedFieldPurity = PurityAnalysisEngine.TryGetTrustedDefinitiveGeneratedFieldPurity(
                    fieldSymbol,
                    context.SemanticModel.Compilation,
                    out var generatedPurity);

                if (fieldSymbol.IsReadOnly && IsStableStaticBclValueField(fieldSymbol))
                {
                    PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Static readonly field '{fieldSymbol.Name}' is a stable BCL value. Assuming Pure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                var staticCtorResult = PurityAnalysisEngine.CheckStaticConstructorPurity(fieldSymbol.ContainingType, context, currentState);
                if (!staticCtorResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Static constructor trigger for field '{fieldSymbol.Name}' is IMPURE. Field reference is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        staticCtorResult.ImpureSyntaxNode ?? fieldReferenceOperation.Syntax,
                        staticCtorResult.Evidence);
                }

                if (fieldSymbol.IsReadOnly)
                {
                    var knownImpureMemberSource = PurityAnalysisEngine.GetKnownImpureMemberSource(fieldSymbol);
                    var hasConfiguredKnownImpureMember = string.Equals(
                        knownImpureMemberSource,
                        "config_known_impure",
                        StringComparison.Ordinal);

                    if (hasConfiguredKnownImpureMember)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Static readonly field '{fieldSymbol.Name}' is configured known impure.");
                        return ImpureFieldRead(fieldReferenceOperation, "known_impure_member", knownImpureMemberSource);
                    }

                    if (hasTrustedGeneratedFieldPurity && generatedPurity.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Static readonly field '{fieldSymbol.Name}' is trusted pure from generated purity summary.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }

                    if (knownImpureMemberSource != null)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Static readonly field '{fieldSymbol.Name}' is built-in known impure.");
                        return ImpureFieldRead(fieldReferenceOperation, "known_impure_member", knownImpureMemberSource);
                    }

                    if (hasTrustedGeneratedFieldPurity && generatedPurity.IsNonPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Static readonly field '{fieldSymbol.Name}' is trusted impure from generated purity summary.");
                        return ImpureFieldRead(fieldReferenceOperation, generatedPurity.PrimaryCategory, "generated_purity_summary");
                    }

                    PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Static readonly field '{fieldSymbol.Name}' - Pure");
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Static non-readonly field '{fieldSymbol.Name}' - Impure");
                    if (PurityAnalysisEngine.TryCreateBclFallbackImpurity(
                        fieldSymbol,
                        fieldReferenceOperation.Syntax,
                        fieldReferenceOperation,
                        nameof(FieldReferencePurityRule),
                        out var staticFieldFallbackResult))
                    {
                        return staticFieldFallbackResult;
                    }

                    return ImpureFieldRead(fieldReferenceOperation);
                }
            }


            if (fieldReferenceOperation.Instance != null)
            {
                PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance field '{fieldSymbol.Name}'. Checking instance...");

                IOperation instanceOperation = fieldReferenceOperation.Instance;

                if (instanceOperation is IParameterReferenceOperation paramRef)
                {


                    bool isReadOnlyRef = paramRef.Parameter.RefKind == RefKind.In ||
                                         paramRef.Parameter.RefKind == RefKind.RefReadOnly ||
                                         paramRef.Parameter.RefKind == (RefKind)4;
                    bool isValueStruct = paramRef.Parameter.RefKind == RefKind.None && paramRef.Parameter.Type.IsValueType;

                    if (isReadOnlyRef || isValueStruct)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance '{paramRef.Parameter.Name}' is {(isValueStruct ? "value struct" : "readonly ref")}. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                    else
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance '{paramRef.Parameter.Name}' is mutable ref/out. Read is Impure.");
                        if (PurityAnalysisEngine.TryCreateBclFallbackImpurity(
                            fieldSymbol,
                            fieldReferenceOperation.Syntax,
                            fieldReferenceOperation,
                            nameof(FieldReferencePurityRule),
                            out var parameterFieldFallbackResult))
                        {
                            return parameterFieldFallbackResult;
                        }

                        return ImpureFieldRead(fieldReferenceOperation);
                    }
                }
                else if (instanceOperation is IInstanceReferenceOperation instanceRef && instanceRef.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance)
                {

                    bool isReadonlyStruct = context.ContainingMethodSymbol.ContainingType.IsReadOnly &&
                                            context.ContainingMethodSymbol.ContainingType.IsValueType;
                    PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Checking 'this' instance. isReadonlyStruct={isReadonlyStruct}, fieldSymbol.IsReadOnly={fieldSymbol.IsReadOnly}");

                    if (isReadonlyStruct)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance is 'this' within a readonly struct. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                    else if (fieldSymbol.IsReadOnly)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance is 'this', field '{fieldSymbol.Name}' is readonly. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                    else if (PurityAnalysisEngine.IsStrictPurityProfile)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Strict profile: mutable instance field '{fieldSymbol.Name}' read from 'this' is Impure.");
                        return ImpureFieldRead(fieldReferenceOperation, "strict_profile");
                    }
                    else
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance is 'this' within a non-readonly type and field '{fieldSymbol.Name}' is not readonly. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                }
                else
                {



                    var unwrappedInstance = PurityAnalysisEngine.SkipImplicitConversions(instanceOperation) ?? instanceOperation;
                    if (IsByValueValueTypeReceiver(unwrappedInstance))
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance is a by-value value-type receiver ({unwrappedInstance.Kind}). Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                    if (fieldSymbol.IsReadOnly &&
                        OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableReadonlyFieldReference(fieldReferenceOperation, fieldReferenceOperation.Syntax, context.SemanticModel))
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Readonly field '{fieldSymbol.Name}' on stable local wrapper carries an owned fresh mutable object. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                    if (OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableObjectReference(instanceOperation, fieldReferenceOperation.Syntax, context, currentState))
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance field '{fieldSymbol.Name}' on fresh local object receiver. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }




                    var receiverResult = PurityAnalysisEngine.CheckSingleOperation(instanceOperation, context, currentState);
                    if (!receiverResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance operation is impure ({instanceOperation.Kind}). Read is impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            receiverResult.ImpureSyntaxNode ?? instanceOperation.Syntax,
                            receiverResult.Evidence);
                    }


                    string fieldPureSig = fieldSymbol.OriginalDefinition.ToDisplayString();
                bool fieldKnownPure = PurityAnalysisEngine.IsKnownPureBCLMember(
                    fieldSymbol,
                    context.SemanticModel.Compilation);
                    PurityAnalysisEngine.LogDebug($"      [FieldRefRule] Checking IsKnownPureBCLMember for instance field accessed via {instanceOperation.Kind}: '{fieldPureSig}' -> {fieldKnownPure}");

                    if (fieldKnownPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance field '{fieldSymbol.Name}' is known pure BCL. Read is Pure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                    }
                    else
                    {

                        PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Instance is complex ({instanceOperation.Kind})/non-readonly-local and field '{fieldSymbol.Name}' not known pure BCL. Assuming read is Impure.");
                        if (PurityAnalysisEngine.TryCreateBclFallbackImpurity(
                            fieldSymbol,
                            fieldReferenceOperation.Syntax,
                            fieldReferenceOperation,
                            nameof(FieldReferencePurityRule),
                            out var instanceFieldFallbackResult))
                        {
                            return instanceFieldFallbackResult;
                        }

                        return ImpureFieldRead(fieldReferenceOperation);
                    }
                }
            }


            PurityAnalysisEngine.LogDebug($"    [FieldRefRule] Unhandled case for field '{fieldSymbol.Name}'. Assuming Impure.");
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(fieldReferenceOperation.Syntax);
        }

        private static bool IsStableStaticBclValueField(IFieldSymbol fieldSymbol)
        {
            if (!fieldSymbol.IsStatic || !fieldSymbol.IsReadOnly)
            {
                return false;
            }

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
                "System.Net.IPAddress" => name is "Any" or "Broadcast" or "Loopback" or "None" or "IPv6Any" or "IPv6Loopback" or "IPv6None",
                "System.Net.Http.HttpVersion" => name.StartsWith("Version", StringComparison.Ordinal),
                _ => false,
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
                    ruleName: nameof(FieldReferencePurityRule),
                    operation: fieldReferenceOperation,
                    syntaxNode: fieldReferenceOperation.Syntax,
                    symbol: fieldReferenceOperation.Field,
                    catalogSource: catalogSource));
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
                    ruleName: nameof(FieldReferencePurityRule),
                    operation: fieldReferenceOperation,
                    syntaxNode: fieldReferenceOperation.Syntax,
                    symbol: fieldReferenceOperation.Field,
                    catalogSource: catalogSource));
        }


        private bool IsPartOfAssignmentTarget(IOperation operation)
        {
            IOperation? parent = operation.Parent;
            while (parent != null)
            {
                if (parent is IAssignmentOperation assignmentOperation && assignmentOperation.Target == operation)
                {
                    return true;
                }

                if (parent is IExpressionStatementOperation || parent is IBlockOperation)
                {
                    return false;
                }

                if (parent is ICompoundAssignmentOperation compoundAssignment && compoundAssignment.Target == operation)
                {
                    return true;
                }
                if (parent is IIncrementOrDecrementOperation incrementOrDecrement && incrementOrDecrement.Target == operation)
                {
                    return true;
                }

                operation = parent;
                parent = parent.Parent;
            }
            return false;
        }

        private static bool IsByValueValueTypeReceiver(IOperation operation)
        {
            if (operation.Type == null || !operation.Type.IsValueType)
            {
                return false;
            }

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

}
