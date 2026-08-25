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

    internal static void AnalyzeWrite(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        if (!WriteMethods.Contains(invocation.TargetMethod.Name) ||
            !IsCacheType(invocation.Instance?.Type ?? invocation.TargetMethod.ContainingType) ||
            !invocation.Arguments.Any(argument => IsNonCacheableSemanticAnswer(
                argument.Value, Root(argument.Value),
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default))))
        {
            return;
        }

        Report(context, invocation.Syntax.GetLocation());
    }

    internal static void AnalyzeAssignment(OperationAnalysisContext context)
    {
        if (context.Operation is not IAssignmentOperation assignment)
        {
            return;
        }
        if (assignment.Target is not IPropertyReferenceOperation property ||
            !IsCacheType(property.Instance?.Type ?? property.Property.ContainingType) ||
            !IsNonCacheableSemanticAnswer(
                assignment.Value, Root(assignment),
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)))
        {
            return;
        }

        Report(context, assignment.Syntax.GetLocation());
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
            IPropertyReferenceOperation property => ResolveProperty(property),
            IInvocationOperation invocation => ResolveInvocation(invocation),
            _ => IsSemanticAnswerType(operation.Type) &&
                operation.ConstantValue is not { HasValue: true }
        };
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
            if (IsConditionallyExecuted(last, root) && writes.Length > 1)
            {
                return IsNonCacheableSemanticAnswer(writes[writes.Length - 2], root, resolving) ||
                       IsNonCacheableSemanticAnswer(last, root, resolving);
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
        var values = GetReturnedValueNames(invocation.TargetMethod).ToArray();
        return values.Length == 0
            ? IsSemanticAnswerType(invocation.Type) && IsNonCacheableName(invocation.TargetMethod.Name)
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
                name.IndexOf("Timeout", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("TimedOut", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Error", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Failure", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Failed", StringComparison.Ordinal) >= 0);
    }
}
