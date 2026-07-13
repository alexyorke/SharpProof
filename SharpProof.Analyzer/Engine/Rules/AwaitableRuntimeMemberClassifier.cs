using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Engine.Analysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal static class AwaitableRuntimeMemberClassifier
{
    internal static bool IsKnownPureAwaitInfrastructureMethod(IMethodSymbol method)
    {
        method = method.OriginalDefinition;
        if (method.IsStatic) return false;

        if (method.Name == "GetAwaiter" &&
            method.Parameters.Length == 0 &&
            IsKnownAwaitableType(method.ContainingType))
            return true;

        if (!IsKnownAwaiterType(method.ContainingType)) return false;

        if (method.Name is "get_IsCompleted" or "GetResult") return method.Parameters.Length == 0;

        return IsContinuationSchedulingMethod(method);
    }

    internal static bool IsContinuationSchedulingMethod(IMethodSymbol method)
    {
        return method.Name is "OnCompleted" or "UnsafeOnCompleted" &&
               method.Parameters.Length == 1 &&
               IsSystemAction(method.Parameters[0].Type);
    }

    private static bool IsKnownAwaitableType(INamedTypeSymbol? type)
    {
        if (type == null) return false;

        if (TypeHierarchyEnumeration.IsNamespace(type.ContainingNamespace, "System.Threading.Tasks") &&
            type.MetadataName is "Task" or "Task`1" or "ValueTask" or "ValueTask`1")
            return true;

        return TypeHierarchyEnumeration.IsNamespace(type.ContainingNamespace, "System.Runtime.CompilerServices") &&
               type.MetadataName is
                   "ConfiguredTaskAwaitable" or
                   "ConfiguredTaskAwaitable`1" or
                   "ConfiguredValueTaskAwaitable" or
                   "ConfiguredValueTaskAwaitable`1" or
                   "YieldAwaitable";
    }

    private static bool IsKnownAwaiterType(INamedTypeSymbol? type)
    {
        if (type == null ||
            !TypeHierarchyEnumeration.IsNamespace(type.ContainingNamespace, "System.Runtime.CompilerServices"))
            return false;

        if (type.MetadataName is "TaskAwaiter" or "TaskAwaiter`1" or "ValueTaskAwaiter" or "ValueTaskAwaiter`1")
            return true;

        if (type.MetadataName is not ("ConfiguredTaskAwaiter" or "ConfiguredValueTaskAwaiter" or "YieldAwaiter"))
            return false;

        return type.ContainingType?.MetadataName is
            "ConfiguredTaskAwaitable" or
            "ConfiguredTaskAwaitable`1" or
            "ConfiguredValueTaskAwaitable" or
            "ConfiguredValueTaskAwaitable`1" or
            "YieldAwaitable";
    }

    private static bool IsSystemAction(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
               namedType.Arity == 0 &&
               string.Equals(namedType.MetadataName, "Action", StringComparison.Ordinal) &&
               TypeHierarchyEnumeration.IsNamespace(namedType.ContainingNamespace, "System");
    }
}
