using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer;

internal static partial class CommonBugAnalyzer
{
    private static readonly ImmutableHashSet<string> MutatingCollectionMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Add",
            "AddRange",
            "Clear",
            "Dequeue",
            "Enqueue",
            "Insert",
            "InsertRange",
            "Pop",
            "Push",
            "Remove",
            "RemoveAll",
            "RemoveAt",
            "RemoveRange",
            "Reverse",
            "SetItem",
            "Sort",
            "TryAdd",
            "TryDequeue",
            "TryPop",
            "TryRemove",
            "TryTake");

    private static readonly ImmutableHashSet<string> EscapingClosureMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Add",
            "ContinueWith",
            "For",
            "ForEach",
            "Invoke",
            "OrderBy",
            "QueueUserWorkItem",
            "Register",
            "Run",
            "Select",
            "SelectMany",
            "Start",
            "StartNew",
            "Subscribe",
            "ThenBy",
            "UnsafeQueueUserWorkItem",
            "Where");

    private static void AnalyzeCollectionAndConcurrencyCorrectness(
        MethodBodyAnalysisContext context,
        AnalyzerSession session)
    {
        AnalyzeCollectionMutationDuringEnumeration(context, session);
        AnalyzeCapturedForLoopVariables(context, session);

        foreach (var operation in context.Snapshot.VisibleOperations)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            switch (operation)
            {
                case IObjectCreationOperation creation when IsHttpClient(creation.Type):
                    if (FindContainingLoop(creation) is { } clientLoop)
                        Report(
                            context,
                            session,
                            SharpProofDiagnostics.HttpClientInLoopRule,
                            creation.Syntax.GetLocation(),
                            "http_client_in_loop",
                            clientLoop.Syntax.Kind().ToString());
                    break;
                case IInvocationOperation invocation:
                    AnalyzeConcurrentCollectionEnumeration(context, session, invocation);
                    break;
                case IConversionOperation conversion when IsBoxingConversion(conversion):
                    if (FindContainingLoop(conversion) is { } boxingLoop)
                        Report(
                            context,
                            session,
                            SharpProofDiagnostics.BoxingInLoopRule,
                            conversion.Syntax.GetLocation(),
                            "boxing_in_loop",
                            conversion.Operand.Type?.ToDisplayString() ?? "value",
                            boxingLoop.Syntax.Kind().ToString());
                    break;
            }
        }

        AnalyzeParallelCallbacks(context, session);
    }

    private static void AnalyzeCollectionMutationDuringEnumeration(
        MethodBodyAnalysisContext context,
        AnalyzerSession session)
    {
        foreach (var loop in context.Snapshot.VisibleOperations.OfType<IForEachLoopOperation>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var collection = Unwrap(loop.Collection);
            var collectionSymbol = GetReferencedSymbol(collection);
            if (collectionSymbol == null || !IsKnownOrdinaryMutableCollection(collection?.Type)) continue;

            foreach (var operation in loop.Body.DescendantsAndSelf())
            {
                if (IsInsideNestedCallable(operation, loop.Body)) continue;
                if (!TryGetCollectionMutation(operation, out var receiver, out var mutationName) ||
                    !SymbolEq.AreEqual(collectionSymbol, GetReferencedSymbol(receiver)))
                    continue;

                Report(
                    context,
                    session,
                    SharpProofDiagnostics.CollectionMutationDuringEnumerationRule,
                    operation.Syntax.GetLocation(),
                    "collection_mutation_during_enumeration",
                    collectionSymbol.Name,
                    mutationName);
            }
        }
    }

    private static bool TryGetCollectionMutation(
        IOperation operation,
        out IOperation? receiver,
        out string mutationName)
    {
        receiver = null;
        mutationName = string.Empty;
        switch (operation)
        {
            case IInvocationOperation invocation
                when MutatingCollectionMethods.Contains(invocation.TargetMethod.Name):
                receiver = invocation.Instance;
                mutationName = invocation.TargetMethod.Name;
                return receiver != null;
            case ISimpleAssignmentOperation assignment:
                receiver = GetIndexedReceiver(assignment.Target);
                mutationName = "index assignment";
                return receiver != null;
            case ICompoundAssignmentOperation assignment:
                receiver = GetIndexedReceiver(assignment.Target);
                mutationName = "compound index assignment";
                return receiver != null;
        }

        return false;
    }

    private static IOperation? GetIndexedReceiver(IOperation operation)
    {
        operation = Unwrap(operation)!;
        return operation switch
        {
            IPropertyReferenceOperation { Property.IsIndexer: true } property => property.Instance,
            _ => null
        };
    }

    private static void AnalyzeCapturedForLoopVariables(
        MethodBodyAnalysisContext context,
        AnalyzerSession session)
    {
        foreach (var forStatement in context.Node.DescendantNodes()
                     .OfType<ForStatementSyntax>()
                     .Where(statement => !IsInsideNestedSyntaxCallable(statement, context.Node)))
        {
            var variables = forStatement.Declaration?.Variables
                .Select(variable => context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken))
                .OfType<ILocalSymbol>()
                .ToArray();
            if (variables is not { Length: > 0 }) continue;

            foreach (var lambda in forStatement.Statement.DescendantNodesAndSelf()
                         .OfType<AnonymousFunctionExpressionSyntax>())
            {
                if (!CanEscapeIteration(lambda, context)) continue;

                foreach (var identifier in lambda.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    var referenced = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;
                    var captured = variables.FirstOrDefault(variable =>
                        SymbolEq.AreEqual(variable, referenced));
                    if (captured == null) continue;

                    Report(
                        context,
                        session,
                        SharpProofDiagnostics.CapturedLoopVariableRule,
                        identifier.GetLocation(),
                        "captured_for_loop_variable",
                        captured.Name);
                    break;
                }
            }
        }
    }

    private static bool CanEscapeIteration(
        AnonymousFunctionExpressionSyntax lambda,
        MethodBodyAnalysisContext context)
    {
        SyntaxNode current = lambda;
        while (current.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
            current = current.Parent;

        if (current.Parent is InvocationExpressionSyntax immediate && immediate.Expression == current) return false;
        if (current.Parent is ArgumentSyntax argument &&
            argument.Parent?.Parent is InvocationExpressionSyntax invocation &&
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)
            return EscapingClosureMethods.Contains(method.Name);

        if (current.Parent is AssignmentExpressionSyntax assignment)
        {
            var target = context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol;
            return target is IFieldSymbol or IPropertySymbol;
        }

        return current.Parent is ReturnStatementSyntax or ArrowExpressionClauseSyntax;
    }

    private static void AnalyzeParallelCallbacks(
        MethodBodyAnalysisContext context,
        AnalyzerSession session)
    {
        var root = context.Snapshot.RootOperation;
        if (root == null) return;

        foreach (var lambda in root.DescendantsAndSelf().OfType<IAnonymousFunctionOperation>())
        {
            if (!TryGetParallelScheduler(lambda, out var scheduler)) continue;

            foreach (var mutation in lambda.Body.DescendantsAndSelf())
            {
                if (!TryGetMutationTarget(mutation, out var target) ||
                    IsProtectedByLock(mutation, lambda) ||
                    !TryGetSharedStateName(target, lambda, out var stateName))
                    continue;

                Report(
                    context,
                    session,
                    SharpProofDiagnostics.UnsynchronizedSharedMutationRule,
                    mutation.Syntax.GetLocation(),
                    "unsynchronized_shared_mutation",
                    stateName,
                    scheduler);
            }
        }
    }

    private static bool TryGetParallelScheduler(
        IAnonymousFunctionOperation lambda,
        out string scheduler)
    {
        scheduler = string.Empty;
        for (IOperation? current = lambda.Parent; current != null; current = current.Parent)
            switch (current)
            {
                case IInvocationOperation invocation:
                {
                    var typeName = invocation.TargetMethod.ContainingType.ToDisplayString();
                    if ((typeName == "System.Threading.Tasks.Task" && invocation.TargetMethod.Name == "Run") ||
                        (typeName == "System.Threading.Tasks.TaskFactory" && invocation.TargetMethod.Name == "StartNew") ||
                        (typeName == "System.Threading.Tasks.Parallel" &&
                         invocation.TargetMethod.Name is "For" or "ForEach" or "Invoke") ||
                        (typeName == "System.Threading.ThreadPool" &&
                         invocation.TargetMethod.Name is "QueueUserWorkItem" or "UnsafeQueueUserWorkItem"))
                    {
                        scheduler = typeName + "." + invocation.TargetMethod.Name;
                        return true;
                    }

                    return false;
                }
                case IObjectCreationOperation creation:
                {
                    var typeName = creation.Type?.ToDisplayString();
                    if (typeName is "System.Threading.Thread" or "System.Threading.Timer" or
                        "System.Timers.Timer")
                    {
                        scheduler = typeName;
                        return true;
                    }

                    return false;
                }
                case IAnonymousFunctionOperation:
                    return false;
            }

        return false;
    }

    private static bool TryGetMutationTarget(IOperation operation, out IOperation target)
    {
        switch (operation)
        {
            case ISimpleAssignmentOperation assignment:
                target = assignment.Target;
                return true;
            case ICompoundAssignmentOperation assignment:
                target = assignment.Target;
                return true;
            case IIncrementOrDecrementOperation increment:
                target = increment.Target;
                return true;
            case IInvocationOperation invocation
                when MutatingCollectionMethods.Contains(invocation.TargetMethod.Name) &&
                     invocation.Instance != null:
                target = invocation.Instance;
                return true;
            default:
                target = null!;
                return false;
        }
    }

    private static bool TryGetSharedStateName(
        IOperation target,
        IAnonymousFunctionOperation lambda,
        out string stateName)
    {
        target = Unwrap(target)!;
        switch (target)
        {
            case IFieldReferenceOperation field:
                stateName = field.Field.Name;
                return true;
            case IPropertyReferenceOperation property:
                stateName = property.Property.Name;
                return true;
            case ILocalReferenceOperation local:
            {
                var declaration = local.Local.Locations.FirstOrDefault(static location => location.IsInSource);
                if (declaration != null && !lambda.Syntax.Span.Contains(declaration.SourceSpan))
                {
                    stateName = local.Local.Name;
                    return true;
                }

                break;
            }
        }

        stateName = string.Empty;
        return false;
    }

    private static bool IsProtectedByLock(IOperation operation, IAnonymousFunctionOperation lambda)
    {
        for (var current = operation.Parent; current != null && current != lambda; current = current.Parent)
            if (current is ILockOperation)
                return true;

        return false;
    }

    private static void AnalyzeConcurrentCollectionEnumeration(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        IInvocationOperation invocation)
    {
        if (!string.Equals(invocation.TargetMethod.ContainingType.ToDisplayString(), "System.Linq.Enumerable",
                StringComparison.Ordinal))
            return;

        var source = invocation.Instance ?? invocation.Arguments.FirstOrDefault()?.Value;
        source = Unwrap(source);
        if (!IsConcurrentCollectionType(source?.Type)) return;

        Report(
            context,
            session,
            SharpProofDiagnostics.ConcurrentCollectionEnumerationRule,
            invocation.Syntax.GetLocation(),
            "concurrent_collection_enumeration",
            invocation.TargetMethod.Name,
            source!.Syntax.ToString());
    }

    private static bool IsKnownOrdinaryMutableCollection(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol namedType) return false;
        var namespaceName = namedType.ContainingNamespace?.ToDisplayString();
        return namespaceName switch
        {
            "System.Collections.Generic" => namedType.Name is
                "Dictionary" or "HashSet" or "LinkedList" or "List" or "Queue" or
                "SortedDictionary" or "SortedList" or "SortedSet" or "Stack",
            "System.Collections" => namedType.Name is "ArrayList" or "Hashtable" or "Queue" or "Stack",
            "System.Collections.ObjectModel" => namedType.Name is "Collection" or "ObservableCollection",
            _ => false
        };
    }

    private static bool IsConcurrentCollectionType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol namedType &&
               string.Equals(
                   namedType.ContainingNamespace?.ToDisplayString(),
                   "System.Collections.Concurrent",
                   StringComparison.Ordinal);
    }

    private static bool IsHttpClient(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol namedType &&
               string.Equals(namedType.Name, "HttpClient", StringComparison.Ordinal) &&
               string.Equals(
                   namedType.ContainingNamespace?.ToDisplayString(),
                   "System.Net.Http",
                   StringComparison.Ordinal);
    }

    private static bool IsBoxingConversion(IConversionOperation conversion)
    {
        var sourceType = conversion.Operand.Type;
        var targetType = conversion.Type;
        return sourceType?.IsValueType == true &&
               targetType != null &&
               (targetType.SpecialType == SpecialType.System_Object || targetType.TypeKind == TypeKind.Interface);
    }

    private static ILoopOperation? FindContainingLoop(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation) return null;
            if (current is ILoopOperation loop) return loop;
        }

        return null;
    }

    private static ISymbol? GetReferencedSymbol(IOperation? operation)
    {
        operation = Unwrap(operation);
        return operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            IFieldReferenceOperation field => field.Field,
            IPropertyReferenceOperation property => property.Property,
            _ => null
        };
    }

    private static bool IsInsideNestedCallable(IOperation operation, IOperation root)
    {
        for (var current = operation.Parent; current != null && current != root; current = current.Parent)
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return true;

        return false;
    }

    private static bool IsInsideNestedSyntaxCallable(SyntaxNode node, SyntaxNode root)
    {
        for (var current = node.Parent; current != null && current != root; current = current.Parent)
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
                return true;

        return false;
    }
}
