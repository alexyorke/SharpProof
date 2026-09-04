using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Roslyn;

namespace SharpProof.Meta.Analyzers;

internal static class CacheSoundnessRules
{
    private static readonly ImmutableHashSet<string> WriteMethods =
        new[]
        {
            "Add", "AddOrUpdate", "GetOrAdd", "Insert", "Put", "Set",
            "SetAsync", "Store", "TryAdd", "TryUpdate", "TryWrite",
            "TryWriteAsync", "Update", "Write", "WriteAsync"
        }
            .ToImmutableHashSet(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<
        Compilation,
        ConcurrentDictionary<ISymbol, ImmutableArray<ISymbol>>>
        DispatchTargetCaches = new();

    internal static void AnalyzeWrite(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var root = Root(invocation);
        if (ForwardsNonCacheableSemanticAnswer(
                invocation,
                root,
                context.Compilation,
                context.CancellationToken))
        {
            Report(context, invocation.Syntax.GetLocation());
            return;
        }

        if (!WriteMethods.Contains(invocation.TargetMethod.Name) ||
            !IsCacheReceiver(
                invocation.Instance,
                invocation.TargetMethod.ContainingType,
                root,
                new LocalResolution(context.CancellationToken)) ||
            !invocation.Arguments.Any(argument =>
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                return IsStoredValueArgument(invocation, argument) &&
                    !IsGuardedCacheableResponse(
                        invocation,
                        argument.Value) &&
                    (IsNonCacheableSemanticAnswer(
                         argument.Value,
                         root,
                         new LocalResolution(
                             context.CancellationToken,
                             context.Compilation)) ||
                     IsNonCacheableGetOrAddFactory(
                         invocation,
                         argument,
                         root,
                         context.Compilation,
                         context.CancellationToken));
            }))
        {
            return;
        }

        Report(context, invocation.Syntax.GetLocation());
    }

    private static bool ForwardsNonCacheableSemanticAnswer(
        IInvocationOperation invocation,
        IOperation root,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var method = invocation.TargetMethod.OriginalDefinition;
        if (method.DeclaringSyntaxReferences.Length == 0)
        {
            return false;
        }

        foreach (var argument in invocation.Arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ordinal = argument.Parameter?.Ordinal ?? -1;
            if (ordinal < 0 ||
                ordinal >= method.Parameters.Length ||
                method.Parameters[ordinal].Type is not ITypeParameterSymbol ||
                !IsNonCacheableSemanticAnswer(
                    argument.Value,
                    root,
                    new LocalResolution(cancellationToken, compilation)))
            {
                continue;
            }

            if (IsForwardedToCacheWrite(
                    method,
                    method.Parameters[ordinal],
                    cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsForwardedToCacheWrite(
        IMethodSymbol method,
        IParameterSymbol parameter,
        CancellationToken cancellationToken)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaration = reference.GetSyntax(cancellationToken);
            foreach (var invocation in declaration.DescendantNodesAndSelf()
                         .OfType<InvocationExpressionSyntax>()
                         .Where(candidate =>
                             !IsInsideNestedCallable(candidate, declaration)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!WriteMethods.Contains(
                        GetInvokedName(invocation.Expression) ?? string.Empty) ||
                    !IsSyntacticCacheReceiver(invocation.Expression, method) ||
                    !invocation.ArgumentList.Arguments.Any(argument =>
                        IsForwardedParameter(
                            argument.Expression,
                            parameter.Name) &&
                        IsStoredValueArgument(
                            invocation,
                            argument)))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool IsStoredValueArgument(
        IInvocationOperation invocation,
        IArgumentOperation argument)
    {
        return !string.Equals(invocation.TargetMethod.Name, "TryUpdate",
                StringComparison.Ordinal) || argument.Parameter?.Ordinal != 2;
    }

    private static bool IsStoredValueArgument(
        InvocationExpressionSyntax invocation,
        ArgumentSyntax argument)
    {
        // TryUpdate(cache, key, newValue, comparisonValue) reads the final
        // argument only for comparison; it is not the value persisted in the
        // cache. Use the bound parameter ordinal when available through the
        // syntax position as this helper is intentionally syntax-only.
        if (!string.Equals(
                GetInvokedName(invocation.Expression),
                "TryUpdate",
                StringComparison.Ordinal))
        {
            return true;
        }

        return invocation.ArgumentList.Arguments.IndexOf(argument) != 2;
    }

    private static bool IsSyntacticCacheReceiver(
        ExpressionSyntax expression,
        IMethodSymbol method)
    {
        if (expression is SimpleNameSyntax)
        {
            return IsCacheType(method.ContainingType);
        }

        if (expression is not MemberAccessExpressionSyntax member)
        {
            return false;
        }

        var receiver = UnwrapSyntax(member.Expression);
        if (receiver is IdentifierNameSyntax identifier)
        {
            var receiverName = identifier.Identifier.ValueText;
            var parameter = method.Parameters.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Name,
                    receiverName,
                    StringComparison.Ordinal));
            if (parameter != null)
            {
                return IsCacheType(parameter.Type);
            }

            return IsCacheMember(method.ContainingType, receiverName);
        }

        return receiver is MemberAccessExpressionSyntax
        {
            Expression: ThisExpressionSyntax,
            Name: { } memberName
        } && IsCacheMember(
            method.ContainingType,
            memberName.Identifier.ValueText);
    }

    private static bool IsCacheMember(
        INamedTypeSymbol? containingType,
        string name)
    {
        return containingType?.GetMembers(name).Any(member =>
            member switch
            {
                IFieldSymbol field => IsCacheType(field.Type),
                IPropertySymbol property => IsCacheType(property.Type),
                _ => false
            }) == true;
    }

    private static bool IsForwardedParameter(
        ExpressionSyntax expression,
        string parameterName)
    {
        expression = UnwrapSyntax(expression);
        return expression switch
        {
            IdentifierNameSyntax identifier => string.Equals(
                identifier.Identifier.ValueText,
                parameterName,
                StringComparison.Ordinal),
            ConditionalExpressionSyntax conditional =>
                IsForwardedParameter(conditional.WhenTrue, parameterName) ||
                IsForwardedParameter(conditional.WhenFalse, parameterName),
            BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.CoalesceExpression) =>
                IsForwardedParameter(binary.Left, parameterName) ||
                IsForwardedParameter(binary.Right, parameterName),
            _ => false
        };
    }

    private static ExpressionSyntax UnwrapSyntax(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case CheckedExpressionSyntax checkedExpression:
                    expression = checkedExpression.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static bool IsNonCacheableGetOrAddFactory(
        IInvocationOperation invocation,
        IArgumentOperation argument,
        IOperation root,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var factoryType = argument.Parameter?.Type ?? argument.Value.Type;
        if (!string.Equals(
                invocation.TargetMethod.Name,
                "GetOrAdd",
                StringComparison.Ordinal) ||
            factoryType is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Delegate,
                DelegateInvokeMethod: { } invoke
            } ||
            !IsSemanticAnswerType(invoke.ReturnType))
        {
            return false;
        }

        return IsNonCacheableValueFactory(
            argument.Value,
            root,
            new LocalResolution(cancellationToken, compilation));
    }

    private static bool IsNonCacheableValueFactory(
        IOperation operation,
        IOperation root,
        LocalResolution resolving)
    {
        resolving.CancellationToken.ThrowIfCancellationRequested();
        operation = UnwrapValue(operation);
        return operation switch
        {
            IDelegateCreationOperation creation =>
                IsNonCacheableValueFactory(
                    creation.Target,
                    root,
                    resolving),
            IAnonymousFunctionOperation anonymous =>
                IsNonCacheableAnonymousFactory(
                    anonymous,
                    resolving.Compilation,
                    resolving.CancellationToken),
            IMethodReferenceOperation method =>
                IsNonCacheableReturnedValue(
                    method.Method,
                    method.Method.ReturnType,
                    method.Method.Name,
                    resolving.Compilation,
                    resolving.CancellationToken),
            ILocalReferenceOperation local =>
                ResolveValueFactoryLocal(local, root, resolving),
            IConditionalOperation conditional =>
                IsNonCacheableValueFactory(
                    conditional.WhenTrue,
                    root,
                    resolving) ||
                conditional.WhenFalse != null &&
                IsNonCacheableValueFactory(
                    conditional.WhenFalse,
                    root,
                    resolving),
            ICoalesceOperation coalesce =>
                IsNonCacheableValueFactory(
                    coalesce.Value,
                    root,
                    resolving) ||
                IsNonCacheableValueFactory(
                    coalesce.WhenNull,
                    root,
                    resolving),
            _ => true
        };
    }

    private static bool IsNonCacheableAnonymousFactory(
        IAnonymousFunctionOperation factory,
        Compilation? compilation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var returns = factory.Body.DescendantsAndSelf()
            .OfType<IReturnOperation>()
            .Where(operation =>
                !IsInsideNestedCallable(operation, factory.Body))
            .Select(static operation => operation.ReturnedValue)
            .Where(static value => value != null)
            .Cast<IOperation>()
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return returns.Length == 0 || returns.Any(value =>
            IsNonCacheableSemanticAnswer(
                value,
                factory.Body,
                new LocalResolution(cancellationToken, compilation)));
    }

    private static bool ResolveValueFactoryLocal(
        ILocalReferenceOperation reference,
        IOperation root,
        LocalResolution resolving)
    {
        if (!resolving.Add(reference))
        {
            return true;
        }

        try
        {
            var writes = GetReachingLocalValues(
                reference,
                root,
                resolving.CancellationToken);
            return writes.Length == 0 || writes.Any(value =>
                IsSelfReference(value, reference.Local) ||
                IsNonCacheableValueFactory(value, root, resolving));
        }
        finally
        {
            resolving.Remove(reference);
        }
    }

    internal static void AnalyzeAssignment(OperationAnalysisContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var assignment = (IAssignmentOperation)context.Operation;
        var root = Root(assignment);
        if (!IsCacheAssignmentTarget(
                assignment.Target,
                root,
                context.CancellationToken) ||
            !IsNonCacheableSemanticAnswer(
                assignment.Value,
                root,
                new LocalResolution(
                    context.CancellationToken,
                    context.Compilation)))
        {
            return;
        }

        Report(context, assignment.Syntax.GetLocation());
    }

    private static bool IsCacheAssignmentTarget(
        IOperation target,
        IOperation root,
        CancellationToken cancellationToken)
    {
        var resolving = new LocalResolution(cancellationToken);
        return target switch
        {
            IPropertyReferenceOperation property => IsCacheReceiver(
                property.Instance,
                property.Property.ContainingType,
                root,
                resolving),
            IFieldReferenceOperation field => IsCacheReceiver(
                field.Instance,
                field.Field.ContainingType,
                root,
                resolving),
            _ => false
        };
    }

    private static void Report(OperationAnalysisContext context, Location? location)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            MetaDiagnosticDescriptors.NonCacheableSemanticAnswer, location));
    }

    private static bool IsCacheType(ITypeSymbol? type)
    {
        return type?.Name.IndexOf("Cache", StringComparison.Ordinal) >= 0;
    }

    private static bool IsGuardedCacheableResponse(
        IInvocationOperation write,
        IOperation value)
    {
        if (!IsWorkerVerifyResponse(value.Type))
        {
            return false;
        }

        for (var current = write.Parent; current != null; current = current.Parent)
        {
            if (current is IConditionalOperation conditional &&
                IsDescendantOf(write, conditional.WhenTrue) &&
                HasCacheableGuard(conditional.Condition, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCacheableGuard(
        IOperation condition,
        IOperation value)
    {
        condition = UnwrapValue(condition);

        if (condition is IInvocationOperation
            {
                TargetMethod:
                {
                    IsStatic: true,
                    Name: "IsCacheable",
                    ContainingType: { } containingType
                }
            } invocation &&
            IsCacheType(containingType) &&
            invocation.Arguments.FirstOrDefault(argument =>
                argument.Parameter?.Ordinal == 0) is { } response &&
            IsSameValue(response.Value, value))
        {
            return true;
        }

        return condition is IBinaryOperation
        {
            OperatorKind: BinaryOperatorKind.ConditionalAnd
        } binary &&
            (HasCacheableGuard(binary.LeftOperand, value) ||
             HasCacheableGuard(binary.RightOperand, value));
    }

    private static bool IsSameValue(IOperation left, IOperation right)
    {
        left = UnwrapValue(left);
        right = UnwrapValue(right);
        return left switch
        {
            ILocalReferenceOperation local
                when right is ILocalReferenceOperation other =>
                SymbolEqualityComparer.Default.Equals(
                    local.Local,
                    other.Local),
            IParameterReferenceOperation parameter
                when right is IParameterReferenceOperation other =>
                SymbolEqualityComparer.Default.Equals(
                    parameter.Parameter,
                    other.Parameter),
            _ => false
        };
    }

    private static IOperation UnwrapValue(IOperation operation)
    {
        return OperationUnwrapping.Unwrap(operation)!;
    }

    private static bool IsDescendantOf(
        IOperation operation,
        IOperation ancestor)
    {
        for (var current = operation; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCacheReceiver(
        IOperation? operation,
        ITypeSymbol? fallbackType,
        IOperation root,
        LocalResolution resolving)
    {
        resolving.CancellationToken.ThrowIfCancellationRequested();
        if (IsCacheType(fallbackType) || IsCacheType(operation?.Type))
        {
            return true;
        }

        operation = operation == null ? null : UnwrapValue(operation);
        if (IsCacheType(operation?.Type))
        {
            return true;
        }

        return operation switch
        {
            ILocalReferenceOperation local =>
                ResolveCacheLocal(local, root, resolving),
            IConditionalOperation conditional =>
                IsCacheReceiver(
                    conditional.WhenTrue,
                    null,
                    root,
                    resolving) ||
                conditional.WhenFalse != null &&
                IsCacheReceiver(
                    conditional.WhenFalse,
                    null,
                    root,
                    resolving),
            ICoalesceOperation coalesce =>
                IsCacheReceiver(coalesce.Value, null, root, resolving) ||
                IsCacheReceiver(coalesce.WhenNull, null, root, resolving),
            _ => false
        };
    }

    private static bool ResolveCacheLocal(
        ILocalReferenceOperation reference,
        IOperation root,
        LocalResolution resolving)
    {
        if (!resolving.Add(reference))
        {
            return false;
        }

        try
        {
            return GetReachingLocalValues(
                    reference,
                    root,
                    resolving.CancellationToken)
                .Any(value =>
                    !IsSelfReference(value, reference.Local) &&
                    IsCacheReceiver(value, null, root, resolving));
        }
        finally
        {
            resolving.Remove(reference);
        }
    }

    private static IOperation Root(IOperation operation)
    {
        while (operation.Parent != null &&
               operation.Parent is not
                   (IAnonymousFunctionOperation or ILocalFunctionOperation))
        {
            operation = operation.Parent;
        }
        return operation;
    }

    private static bool IsNonCacheableSemanticAnswer(
        IOperation operation,
        IOperation root,
        LocalResolution resolving)
    {
        resolving.CancellationToken.ThrowIfCancellationRequested();
        if (TryClassifySemanticEnumConstant(operation, out var nonCacheable))
        {
            return nonCacheable;
        }

        if (operation is IConversionOperation
            {
                OperatorMethod: null,
                Type: INamedTypeSymbol
                {
                    TypeKind: TypeKind.Enum
                } enumType
            } conversion &&
            IsSemanticAnswerType(enumType))
        {
            return IsNonCacheableNumericEnumValue(
                conversion.Operand,
                enumType,
                root,
                resolving);
        }

        operation = UnwrapValue(operation);
        if (TryClassifySemanticEnumConstant(operation, out nonCacheable))
        {
            return nonCacheable;
        }

        bool Recurse(IOperation value)
        {
            return IsNonCacheableSemanticAnswer(value, root, resolving);
        }

        return operation switch
        {
            IFieldReferenceOperation field
                when field.Field.ContainingType.TypeKind == TypeKind.Enum &&
                     IsSemanticAnswerType(field.Type) => IsNonCacheableName(field.Field.Name),
            IObjectCreationOperation creation =>
                (IsSemanticAnswerType(creation.Type) &&
                 IsNonCacheableName(creation.Type?.Name)) ||
                creation.Arguments.Any(argument => Recurse(argument.Value)),
            ILocalReferenceOperation local => ResolveLocal(
                local,
                root,
                resolving),
            IConditionalOperation conditional =>
                Recurse(conditional.WhenTrue) ||
                conditional.WhenFalse != null &&
                Recurse(conditional.WhenFalse),
            ISwitchExpressionOperation switchExpression =>
                switchExpression.Arms.Any(arm => Recurse(arm.Value)),
            ICoalesceOperation coalesce =>
                Recurse(coalesce.Value) || Recurse(coalesce.WhenNull),
            IPropertyReferenceOperation property => ResolveProperty(
                property,
                resolving),
            IInvocationOperation invocation => ResolveInvocation(
                invocation,
                resolving),
            _ => IsSemanticAnswerType(operation.Type) &&
                operation.ConstantValue is not { HasValue: true }
        };
    }

    private static bool TryClassifySemanticEnumConstant(
        IOperation operation,
        out bool nonCacheable)
    {
        nonCacheable = false;
        if (operation.Type is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Enum
            } enumType ||
            !IsSemanticAnswerType(enumType) ||
            operation.ConstantValue is not
            {
                HasValue: true,
                Value: { } constantValue
            })
        {
            return false;
        }

        nonCacheable = IsNonCacheableEnumConstant(
            enumType,
            constantValue);
        return true;
    }

    private static bool IsNonCacheableNumericEnumValue(
        IOperation operation,
        INamedTypeSymbol enumType,
        IOperation root,
        LocalResolution resolving)
    {
        resolving.CancellationToken.ThrowIfCancellationRequested();
        if (operation.ConstantValue is
            {
                HasValue: true,
                Value: { } constantValue
            })
        {
            return IsNonCacheableEnumConstant(enumType, constantValue);
        }

        operation = UnwrapValue(operation);
        if (operation.ConstantValue is
            {
                HasValue: true,
                Value: { } unwrappedConstant
            })
        {
            return IsNonCacheableEnumConstant(
                enumType,
                unwrappedConstant);
        }

        bool Recurse(IOperation value)
        {
            return IsNonCacheableNumericEnumValue(
                value,
                enumType,
                root,
                resolving);
        }

        return operation switch
        {
            ILocalReferenceOperation local =>
                ResolveNumericEnumLocal(
                    local,
                    enumType,
                    root,
                    resolving),
            IConditionalOperation conditional =>
                Recurse(conditional.WhenTrue) ||
                conditional.WhenFalse != null &&
                Recurse(conditional.WhenFalse),
            ISwitchExpressionOperation switchExpression =>
                switchExpression.Arms.Any(arm => Recurse(arm.Value)),
            ICoalesceOperation coalesce =>
                Recurse(coalesce.Value) || Recurse(coalesce.WhenNull),
            _ => true
        };
    }

    private static bool ResolveNumericEnumLocal(
        ILocalReferenceOperation reference,
        INamedTypeSymbol enumType,
        IOperation root,
        LocalResolution resolving)
    {
        if (!resolving.Add(reference))
        {
            return true;
        }

        try
        {
            var writes = GetReachingLocalValues(
                reference,
                root,
                resolving.CancellationToken);
            return writes.Length == 0 || writes.Any(value =>
                IsSelfReference(value, reference.Local) ||
                IsNonCacheableNumericEnumValue(
                    value,
                    enumType,
                    root,
                    resolving));
        }
        finally
        {
            resolving.Remove(reference);
        }
    }

    private static bool IsNonCacheableEnumConstant(
        INamedTypeSymbol enumType,
        object constantValue)
    {
        var matchingMembers = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field =>
                field.HasConstantValue &&
                field.ConstantValue is { } memberValue &&
                AreEqualIntegralConstants(memberValue, constantValue))
            .ToArray();
        return matchingMembers.Length == 0 ||
            matchingMembers.Any(field => IsNonCacheableName(field.Name));
    }

    private static bool AreEqualIntegralConstants(
        object left,
        object right)
    {
        return TryGetIntegralConstant(left, out var leftValue) &&
            TryGetIntegralConstant(right, out var rightValue) &&
            leftValue == rightValue;
    }

    private static bool TryGetIntegralConstant(
        object value,
        out decimal result)
    {
        switch (value)
        {
            case sbyte signedByte:
                result = signedByte;
                return true;
            case byte unsignedByte:
                result = unsignedByte;
                return true;
            case short signedShort:
                result = signedShort;
                return true;
            case ushort unsignedShort:
                result = unsignedShort;
                return true;
            case int signedInteger:
                result = signedInteger;
                return true;
            case uint unsignedInteger:
                result = unsignedInteger;
                return true;
            case long signedLong:
                result = signedLong;
                return true;
            case ulong unsignedLong:
                result = unsignedLong;
                return true;
            case char character:
                result = character;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool ResolveLocal(
        ILocalReferenceOperation reference,
        IOperation root,
        LocalResolution resolving)
    {
        if (!resolving.Add(reference))
        {
            // A repeated local produced by a plain local initializer or
            // assignment is an alias cycle; the outer frame evaluates the
            // originating definition. Reference/out writes are different:
            // their value is intentionally unknown and must remain flagged.
            return IsPlainLocalAlias(reference) ? false : true;
        }
        try
        {
            var writes = GetReachingLocalValues(
                reference,
                root,
                resolving.CancellationToken);
            if (writes.Length == 0)
            {
                return IsSemanticAnswerType(reference.Type);
            }
            return writes.Any(value =>
                IsSelfReference(value, reference.Local) ||
                IsNonCacheableSemanticAnswer(
                    value,
                    root,
                    resolving));
        }
        finally
        {
            resolving.Remove(reference);
        }
    }

    private static bool IsPlainLocalAlias(ILocalReferenceOperation reference)
    {
        for (var parent = reference.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is IArgumentOperation)
            {
                return false;
            }

            if (parent is IVariableDeclaratorOperation or
                ISimpleAssignmentOperation or
                IDeconstructionAssignmentOperation)
            {
                return true;
            }

            if (parent is IInvocationOperation)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsSelfReference(
        IOperation operation,
        ILocalSymbol local)
    {
        operation = UnwrapValue(operation);
        return operation is ILocalReferenceOperation reference &&
            SymbolEqualityComparer.Default.Equals(reference.Local, local);
    }

    private static IOperation[] GetReachingLocalValues(
        ILocalReferenceOperation reference,
        IOperation root,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var graph = CreateControlFlowGraph(root, cancellationToken);
        if (graph == null)
        {
            return GetPriorLocalValues(
                reference,
                root,
                cancellationToken);
        }

        BasicBlock? target = null;
        foreach (var block in graph.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ContainsLocalReference(
                    block,
                    reference,
                    cancellationToken))
            {
                target = block;
                break;
            }
        }
        if (target == null)
        {
            return GetPriorLocalValues(
                reference,
                root,
                cancellationToken);
        }

        var outputs = CreateBlockStates(graph, cancellationToken);
        var exceptionalInputs = CreateBlockStates(
            graph,
            cancellationToken);
        bool changed;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            changed = false;
            var nextExceptionalInputs = CreateBlockStates(
                graph,
                cancellationToken);
            foreach (var block in RoslynCfgThrowFacts.ReachableBlocks(
                         graph,
                         cancellationToken))
            {
                var input = new HashSet<IOperation>(
                    exceptionalInputs[block.Ordinal]);
                foreach (var predecessor in block.Predecessors)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    input.UnionWith(outputs[predecessor.Source.Ordinal]);
                }
                var exceptional = GetExceptionalLocalValues(
                    block,
                    reference.Local,
                    input,
                    root,
                    cancellationToken);
                foreach (var successor in RoslynCfgThrowFacts.ExceptionalSuccessors(
                             graph,
                             block,
                             cancellationToken))
                {
                    nextExceptionalInputs[successor.Ordinal]
                        .UnionWith(exceptional);
                }
                var output = TransferLocalValues(
                    block,
                    reference.Local,
                    input,
                    null,
                    root,
                    cancellationToken);
                if (!outputs[block.Ordinal].SetEquals(output))
                {
                    outputs[block.Ordinal] = output;
                    changed = true;
                }
            }

            foreach (var block in graph.Blocks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!exceptionalInputs[block.Ordinal].SetEquals(
                        nextExceptionalInputs[block.Ordinal]))
                {
                    changed = true;
                }
            }
            exceptionalInputs = nextExceptionalInputs;
        }
        while (changed);

        var reaching = new HashSet<IOperation>(
            exceptionalInputs[target.Ordinal]);
        foreach (var predecessor in target.Predecessors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reaching.UnionWith(outputs[predecessor.Source.Ordinal]);
        }
        return TransferLocalValues(
                target,
                reference.Local,
                reaching,
                reference,
                root,
                cancellationToken)
            .ToArray();
    }

    private static ControlFlowGraph? CreateControlFlowGraph(
        IOperation root,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var graph = root switch
            {
                IMethodBodyOperation or IConstructorBodyOperation =>
                    RoslynCfgFactory.TryCreateMethodOrConstructorGraph(
                        root, cancellationToken),
                IBlockOperation block => ControlFlowGraph.Create(
                    block,
                    cancellationToken),
                _ => null
            };
            cancellationToken.ThrowIfCancellationRequested();
            return graph;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool ContainsLocalReference(
        BasicBlock block,
        ILocalReferenceOperation reference,
        CancellationToken cancellationToken)
    {
        foreach (var operation in BlockOperations(block))
        {
            foreach (var candidate in operation.DescendantsAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsSameLocalReference(candidate, reference))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static Dictionary<int, HashSet<IOperation>> CreateBlockStates(
        ControlFlowGraph graph,
        CancellationToken cancellationToken)
    {
        var states = new Dictionary<int, HashSet<IOperation>>();
        foreach (var block in graph.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            states.Add(block.Ordinal, []);
        }
        return states;
    }

    private static HashSet<IOperation> TransferLocalValues(
        BasicBlock block,
        ILocalSymbol local,
        IEnumerable<IOperation> input,
        ILocalReferenceOperation? before,
        IOperation root,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<IOperation>(input);
        foreach (var candidate in LocalWriteCandidates(
                     block,
                     root,
                     cancellationToken))
        {
            if (before != null &&
                IsSameLocalReference(candidate, before))
            {
                break;
            }

            var value = GetLocalWriteValue(candidate, local);
            if (value == null)
            {
                continue;
            }

            result.Clear();
            result.Add(value);
        }
        return result;
    }

    private static HashSet<IOperation> GetExceptionalLocalValues(
        BasicBlock block,
        ILocalSymbol local,
        IEnumerable<IOperation> input,
        IOperation root,
        CancellationToken cancellationToken)
    {
        var state = new HashSet<IOperation>(input);
        var exceptional = new HashSet<IOperation>();
        foreach (var candidate in LocalWriteCandidates(
                     block,
                     root,
                     cancellationToken))
        {
            if (RoslynCfgThrowFacts.OperationMayThrow(candidate))
            {
                exceptional.UnionWith(state);
            }

            var value = GetLocalWriteValue(candidate, local);
            if (value == null)
            {
                continue;
            }

            state.Clear();
            state.Add(value);
        }
        return exceptional;
    }

    private static IEnumerable<IOperation> LocalWriteCandidates(
        BasicBlock block,
        IOperation root,
        CancellationToken cancellationToken)
    {
        return BlockOperations(block).SelectMany(operation =>
            InEvaluationOrder(
                operation,
                root,
                cancellationToken));
    }

    private static IEnumerable<IOperation> InEvaluationOrder(
        IOperation operation,
        IOperation root,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<(IOperation Operation, bool ChildrenVisited)>();
        pending.Push((operation, false));
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, childrenVisited) = pending.Pop();
            if (IsInsideNestedCallable(current, root))
            {
                continue;
            }

            if (childrenVisited)
            {
                yield return current;
                continue;
            }

            pending.Push((current, true));
            foreach (var child in current.ChildOperations.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                pending.Push((child, false));
            }
        }
    }

    private static bool IsSameLocalReference(
        IOperation candidate,
        ILocalReferenceOperation reference)
    {
        return candidate is ILocalReferenceOperation local &&
            SymbolEqualityComparer.Default.Equals(
                local.Local,
                reference.Local) &&
            candidate.Syntax.SyntaxTree == reference.Syntax.SyntaxTree &&
            candidate.Syntax.Span == reference.Syntax.Span;
    }

    private static IEnumerable<IOperation> BlockOperations(BasicBlock block)
    {
        return block.Operations.Concat(
            block.BranchValue == null ? [] : [block.BranchValue]);
    }

    private static IOperation? GetLocalWriteValue(
        IOperation candidate,
        ILocalSymbol local)
    {
        return candidate switch
        {
            IVariableDeclaratorOperation declarator
                when SymbolEqualityComparer.Default.Equals(
                    declarator.Symbol,
                    local) => declarator.Initializer?.Value,
            ISimpleAssignmentOperation
            { Target: ILocalReferenceOperation target } assignment
                when SymbolEqualityComparer.Default.Equals(
                    target.Local,
                    local) => assignment.Value,
            IInvocationOperation invocation =>
                GetRefOrOutWriteValue(invocation, local),
            IDeconstructionAssignmentOperation deconstruction =>
                GetDeconstructionWriteValue(
                    UnwrapValue(deconstruction.Target),
                    UnwrapValue(deconstruction.Value),
                    local),
            _ => null
        };
    }

    private static IOperation? GetRefOrOutWriteValue(
        IInvocationOperation invocation,
        ILocalSymbol local)
    {
        if (invocation.TargetMethod.ReducedFrom is
            { Parameters.Length: > 0 } reduced &&
            IsWritableReference(reduced.Parameters[0].RefKind) &&
            FindLocalReference(invocation.Instance, local) is
            { } receiver)
        {
            return receiver;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is { } parameter &&
                IsWritableReference(parameter.RefKind) &&
                FindLocalReference(argument.Value, local) is { } reference)
            {
                return reference;
            }
        }

        return null;
    }

    private static bool IsWritableReference(RefKind refKind)
    {
        return refKind is RefKind.Ref or RefKind.Out;
    }

    private static IOperation? GetDeconstructionWriteValue(
        IOperation target,
        IOperation value,
        ILocalSymbol local)
    {
        if (target is ITupleOperation targetTuple &&
            value is ITupleOperation valueTuple &&
            targetTuple.Elements.Length == valueTuple.Elements.Length)
        {
            for (var index = 0; index < targetTuple.Elements.Length; index++)
            {
                var result = GetDeconstructionWriteValue(
                    UnwrapValue(targetTuple.Elements[index]),
                    UnwrapValue(valueTuple.Elements[index]),
                    local);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        return FindLocalReference(target, local) == null
            ? null
            : value;
    }

    private static ILocalReferenceOperation? FindLocalReference(
        IOperation? operation,
        ILocalSymbol local)
    {
        return operation?.DescendantsAndSelf()
            .OfType<ILocalReferenceOperation>()
            .FirstOrDefault(reference =>
                SymbolEqualityComparer.Default.Equals(
                    reference.Local,
                    local));
    }

    private static IOperation[] GetPriorLocalValues(
        ILocalReferenceOperation reference,
        IOperation root,
        CancellationToken cancellationToken)
    {
        var values = new List<IOperation>();
        foreach (var candidate in root.DescendantsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Syntax.SpanStart >= reference.Syntax.SpanStart ||
                IsInsideNestedCallable(candidate, root))
            {
                continue;
            }

            var value = GetLocalWriteValue(candidate, reference.Local);
            if (value != null)
            {
                values.Add(value);
            }
        }
        return [.. values];
    }

    private static bool IsInsideNestedCallable(IOperation operation, IOperation root)
    {
        for (var current = operation.Parent; current != null && !ReferenceEquals(current, root);
             current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return true;
            }
        }
        return false;
    }

    private static bool ResolveProperty(
        IPropertyReferenceOperation property,
        LocalResolution resolving)
    {
        return IsNonCacheableReturnedValue(
            property.Property,
            property.Type,
            property.Property.Name,
            resolving.Compilation,
            resolving.CancellationToken);
    }

    private static bool ResolveInvocation(
        IInvocationOperation invocation,
        LocalResolution resolving)
    {
        return IsNonCacheableReturnedValue(
            invocation.TargetMethod,
            invocation.Type,
            invocation.TargetMethod.Name,
            resolving.Compilation,
            resolving.CancellationToken);
    }

    private static bool IsNonCacheableReturnedValue(
        ISymbol symbol,
        ITypeSymbol? returnType,
        string fallbackName,
        Compilation? compilation,
        CancellationToken cancellationToken)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        var resolving = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var target in GetPossibleDispatchTargets(
                     symbol,
                     compilation,
                     cancellationToken))
        {
            names.AddRange(GetReturnedValueNames(target, resolving));
        }
        return names.Count == 0
            ? IsSemanticAnswerType(returnType) &&
              IsNonCacheableName(fallbackName)
            : names.Any(IsNonCacheableName);
    }

    private static ImmutableArray<ISymbol> GetPossibleDispatchTargets(
        ISymbol symbol,
        Compilation? compilation,
        CancellationToken cancellationToken)
    {
        var targets = ImmutableArray.CreateBuilder<ISymbol>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        AddTarget(symbol, targets, seen);
        if (compilation == null ||
            symbol.ContainingType == null ||
            (!symbol.IsVirtual &&
             !symbol.IsAbstract &&
             symbol.ContainingType.TypeKind != TypeKind.Interface))
        {
            return targets.ToImmutable();
        }

        var dispatchTargetCache = DispatchTargetCaches.GetValue(
            compilation,
            static _ => new ConcurrentDictionary<ISymbol, ImmutableArray<ISymbol>>(
                SymbolEqualityComparer.Default));
        if (dispatchTargetCache.TryGetValue(symbol, out var cached))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return cached;
        }

        foreach (var type in GetSourceTypes(compilation.Assembly.GlobalNamespace))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol.ContainingType.TypeKind == TypeKind.Interface)
            {
                AddTarget(
                    type.FindImplementationForInterfaceMember(symbol),
                    targets,
                    seen);
                continue;
            }

            foreach (var member in type.GetMembers(symbol.Name))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Overrides(member, symbol))
                {
                    AddTarget(member, targets, seen);
                }
            }
        }
        var result = targets.ToImmutable();
        dispatchTargetCache.TryAdd(symbol, result);
        return result;
    }

    private static void AddTarget(
        ISymbol? symbol,
        ImmutableArray<ISymbol>.Builder targets,
        HashSet<ISymbol> seen)
    {
        if (symbol != null && seen.Add(symbol))
        {
            targets.Add(symbol);
        }
    }

    private static bool Overrides(ISymbol candidate, ISymbol target)
    {
        switch (candidate, target)
        {
            case (IMethodSymbol method, IMethodSymbol targetMethod):
                for (var current = method.OverriddenMethod;
                     current != null;
                     current = current.OverriddenMethod)
                {
                    if (SymbolEqualityComparer.Default.Equals(
                            current.OriginalDefinition,
                            targetMethod.OriginalDefinition))
                    {
                        return true;
                    }
                }
                break;
            case (IPropertySymbol property, IPropertySymbol targetProperty):
                for (var current = property.OverriddenProperty;
                     current != null;
                     current = current.OverriddenProperty)
                {
                    if (SymbolEqualityComparer.Default.Equals(
                            current.OriginalDefinition,
                            targetProperty.OriginalDefinition))
                    {
                        return true;
                    }
                }
                break;
        }
        return false;
    }

    private static IEnumerable<INamedTypeSymbol> GetSourceTypes(
        INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            foreach (var nested in GetTypeAndNestedTypes(type))
            {
                yield return nested;
            }
        }

        foreach (var child in @namespace.GetNamespaceMembers())
        {
            foreach (var type in GetSourceTypes(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetTypeAndNestedTypes(
        INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var descendant in GetTypeAndNestedTypes(nested))
            {
                yield return descendant;
            }
        }
    }

    private static ImmutableArray<string> GetReturnedValueNames(
        ISymbol symbol,
        HashSet<ISymbol> resolving)
    {
        if (!resolving.Add(symbol))
        {
            return ["Unknown"];
        }

        try
        {
            var names = ImmutableArray.CreateBuilder<string>();
            foreach (var reference in symbol.DeclaringSyntaxReferences)
            {
                var syntax = reference.GetSyntax();
                foreach (var expression in GetReturnExpressions(syntax))
                {
                    names.AddRange(GetExpressionValueNames(
                        expression,
                        symbol,
                        syntax,
                        resolving,
                        new HashSet<string>(StringComparer.Ordinal)));
                }
            }
            return names.ToImmutable();
        }
        finally
        {
            resolving.Remove(symbol);
        }
    }

    private static IEnumerable<ExpressionSyntax> GetReturnExpressions(
        SyntaxNode syntax)
    {
        var arrows = new List<ExpressionSyntax>();
        var returns = new List<ExpressionSyntax>();
        foreach (var node in syntax.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case ArrowExpressionClauseSyntax arrow:
                    arrows.Add(arrow.Expression);
                    break;
                case ReturnStatementSyntax
                {
                    Expression: { } expression
                } statement when !IsSyntacticallyUnreachableReturn(statement):
                    returns.Add(expression);
                    break;
            }
        }

        return arrows.Concat(returns)
            .Where(expression => !IsInsideNestedCallable(expression, syntax));
    }

    // Roslyn's operation CFG is not available at this syntax-only stage. Keep
    // the conservative fallback for unknown conditions, but avoid treating a
    // return in an obviously disabled constant branch as a possible result.
    private static bool IsSyntacticallyUnreachableReturn(ReturnStatementSyntax statement)
    {
        for (SyntaxNode? current = statement; current?.Parent != null; current = current.Parent)
        {
            if (current.Parent is IfStatementSyntax @if)
            {
                bool? condition = GetBooleanConstant(@if.Condition);
                if (condition.HasValue)
                {
                    bool inThen = @if.Statement.Span.Contains(statement.Span);
                    bool inElse = @if.Else?.Statement.Span.Contains(statement.Span) == true;
                    if ((inThen && !condition.Value) || (inElse && condition.Value))
                    {
                        return true;
                    }
                }
            }
            else if (current.Parent is WhileStatementSyntax @while &&
                     GetBooleanConstant(@while.Condition) == false)
            {
                return true;
            }
            else if (current.Parent is ForStatementSyntax @for &&
                     @for.Condition != null && GetBooleanConstant(@for.Condition) == false)
            {
                return true;
            }
        }
        return false;
    }

    private static bool? GetBooleanConstant(ExpressionSyntax expression)
    {
        expression = UnwrapSyntax(expression);
        return expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression) => true,
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression) => false,
            _ => null
        };
    }

    private static ImmutableArray<string> GetExpressionValueNames(
        ExpressionSyntax expression,
        ISymbol owner,
        SyntaxNode syntax,
        HashSet<ISymbol> resolving,
        HashSet<string> resolvingNames)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        void AddNames(ExpressionSyntax value)
        {
            names.AddRange(GetExpressionValueNames(
                value,
                owner,
                syntax,
                resolving,
                resolvingNames));
        }

        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                AddNames(parenthesized.Expression);
                break;
            case CastExpressionSyntax cast:
                AddNames(cast.Expression);
                break;
            case ConditionalExpressionSyntax conditional:
                AddNames(conditional.WhenTrue);
                AddNames(conditional.WhenFalse);
                break;
            case SwitchExpressionSyntax switchExpression:
                foreach (var arm in switchExpression.Arms)
                {
                    AddNames(arm.Expression);
                }
                break;
            case BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.CoalesceExpression):
                AddNames(binary.Left);
                AddNames(binary.Right);
                break;
            case IdentifierNameSyntax identifier:
                names.AddRange(GetIdentifierValueNames(
                    identifier,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                break;
            case MemberAccessExpressionSyntax member:
                names.Add(member.Name.Identifier.ValueText);
                names.AddRange(GetMemberValueNames(
                    member.Name.Identifier.ValueText,
                    owner,
                    resolving));
                break;
            case InvocationExpressionSyntax invocation:
                var name = GetInvokedName(invocation.Expression);
                if (name != null)
                {
                    names.Add(name);
                    names.AddRange(GetMemberValueNames(name, owner, resolving));
                }
                break;
            case ObjectCreationExpressionSyntax creation:
                names.Add(creation.Type.ToString());
                if (creation.ArgumentList != null)
                {
                    foreach (var argument in creation.ArgumentList.Arguments)
                    {
                        AddNames(argument.Expression);
                    }
                }
                break;
            case ImplicitObjectCreationExpressionSyntax implicitCreation:
                foreach (var argument in implicitCreation.ArgumentList.Arguments)
                {
                    AddNames(argument.Expression);
                }
                break;
        }
        return names.ToImmutable();
    }

    private static ImmutableArray<string> GetIdentifierValueNames(
        IdentifierNameSyntax identifier,
        ISymbol owner,
        SyntaxNode syntax,
        HashSet<ISymbol> resolving,
        HashSet<string> resolvingNames)
    {
        var name = identifier.Identifier.ValueText;
        if (!resolvingNames.Add(name))
        {
            return ["Unknown"];
        }

        try
        {
            var values = syntax.DescendantNodes()
                .Where(candidate =>
                    candidate.SpanStart < identifier.SpanStart &&
                    !IsInsideNestedCallable(candidate, syntax))
                .Select(candidate => GetSyntacticLocalWrite(candidate, name))
                .Where(static value => value != null)
                .Cast<ExpressionSyntax>()
                .ToArray();
            if (values.Length == 0)
            {
                return [name];
            }

            var names = ImmutableArray.CreateBuilder<string>();
            foreach (var value in values)
            {
                names.AddRange(GetExpressionValueNames(
                    value, owner, syntax, resolving, resolvingNames));
            }
            return names.ToImmutable();
        }
        finally
        {
            resolvingNames.Remove(name);
        }
    }

    private static ImmutableArray<string> GetMemberValueNames(
        string name,
        ISymbol owner,
        HashSet<ISymbol> resolving)
    {
        if (owner.ContainingType == null)
        {
            return [];
        }

        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var member in owner.ContainingType.GetMembers(name))
        {
            if (member is IMethodSymbol or IPropertySymbol)
            {
                names.AddRange(GetReturnedValueNames(member, resolving));
            }
        }
        return names.ToImmutable();
    }

    private static string? GetInvokedName(ExpressionSyntax expression)
    {
        return expression switch
        {
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            MemberAccessExpressionSyntax member =>
                member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding =>
                binding.Name.Identifier.ValueText,
            _ => null
        };
    }

    private static ExpressionSyntax? GetSyntacticLocalWrite(
        SyntaxNode syntax,
        string name)
    {
        return syntax switch
        {
            VariableDeclaratorSyntax declarator
                when declarator.Identifier.ValueText == name =>
                declarator.Initializer?.Value,
            AssignmentExpressionSyntax assignment
                when assignment.Left is IdentifierNameSyntax identifier &&
                     identifier.Identifier.ValueText == name =>
                assignment.Right,
            _ => null
        };
    }

    private static bool IsInsideNestedCallable(
        SyntaxNode node,
        SyntaxNode root)
    {
        return node.Ancestors()
            .TakeWhile(ancestor => !ReferenceEquals(ancestor, root))
            .Any(static ancestor => ancestor is
                AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
    }

    private static bool IsSemanticAnswerType(ITypeSymbol? type)
    {
        if (type == null || !IsSharpProofNamespace(type.ContainingNamespace))
        {
            return false;
        }
        return type.Name.IndexOf("Answer", StringComparison.Ordinal) >= 0 ||
               type.Name.IndexOf("Result", StringComparison.Ordinal) >= 0 ||
               type.Name.IndexOf("Outcome", StringComparison.Ordinal) >= 0 ||
               IsWorkerVerifyResponse(type);
    }

    private static bool IsWorkerVerifyResponse(ITypeSymbol? type)
    {
        return type != null &&
               string.Equals(
                   type.Name,
                   "WorkerVerifyResponse",
                   StringComparison.Ordinal) &&
               SharpProofSoundnessAnalyzer.IsExactNamespace(
                   type.ContainingNamespace,
                   "SharpProof",
                   "Worker",
                   "Protocol");
    }

    private static bool IsSharpProofNamespace(INamespaceSymbol? symbol)
    {
        while (symbol is { IsGlobalNamespace: false, ContainingNamespace: { } parent } &&
               !parent.IsGlobalNamespace)
        {
            symbol = parent;
        }
        return string.Equals(symbol?.Name, "SharpProof", StringComparison.Ordinal);
    }

    private static bool IsNonCacheableName(string? name)
    {
        return name != null &&
               (string.Equals(name, "Unknown", StringComparison.Ordinal) ||
                name.IndexOf("Timeout", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("TimedOut", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Error", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Failure", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Failed", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Abstain", StringComparison.Ordinal) >= 0);
    }

    private sealed class LocalResolution
    {
        private readonly HashSet<ILocalSymbol> _locals = new(
            SymbolEqualityComparer.Default);
        private readonly Dictionary<ILocalSymbol, HashSet<TextSpan>> _points =
            new(SymbolEqualityComparer.Default);

        internal LocalResolution(
            CancellationToken cancellationToken,
            Compilation? compilation = null)
        {
            CancellationToken = cancellationToken;
            Compilation = compilation;
        }

        internal CancellationToken CancellationToken
        {
            get;
        }

        internal Compilation? Compilation
        {
            get;
        }

        internal bool Add(ILocalSymbol local)
        {
            CancellationToken.ThrowIfCancellationRequested();
            return _locals.Add(local);
        }

        internal void Remove(ILocalSymbol local)
        {
            _locals.Remove(local);
        }

        internal bool Add(ILocalReferenceOperation reference)
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (!_points.TryGetValue(reference.Local, out var points))
            {
                points = [];
                _points.Add(reference.Local, points);
            }

            return points.Add(reference.Syntax.Span);
        }

        internal void Remove(ILocalReferenceOperation reference)
        {
            if (!_points.TryGetValue(reference.Local, out var points))
            {
                return;
            }

            points.Remove(reference.Syntax.Span);
            if (points.Count == 0)
            {
                _points.Remove(reference.Local);
            }
        }
    }
}
