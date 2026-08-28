using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Meta.Analyzers;

internal static class CacheSoundnessRules
{
    private static readonly ImmutableHashSet<string> WriteMethods =
        new[] { "Add", "AddOrUpdate", "GetOrAdd", "Set", "TryAdd", "TryUpdate", "TryWrite", "TryWriteAsync", "Write", "WriteAsync" }
            .ToImmutableHashSet(StringComparer.Ordinal);

    internal static void AnalyzeWrite(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols)
    {
        var root = Root(invocation);
        var ownerType = invocation.Instance == null
            ? invocation.TargetMethod.ContainingType
            : ResolveCacheOwnerType(invocation.Instance, root);
        if (WriteMethods.Contains(invocation.TargetMethod.Name) &&
            IsCacheType(ownerType, symbols) &&
            StoredValueArguments(invocation).Any(argument => IsNonCacheableSemanticAnswer(
                argument.Value, root,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default))))
        {
            Report(context, invocation.Syntax.GetLocation());
        }

        AnalyzeRefArguments(context, invocation, symbols);
    }

    private static IEnumerable<IArgumentOperation> StoredValueArguments(
        IInvocationOperation invocation)
    {
        if (!string.Equals(
                invocation.TargetMethod.Name,
                "TryUpdate",
                StringComparison.Ordinal))
        {
            return invocation.Arguments;
        }

        return invocation.Arguments.Where(argument =>
            argument.Parameter is { } parameter &&
            (string.Equals(parameter.Name, "newValue", StringComparison.Ordinal) ||
             parameter.Ordinal == 1));
    }

    internal static void AnalyzeAssignment(
        OperationAnalysisContext context,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols)
    {
        if (context.Operation is not IAssignmentOperation assignment)
        {
            return;
        }
        if (assignment is IDeconstructionAssignmentOperation deconstruction)
        {
            AnalyzeDeconstructionTarget(
                context,
                symbols,
                deconstruction.Target,
                deconstruction.Value,
                Root(assignment),
                new HashSet<ISymbol>(SymbolEqualityComparer.Default));
            return;
        }

        AnalyzeAssignmentTarget(
            context,
            symbols,
            assignment.Target,
            assignment.Value,
            Root(assignment));
    }

    private static void AnalyzeDeconstructionTarget(
        OperationAnalysisContext context,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols,
        IOperation target,
        IOperation value,
        IOperation root,
        HashSet<ISymbol> resolving)
    {
        target = UnwrapAssignmentOperation(target);
        value = UnwrapAssignmentOperation(value);
        if (target is ITupleOperation targetTuple)
        {
            for (var index = 0; index < targetTuple.Elements.Length; index++)
            {
                var element = TryResolveTupleElement(
                    context, value, index, root, resolving);
                if (element == null)
                {
                    AnalyzeUnknownAssignmentTarget(
                        context, symbols, targetTuple.Elements[index], root);
                }
                else
                {
                    AnalyzeDeconstructionTarget(
                        context,
                        symbols,
                        targetTuple.Elements[index],
                        element,
                        root,
                        resolving);
                }
            }
            return;
        }

        AnalyzeAssignmentTarget(context, symbols, target, value, root);
    }

    private static IOperation? TryResolveTupleElement(
        OperationAnalysisContext context,
        IOperation value,
        int index,
        IOperation root,
        HashSet<ISymbol> resolving)
    {
        value = UnwrapAssignmentOperation(value);
        if (value is ITupleOperation tuple)
        {
            return index < tuple.Elements.Length
                ? tuple.Elements[index]
                : null;
        }

        if (value is ILocalReferenceOperation local &&
            resolving.Add(local.Local))
        {
            try
            {
                var write = root.DescendantsAndSelf()
                    .Where(candidate =>
                        candidate.Syntax.SpanStart <= value.Syntax.SpanStart &&
                        candidate switch
                        {
                            IVariableDeclaratorOperation declarator =>
                                SymbolEqualityComparer.Default.Equals(
                                    declarator.Symbol, local.Local) &&
                                declarator.Initializer != null,
                            ISimpleAssignmentOperation assignment =>
                                assignment.Target is ILocalReferenceOperation target &&
                                SymbolEqualityComparer.Default.Equals(
                                    target.Local, local.Local),
                            ICompoundAssignmentOperation assignment =>
                                assignment.Target is ILocalReferenceOperation target &&
                                SymbolEqualityComparer.Default.Equals(
                                    target.Local, local.Local),
                            _ => false
                        })
                    .Select(candidate => candidate switch
                    {
                        IVariableDeclaratorOperation declarator =>
                            declarator.Initializer!.Value,
                        ISimpleAssignmentOperation assignment => assignment.Value,
                        ICompoundAssignmentOperation assignment => assignment.Value,
                        _ => null
                    })
                    .Where(static candidate => candidate != null)
                    .Cast<IOperation>()
                    .OrderBy(static candidate => candidate.Syntax.SpanStart)
                    .LastOrDefault();
                return write == null
                    ? null
                    : TryResolveTupleElement(
                        context, write, index, root, resolving);
            }
            finally
            {
                resolving.Remove(local.Local);
            }
        }

        if (value is IInvocationOperation invocation &&
            resolving.Add(invocation.TargetMethod))
        {
            try
            {
                foreach (var reference in invocation.TargetMethod.DeclaringSyntaxReferences)
                {
                    var syntax = reference.GetSyntax(context.CancellationToken);
                    var model = AnalyzerSemanticModelProvider.GetSemanticModel(
                        context.Compilation,
                        syntax.SyntaxTree);
                    var expression = syntax switch
                    {
                        ArrowExpressionClauseSyntax arrow => arrow.Expression,
                        MethodDeclarationSyntax method when method.ExpressionBody != null =>
                            method.ExpressionBody.Expression,
                        _ => null
                    };
                    if (expression == null)
                    {
                        continue;
                    }

                    var returned = model.GetOperation(
                        expression,
                        context.CancellationToken);
                    if (returned != null)
                    {
                        var element = TryResolveTupleElement(
                            context, returned, index, root, resolving);
                        if (element != null)
                        {
                            return element;
                        }
                    }
                }
            }
            finally
            {
                resolving.Remove(invocation.TargetMethod);
            }
        }

        return null;
    }

    private static void AnalyzeUnknownAssignmentTarget(
        OperationAnalysisContext context,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols,
        IOperation target,
        IOperation root)
    {
        if (IsCacheType(ResolveCacheOwnerType(target, root), symbols) &&
            IsSemanticAnswerType(target.Type))
        {
            Report(context, target.Syntax.GetLocation());
        }
    }

    private static IOperation UnwrapAssignmentOperation(IOperation operation)
    {
        while (operation is IConversionOperation conversion &&
               conversion.OperatorMethod == null ||
               operation is IParenthesizedOperation)
        {
            operation = operation switch
            {
                IConversionOperation { OperatorMethod: null } value => value.Operand,
                IParenthesizedOperation parenthesized => parenthesized.Operand,
                _ => operation
            };
        }

        return operation;
    }

    private static void AnalyzeAssignmentTarget(
        OperationAnalysisContext context,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols,
        IOperation target,
        IOperation value,
        IOperation root)
    {
        var cacheType = ResolveCacheOwnerType(target, root);
        if (IsCacheType(cacheType, symbols) &&
            IsNonCacheableSemanticAnswer(
                value,
                root,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)))
        {
            Report(context, target.Syntax.GetLocation());
        }
    }

    private static void AnalyzeRefArguments(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.RefKind is not (RefKind.Ref or RefKind.Out) ||
                !IsCacheType(
                    ResolveCacheOwnerType(
                        argument.Value,
                        Root(invocation)),
                    symbols) ||
                !ParameterWritesNonCacheableAnswer(
                    invocation.TargetMethod,
                    argument.Parameter,
                    context.CancellationToken))
            {
                continue;
            }

            Report(context, argument.Syntax.GetLocation());
        }
    }

    private static bool ParameterWritesNonCacheableAnswer(
        IMethodSymbol method,
        IParameterSymbol parameter,
        CancellationToken cancellationToken)
    {
        if (!IsSemanticAnswerType(parameter.Type))
        {
            return false;
        }

        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax(cancellationToken);
            if (syntax.DescendantNodesAndSelf()
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment =>
                    assignment.Left is IdentifierNameSyntax identifier &&
                    string.Equals(
                        identifier.Identifier.ValueText,
                        parameter.Name,
                        StringComparison.Ordinal) &&
                    IsNonCacheableSyntax(assignment.Right)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNonCacheableSyntax(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression switch
        {
            MemberAccessExpressionSyntax member =>
                IsNonCacheableName(member.Name.Identifier.ValueText),
            IdentifierNameSyntax identifier =>
                IsNonCacheableName(identifier.Identifier.ValueText),
            InvocationExpressionSyntax invocation =>
                invocation.Expression switch
                {
                    MemberAccessExpressionSyntax member =>
                        IsNonCacheableName(member.Name.Identifier.ValueText),
                    IdentifierNameSyntax identifier =>
                        IsNonCacheableName(identifier.Identifier.ValueText),
                    _ => false
                },
            ObjectCreationExpressionSyntax creation =>
                IsNonCacheableName(creation.Type.ToString()),
            ConditionalExpressionSyntax conditional =>
                IsNonCacheableSyntax(conditional.WhenTrue) ||
                IsNonCacheableSyntax(conditional.WhenFalse),
            CastExpressionSyntax cast =>
                IsNonCacheableSyntax(cast.Expression),
            _ => false
        };
    }

    private static ITypeSymbol? ResolveCacheOwnerType(
        IOperation target,
        IOperation root)
    {
        return ResolveCacheOwnerType(
            target,
            root,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
    }

    private static ITypeSymbol? ResolveCacheOwnerType(
        IOperation target,
        IOperation root,
        HashSet<ILocalSymbol> resolving)
    {
        target = UnwrapAssignmentOperation(target);
        return target switch
        {
            IArrayElementReferenceOperation array =>
                ResolveCacheOwnerType(array.ArrayReference, root, resolving),
            IPropertyReferenceOperation property =>
                property.Instance == null
                    ? property.Property.ContainingType
                    : ResolveCacheOwnerType(property.Instance, root, resolving),
            IFieldReferenceOperation field =>
                field.Instance == null
                    ? field.Field.ContainingType
                    : ResolveCacheOwnerType(field.Instance, root, resolving),
            ILocalReferenceOperation local =>
                ResolveLocalOwnerType(local, root, resolving),
            IParameterReferenceOperation parameter => parameter.Type,
            _ => target.Type
        };
    }

    private static ITypeSymbol? ResolveLocalOwnerType(
        ILocalReferenceOperation reference,
        IOperation root,
        HashSet<ILocalSymbol> resolving)
    {
        if (!resolving.Add(reference.Local))
        {
            return reference.Type;
        }

        var initializer = root.DescendantsAndSelf()
            .OfType<IVariableDeclaratorOperation>()
            .Where(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    candidate.Symbol,
                    reference.Local) &&
                candidate.Initializer != null &&
                candidate.Syntax.SpanStart <= reference.Syntax.SpanStart)
            .OrderBy(candidate => candidate.Syntax.SpanStart)
            .LastOrDefault()?
            .Initializer?
            .Value;
        try
        {
            return initializer == null
                ? reference.Type
                : ResolveCacheOwnerType(initializer, root, resolving);
        }
        finally
        {
            resolving.Remove(reference.Local);
        }
    }

    private static void Report(OperationAnalysisContext context, Location? location)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            MetaDiagnosticDescriptors.NonCacheableSemanticAnswer, location));
    }

    private static bool IsCacheType(
        ITypeSymbol? type,
        SharpProofSoundnessAnalyzer.KnownSymbols symbols)
    {
        return type != null &&
            (IsSameType(
                type,
                symbols[SharpProofSoundnessAnalyzer.KnownType.SemanticCache]) ||
             type.AllInterfaces.Any(interfaceType => IsSameType(
                 interfaceType,
                 symbols[SharpProofSoundnessAnalyzer.KnownType.SemanticCache])));
    }

    private static bool IsSameType(
        ITypeSymbol? actual,
        INamedTypeSymbol? expected)
    {
        return actual != null &&
            expected != null &&
            SymbolEqualityComparer.Default.Equals(
                actual.OriginalDefinition,
                expected.OriginalDefinition);
    }

    private static IOperation Root(IOperation operation)
    {
        while (operation.Parent != null)
        {
            operation = operation.Parent;
        }
        return operation;
    }

    private static bool IsNonCacheableSemanticAnswer(
        IOperation operation, IOperation root, HashSet<ILocalSymbol> resolving)
    {
        while (operation is IConversionOperation conversion &&
               conversion.OperatorMethod == null ||
               operation is IParenthesizedOperation)
        {
            operation = operation switch
            {
                IConversionOperation { OperatorMethod: null } value => value.Operand,
                IParenthesizedOperation parenthesized => parenthesized.Operand,
                _ => operation
            };
        }
        return operation switch
        {
            IFieldReferenceOperation field
                when field.Field.ContainingType.TypeKind == TypeKind.Enum &&
                     IsSemanticAnswerType(field.Type) => IsNonCacheableName(field.Field.Name),
            IObjectCreationOperation creation =>
                IsSemanticAnswerType(creation.Type) && IsNonCacheableName(creation.Type?.Name),
            ILocalReferenceOperation local => ResolveLocal(local, root, resolving),
            IConditionalOperation conditional =>
                IsNonCacheableSemanticAnswer(conditional.WhenTrue, root, resolving) ||
                conditional.WhenFalse != null &&
                IsNonCacheableSemanticAnswer(conditional.WhenFalse, root, resolving),
            IConversionOperation conversion
                when conversion.OperatorMethod == null =>
                IsNonCacheableSemanticAnswer(conversion.Operand, root, resolving),
            IDelegateCreationOperation delegateCreation =>
                IsNonCacheableSemanticAnswer(delegateCreation.Target, root, resolving),
            IMethodReferenceOperation methodReference =>
                ResolveMethod(methodReference.Method),
            IAnonymousFunctionOperation anonymous
                when anonymous.Body is { } anonymousBody =>
                ContainsNonCacheableSemanticAnswer(anonymousBody, root, resolving),
            ILocalFunctionOperation localFunction
                when localFunction.Body is { } localBody =>
                ContainsNonCacheableSemanticAnswer(localBody, root, resolving),
            IPropertyReferenceOperation property => ResolveProperty(property),
            IInvocationOperation invocation => ResolveInvocation(invocation),
            _ => IsSemanticAnswerType(operation.Type) &&
                operation.ConstantValue is not { HasValue: true }
        };
    }

    private static bool ContainsNonCacheableSemanticAnswer(
        IOperation body,
        IOperation root,
        HashSet<ILocalSymbol> resolving)
    {
        foreach (var candidate in body.DescendantsAndSelf())
        {
            if (ReferenceEquals(candidate, body) ||
                candidate is IBlockOperation or IExpressionStatementOperation or
                    IReturnOperation or IAnonymousFunctionOperation or
                    ILocalFunctionOperation)
            {
                continue;
            }

            if (IsNonCacheableSemanticAnswer(candidate, root, resolving))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ResolveLocal(
        ILocalReferenceOperation reference, IOperation root,
        HashSet<ILocalSymbol> resolving)
    {
        if (!resolving.Add(reference.Local))
        {
            return true;
        }
        try
        {
            var enclosingLoop = FindEnclosingLoop(reference, root);
            var writes = root.DescendantsAndSelf()
                .Where(candidate =>
                    !IsInsideNestedCallable(candidate, root) &&
                    (candidate.Syntax.SpanStart < reference.Syntax.SpanStart ||
                     enclosingLoop != null && IsWithin(candidate, enclosingLoop)))
                .Select(candidate => candidate switch
                {
                    IVariableDeclaratorOperation declarator
                        when SymbolEqualityComparer.Default.Equals(
                            declarator.Symbol, reference.Local) => declarator.Initializer?.Value,
                    ISimpleAssignmentOperation { Target: ILocalReferenceOperation local } assignment
                        when SymbolEqualityComparer.Default.Equals(
                            local.Local, reference.Local) => assignment.Value,
                    ICompoundAssignmentOperation { Target: ILocalReferenceOperation local } assignment
                        when SymbolEqualityComparer.Default.Equals(
                            local.Local, reference.Local) => assignment.Value,
                    _ => null
                })
                .Where(static value => value != null)
                .Cast<IOperation>()
                .OrderBy(static value => value.Syntax.SpanStart)
                .ToArray();
            if (writes.Length == 0)
            {
                return IsSemanticAnswerType(reference.Type);
            }
            var last = writes[writes.Length - 1];
            if (last.Parent is ICompoundAssignmentOperation)
            {
                var previous = writes.Length > 1
                    ? IsNonCacheableSemanticAnswer(
                        writes[writes.Length - 2], root, resolving)
                    : IsSemanticAnswerType(reference.Type);
                return previous ||
                    IsNonCacheableSemanticAnswer(last, root, resolving);
            }
            if (IsConditionallyExecuted(last, root) && writes.Length > 1)
            {
                for (var index = writes.Length - 2; index >= 0; index--)
                {
                    if (IsNonCacheableSemanticAnswer(
                            writes[index], root, resolving))
                    {
                        return true;
                    }

                    // Once an unconditional write is reached it dominates
                    // the later conditional chain; older writes cannot reach
                    // the cache without passing through that value.
                    if (!IsConditionallyExecuted(writes[index], root))
                    {
                        break;
                    }
                }

                return IsNonCacheableSemanticAnswer(last, root, resolving);
            }
            return IsNonCacheableSemanticAnswer(last, root, resolving);
        }
        finally
        {
            resolving.Remove(reference.Local);
        }
    }

    private static bool IsConditionallyExecuted(IOperation operation, IOperation root)
    {
        for (var current = operation.Parent; current != null && !ReferenceEquals(current, root);
             current = current.Parent)
        {
            if (current is IConditionalOperation or ISwitchOperation or ILoopOperation)
            {
                return true;
            }
        }
        return false;
    }

    private static ILoopOperation? FindEnclosingLoop(
        IOperation operation, IOperation root)
    {
        for (var current = operation.Parent;
             current != null && !ReferenceEquals(current, root);
             current = current.Parent)
        {
            if (current is ILoopOperation loop)
            {
                return loop;
            }
        }

        return null;
    }

    private static bool IsWithin(IOperation operation, IOperation container)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, container))
            {
                return true;
            }
        }

        return ReferenceEquals(operation, container);
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
        var values = GetReturnedValueNames(property.Property).ToArray();
        return values.Length == 0
            ? IsSemanticAnswerType(property.Type) && IsNonCacheableName(property.Property.Name)
            : values.Any(IsNonCacheableName);
    }

    private static bool ResolveInvocation(IInvocationOperation invocation)
    {
        return ResolveMethod(invocation.TargetMethod, invocation.Type);
    }

    private static bool ResolveMethod(
        IMethodSymbol method,
        ITypeSymbol? returnType = null)
    {
        var values = GetReturnedValueNames(method).ToArray();
        return values.Length == 0
            ? IsSemanticAnswerType(returnType ?? method.ReturnType) &&
                IsNonCacheableName(method.Name)
            : values.Any(IsNonCacheableName);
    }

    private static IEnumerable<string> GetReturnedValueNames(ISymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax();
            foreach (var expression in syntax.DescendantNodesAndSelf()
                         .OfType<ArrowExpressionClauseSyntax>()
                         .Select(static arrow => arrow.Expression)
                         .Concat(syntax.DescendantNodesAndSelf()
                             .OfType<ReturnStatementSyntax>()
                             .Where(static statement => statement.Expression != null)
                             .Select(static statement => statement.Expression!))
                         .Where(expression => !expression.Ancestors()
                             .TakeWhile(ancestor => !ReferenceEquals(ancestor, syntax))
                             .Any(static ancestor => ancestor is
                                 AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
            {
                foreach (var name in GetExpressionNames(expression))
                {
                    yield return name;
                }
            }
        }
    }

    private static IEnumerable<string> GetExpressionNames(ExpressionSyntax expression)
    {
        foreach (var member in expression.DescendantNodesAndSelf()
                     .OfType<MemberAccessExpressionSyntax>())
        {
            yield return member.Name.Identifier.ValueText;
        }

        foreach (var identifier in expression.DescendantNodesAndSelf()
                     .OfType<IdentifierNameSyntax>()
                     .Where(static identifier =>
                         identifier.Parent is not MemberAccessExpressionSyntax member ||
                         !ReferenceEquals(member.Name, identifier)))
        {
            yield return identifier.Identifier.ValueText;
        }

        foreach (var creation in expression.DescendantNodesAndSelf()
                     .OfType<ObjectCreationExpressionSyntax>())
        {
            yield return creation.Type.ToString();
        }
    }

    private static bool IsSemanticAnswerType(ITypeSymbol? type)
    {
        if (type == null || !IsSharpProofNamespace(type.ContainingNamespace))
        {
            return false;
        }
        return type.Name.IndexOf("Answer", StringComparison.Ordinal) >= 0 ||
               type.Name.IndexOf("Result", StringComparison.Ordinal) >= 0 ||
               type.Name.IndexOf("Outcome", StringComparison.Ordinal) >= 0;
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
                string.Equals(name, "Canceled", StringComparison.Ordinal) ||
                string.Equals(name, "Cancelled", StringComparison.Ordinal) ||
                string.Equals(name, "Interrupted", StringComparison.Ordinal) ||
                name.IndexOf("Timeout", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("TimedOut", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Error", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Failure", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Failed", StringComparison.Ordinal) >= 0);
    }
}
