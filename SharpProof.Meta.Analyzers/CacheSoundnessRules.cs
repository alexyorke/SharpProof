using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Meta.Analyzers;

internal static class CacheSoundnessRules
{
    private static readonly ImmutableHashSet<string> WriteMethods =
        new[] { "Add", "AddOrUpdate", "GetOrAdd", "Set", "TryAdd", "TryUpdate", "TryWrite", "TryWriteAsync", "Write", "WriteAsync" }
            .ToImmutableHashSet(StringComparer.Ordinal);

    internal static void AnalyzeWrite(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        var root = Root(invocation);
        if (ForwardsNonCacheableSemanticAnswer(
                invocation,
                root,
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
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)) ||
            !invocation.Arguments.Any(argument =>
                !IsGuardedCacheableResponse(invocation, argument.Value) &&
                (IsNonCacheableSemanticAnswer(
                     argument.Value,
                     root,
                     new HashSet<ILocalSymbol>(
                         SymbolEqualityComparer.Default)) ||
                 IsNonCacheableGetOrAddFactory(
                     invocation,
                     argument,
                     root))))
        {
            return;
        }

        Report(context, invocation.Syntax.GetLocation());
    }

    private static bool ForwardsNonCacheableSemanticAnswer(
        IInvocationOperation invocation,
        IOperation root,
        CancellationToken cancellationToken)
    {
        var method = invocation.TargetMethod.OriginalDefinition;
        if (method.DeclaringSyntaxReferences.Length == 0)
        {
            return false;
        }

        foreach (var argument in invocation.Arguments)
        {
            var ordinal = argument.Parameter?.Ordinal ?? -1;
            if (ordinal < 0 ||
                ordinal >= method.Parameters.Length ||
                method.Parameters[ordinal].Type is not ITypeParameterSymbol ||
                !IsNonCacheableSemanticAnswer(
                    argument.Value,
                    root,
                    new HashSet<ILocalSymbol>(
                        SymbolEqualityComparer.Default)))
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
            var declaration = reference.GetSyntax(cancellationToken);
            foreach (var invocation in declaration.DescendantNodesAndSelf()
                         .OfType<InvocationExpressionSyntax>()
                         .Where(candidate =>
                             !IsInsideNestedCallable(candidate, declaration)))
            {
                if (!WriteMethods.Contains(
                        GetInvokedName(invocation.Expression) ?? string.Empty) ||
                    !IsSyntacticCacheReceiver(invocation.Expression, method) ||
                    !invocation.ArgumentList.Arguments.Any(argument =>
                        IsForwardedParameter(
                            argument.Expression,
                            parameter.Name)))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
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
        IOperation root)
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
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
    }

    private static bool IsNonCacheableValueFactory(
        IOperation operation,
        IOperation root,
        HashSet<ILocalSymbol> resolving)
    {
        operation = UnwrapValue(operation);
        return operation switch
        {
            IDelegateCreationOperation creation =>
                IsNonCacheableValueFactory(
                    creation.Target,
                    root,
                    resolving),
            IAnonymousFunctionOperation anonymous =>
                IsNonCacheableAnonymousFactory(anonymous),
            IMethodReferenceOperation method =>
                IsNonCacheableReturnedValue(
                    method.Method,
                    method.Method.ReturnType,
                    method.Method.Name),
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
        IAnonymousFunctionOperation factory)
    {
        var returns = factory.Body.DescendantsAndSelf()
            .OfType<IReturnOperation>()
            .Where(operation =>
                !IsInsideNestedCallable(operation, factory.Body))
            .Select(static operation => operation.ReturnedValue)
            .Where(static value => value != null)
            .Cast<IOperation>()
            .ToArray();
        return returns.Length == 0 || returns.Any(value =>
            IsNonCacheableSemanticAnswer(
                value,
                factory.Body,
                new HashSet<ILocalSymbol>(
                    SymbolEqualityComparer.Default)));
    }

    private static bool ResolveValueFactoryLocal(
        ILocalReferenceOperation reference,
        IOperation root,
        HashSet<ILocalSymbol> resolving)
    {
        if (!resolving.Add(reference.Local))
        {
            return true;
        }

        try
        {
            var writes = GetReachingLocalValues(reference, root);
            return writes.Length == 0 || writes.Any(value =>
                IsNonCacheableValueFactory(value, root, resolving));
        }
        finally
        {
            resolving.Remove(reference.Local);
        }
    }

    internal static void AnalyzeAssignment(OperationAnalysisContext context)
    {
        var assignment = (IAssignmentOperation)context.Operation;
        var root = Root(assignment);
        if (!IsCacheAssignmentTarget(assignment.Target, root) ||
            !IsNonCacheableSemanticAnswer(
                assignment.Value,
                root,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)))
        {
            return;
        }

        Report(context, assignment.Syntax.GetLocation());
    }

    private static bool IsCacheAssignmentTarget(
        IOperation target,
        IOperation root)
    {
        var resolving = new HashSet<ILocalSymbol>(
            SymbolEqualityComparer.Default);
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
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation
                { OperatorMethod: null } conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
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
        HashSet<ILocalSymbol> resolving)
    {
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
        HashSet<ILocalSymbol> resolving)
    {
        if (!resolving.Add(reference.Local))
        {
            return false;
        }

        try
        {
            return GetReachingLocalValues(reference, root).Any(value =>
                IsCacheReceiver(value, null, root, resolving));
        }
        finally
        {
            resolving.Remove(reference.Local);
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
        HashSet<ILocalSymbol> resolving)
    {
        if (TryClassifySemanticEnumConstant(operation, out var nonCacheable))
        {
            return nonCacheable;
        }

        operation = UnwrapValue(operation);
        if (TryClassifySemanticEnumConstant(operation, out nonCacheable))
        {
            return nonCacheable;
        }

        return operation switch
        {
            IFieldReferenceOperation field
                when field.Field.ContainingType.TypeKind == TypeKind.Enum &&
                     IsSemanticAnswerType(field.Type) => IsNonCacheableName(field.Field.Name),
            IObjectCreationOperation creation =>
                (IsSemanticAnswerType(creation.Type) &&
                 IsNonCacheableName(creation.Type?.Name)) ||
                creation.Arguments.Any(argument =>
                    IsNonCacheableSemanticAnswer(
                        argument.Value,
                        root,
                        resolving)),
            ILocalReferenceOperation local => ResolveLocal(
                local,
                root,
                resolving),
            IConditionalOperation conditional =>
                IsNonCacheableSemanticAnswer(
                    conditional.WhenTrue,
                    root,
                    resolving) ||
                conditional.WhenFalse != null &&
                IsNonCacheableSemanticAnswer(
                    conditional.WhenFalse,
                    root,
                    resolving),
            ISwitchExpressionOperation switchExpression =>
                switchExpression.Arms.Any(arm =>
                    IsNonCacheableSemanticAnswer(
                        arm.Value,
                        root,
                        resolving)),
            ICoalesceOperation coalesce =>
                IsNonCacheableSemanticAnswer(
                    coalesce.Value,
                    root,
                    resolving) ||
                IsNonCacheableSemanticAnswer(
                    coalesce.WhenNull,
                    root,
                    resolving),
            IPropertyReferenceOperation property => ResolveProperty(property),
            IInvocationOperation invocation => ResolveInvocation(invocation),
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

        var matchingMembers = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field =>
                field.HasConstantValue &&
                Equals(field.ConstantValue, constantValue))
            .ToArray();
        nonCacheable = matchingMembers.Length == 0 ||
            matchingMembers.Any(field => IsNonCacheableName(field.Name));
        return true;
    }

    private static bool ResolveLocal(
        ILocalReferenceOperation reference,
        IOperation root,
        HashSet<ILocalSymbol> resolving)
    {
        if (!resolving.Add(reference.Local))
        {
            return true;
        }
        try
        {
            var writes = GetReachingLocalValues(reference, root);
            if (writes.Length == 0)
            {
                return IsSemanticAnswerType(reference.Type);
            }
            return writes.Any(value =>
                IsNonCacheableSemanticAnswer(
                    value,
                    root,
                    resolving));
        }
        finally
        {
            resolving.Remove(reference.Local);
        }
    }

    private static IOperation[] GetReachingLocalValues(
        ILocalReferenceOperation reference,
        IOperation root)
    {
        var graph = CreateControlFlowGraph(root);
        if (graph == null)
        {
            return GetPriorLocalValues(reference, root);
        }

        var target = graph.Blocks.FirstOrDefault(block =>
            BlockOperations(block).Any(operation =>
                operation.DescendantsAndSelf().Any(candidate =>
                    IsSameLocalReference(candidate, reference))));
        if (target == null)
        {
            return GetPriorLocalValues(reference, root);
        }

        var outputs = graph.Blocks.ToDictionary(
            static block => block.Ordinal,
            static _ => new HashSet<IOperation>());
        var exceptionalInputs = graph.Blocks.ToDictionary(
            static block => block.Ordinal,
            static _ => new HashSet<IOperation>());
        bool changed;
        do
        {
            changed = false;
            var nextExceptionalInputs = graph.Blocks.ToDictionary(
                static block => block.Ordinal,
                static _ => new HashSet<IOperation>());
            foreach (var block in graph.Blocks.Where(static block => block.IsReachable))
            {
                var input = new HashSet<IOperation>(
                    exceptionalInputs[block.Ordinal]);
                foreach (var predecessor in block.Predecessors)
                {
                    input.UnionWith(outputs[predecessor.Source.Ordinal]);
                }
                var exceptional = GetExceptionalLocalValues(
                    block,
                    reference.Local,
                    input,
                    root);
                foreach (var successor in ExceptionalSuccessors(graph, block))
                {
                    nextExceptionalInputs[successor.Ordinal]
                        .UnionWith(exceptional);
                }
                var output = TransferLocalValues(
                    block,
                    reference.Local,
                    input,
                    null,
                    root);
                if (!outputs[block.Ordinal].SetEquals(output))
                {
                    outputs[block.Ordinal] = output;
                    changed = true;
                }
            }

            foreach (var block in graph.Blocks)
            {
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
            reaching.UnionWith(outputs[predecessor.Source.Ordinal]);
        }
        return TransferLocalValues(
                target,
                reference.Local,
                reaching,
                reference,
                root)
            .ToArray();
    }

    private static ControlFlowGraph? CreateControlFlowGraph(IOperation root)
    {
        try
        {
            return root switch
            {
                IMethodBodyOperation method => ControlFlowGraph.Create(method),
                IConstructorBodyOperation constructor =>
                    ControlFlowGraph.Create(constructor),
                IBlockOperation block => ControlFlowGraph.Create(block),
                _ => null
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static HashSet<IOperation> TransferLocalValues(
        BasicBlock block,
        ILocalSymbol local,
        IEnumerable<IOperation> input,
        ILocalReferenceOperation? before,
        IOperation root)
    {
        var result = new HashSet<IOperation>(input);
        foreach (var candidate in BlockOperations(block)
                     .SelectMany(operation =>
                         InEvaluationOrder(operation, root)))
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
        IOperation root)
    {
        var state = new HashSet<IOperation>(input);
        var exceptional = new HashSet<IOperation>();
        foreach (var candidate in BlockOperations(block)
                     .SelectMany(operation =>
                         InEvaluationOrder(operation, root)))
        {
            if (OperationMayThrow(candidate))
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

    private static bool OperationMayThrow(IOperation operation)
    {
        if (operation is IConversionOperation conversion)
        {
            return conversion.IsChecked ||
                (!conversion.IsTryCast && !conversion.IsImplicit &&
                 (conversion.Conversion.IsReference ||
                  conversion.Operand.Type?.IsReferenceType == true &&
                  conversion.Type?.IsValueType == true));
        }

        return operation is
            IThrowOperation or
            IInvocationOperation or
            IDynamicInvocationOperation or
            IDynamicObjectCreationOperation or
            IDynamicIndexerAccessOperation or
            IFunctionPointerInvocationOperation or
            IObjectCreationOperation or
            IArrayCreationOperation or
            IArrayElementReferenceOperation or
            IDynamicMemberReferenceOperation or
            IFieldReferenceOperation { Instance: not null } or
            IPropertyReferenceOperation or
            IEventAssignmentOperation or
            ILockOperation or
            IAwaitOperation or
            ICompoundAssignmentOperation { IsChecked: true } or
            ICompoundAssignmentOperation
            {
                OperatorKind: BinaryOperatorKind.Divide or
                    BinaryOperatorKind.Remainder
            } or
            IBinaryOperation { IsChecked: true } or
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Divide or
                    BinaryOperatorKind.Remainder
            } or
            IUnaryOperation { IsChecked: true } or
            IIncrementOrDecrementOperation { IsChecked: true };
    }

    private static IEnumerable<BasicBlock> ExceptionalSuccessors(
        ControlFlowGraph graph,
        BasicBlock block)
    {
        var yielded = new HashSet<int>();
        for (var region = block.EnclosingRegion;
             region != null;
             region = region.EnclosingRegion)
        {
            if (region.Kind != ControlFlowRegionKind.Try ||
                region.EnclosingRegion is not { } owner)
            {
                continue;
            }

            foreach (var handler in owner.NestedRegions.Where(candidate =>
                         candidate.Kind is ControlFlowRegionKind.Filter or
                             ControlFlowRegionKind.Catch or
                             ControlFlowRegionKind.FilterAndHandler or
                             ControlFlowRegionKind.Finally))
            {
                if (yielded.Add(handler.FirstBlockOrdinal))
                {
                    yield return graph.Blocks[handler.FirstBlockOrdinal];
                }
            }
        }
    }

    private static IEnumerable<IOperation> InEvaluationOrder(
        IOperation operation,
        IOperation root)
    {
        var pending = new Stack<(IOperation Operation, bool ChildrenVisited)>();
        pending.Push((operation, false));
        while (pending.Count != 0)
        {
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
                GetDeconstructionWriteValue(deconstruction, local),
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
        IDeconstructionAssignmentOperation assignment,
        ILocalSymbol local)
    {
        return GetDeconstructionWriteValue(
            UnwrapValue(assignment.Target),
            UnwrapValue(assignment.Value),
            local);
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
        IOperation root)
    {
        return root.DescendantsAndSelf()
            .Where(candidate =>
                candidate.Syntax.SpanStart < reference.Syntax.SpanStart &&
                !IsInsideNestedCallable(candidate, root))
            .Select(candidate => GetLocalWriteValue(candidate, reference.Local))
            .Where(static value => value != null)
            .Cast<IOperation>()
            .ToArray();
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

    private static bool ResolveProperty(IPropertyReferenceOperation property)
    {
        return IsNonCacheableReturnedValue(
            property.Property,
            property.Type,
            property.Property.Name);
    }

    private static bool ResolveInvocation(IInvocationOperation invocation)
    {
        return IsNonCacheableReturnedValue(
            invocation.TargetMethod,
            invocation.Type,
            invocation.TargetMethod.Name);
    }

    private static bool IsNonCacheableReturnedValue(
        ISymbol symbol,
        ITypeSymbol? returnType,
        string fallbackName)
    {
        var names = GetReturnedValueNames(
            symbol,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default));
        return names.Length == 0
            ? IsSemanticAnswerType(returnType) &&
              IsNonCacheableName(fallbackName)
            : names.Any(IsNonCacheableName);
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
        return syntax.DescendantNodesAndSelf()
            .OfType<ArrowExpressionClauseSyntax>()
            .Select(static arrow => arrow.Expression)
            .Concat(syntax.DescendantNodesAndSelf()
                .OfType<ReturnStatementSyntax>()
                .Where(static statement => statement.Expression != null)
                .Select(static statement => statement.Expression!))
            .Where(expression => !IsInsideNestedCallable(expression, syntax));
    }

    private static ImmutableArray<string> GetExpressionValueNames(
        ExpressionSyntax expression,
        ISymbol owner,
        SyntaxNode syntax,
        HashSet<ISymbol> resolving,
        HashSet<string> resolvingNames)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                names.AddRange(GetExpressionValueNames(
                    parenthesized.Expression,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                break;
            case CastExpressionSyntax cast:
                names.AddRange(GetExpressionValueNames(
                    cast.Expression,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                break;
            case ConditionalExpressionSyntax conditional:
                names.AddRange(GetExpressionValueNames(
                    conditional.WhenTrue,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                names.AddRange(GetExpressionValueNames(
                    conditional.WhenFalse,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                break;
            case SwitchExpressionSyntax switchExpression:
                foreach (var arm in switchExpression.Arms)
                {
                    names.AddRange(GetExpressionValueNames(
                        arm.Expression,
                        owner,
                        syntax,
                        resolving,
                        resolvingNames));
                }
                break;
            case BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.CoalesceExpression):
                names.AddRange(GetExpressionValueNames(
                    binary.Left,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                names.AddRange(GetExpressionValueNames(
                    binary.Right,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
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
                        names.AddRange(GetExpressionValueNames(
                            argument.Expression,
                            owner,
                            syntax,
                            resolving,
                            resolvingNames));
                    }
                }
                break;
            case ImplicitObjectCreationExpressionSyntax implicitCreation:
                foreach (var argument in implicitCreation.ArgumentList.Arguments)
                {
                    names.AddRange(GetExpressionValueNames(
                        argument.Expression,
                        owner,
                        syntax,
                        resolving,
                        resolvingNames));
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
                    value,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
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
               IsExactNamespace(
                   type.ContainingNamespace,
                   "SharpProof",
                   "Worker",
                   "Protocol");
    }

    private static bool IsExactNamespace(
        INamespaceSymbol? value,
        params string[] expected)
    {
        var current = value;
        for (var index = expected.Length - 1; index >= 0; index--)
        {
            if (current == null ||
                current.IsGlobalNamespace ||
                !string.Equals(
                    current.Name,
                    expected[index],
                    StringComparison.Ordinal))
            {
                return false;
            }

            current = current.ContainingNamespace;
        }

        return current?.IsGlobalNamespace == true;
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
}
