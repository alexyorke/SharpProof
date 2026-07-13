using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Engine.Analysis;
using static SharpProof.Analyzer.Engine.Rules.ComparerInvocationPurity;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class MethodInvocationPurityRule
{
    internal static IEnumerable<IMethodSymbol> ResolvePotentialDispatchTargets(
        IMethodSymbol invokedMethodSymbol,
        SemanticModel semanticModel,
        INamedTypeSymbol? knownReceiverType,
        IOperation? invocationInstance,
        bool hasExactReceiverType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var compilation = semanticModel.Compilation;
        var target = invokedMethodSymbol.OriginalDefinition;
        var interfaceImplementationTarget = invokedMethodSymbol.ContainingType?.TypeKind == TypeKind.Interface
            ? invokedMethodSymbol
            : target;
        var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        if (target.ContainingType?.TypeKind == TypeKind.Interface)
        {
            if (knownReceiverType != null &&
                TypeHierarchyEnumeration.ImplementsInterface(knownReceiverType, target.ContainingType, true))
            {
                if (hasExactReceiverType ||
                    IsAllocationOnlyInterfaceReceiver(invocationInstance) ||
                    knownReceiverType.TypeKind == TypeKind.Struct ||
                    (knownReceiverType.TypeKind == TypeKind.Class && knownReceiverType.IsSealed))
                {
                    var implementation = ResolveKnownInterfaceImplementation(knownReceiverType,
                        interfaceImplementationTarget, cancellationToken);
                    if (implementation != null)
                        targets.Add(implementation.OriginalDefinition);
                    else if (!target.IsAbstract || TypeHierarchyEnumeration.HasMethodBody(target, cancellationToken))
                        targets.Add(target.OriginalDefinition);

                    return targets;
                }

                var requiresInterfaceReceiverConstraint = knownReceiverType.TypeKind == TypeKind.Interface;

                foreach (var type in TypeHierarchyEnumeration.EnumerateAllNamedTypes(compilation.Assembly
                             .GlobalNamespace))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (requiresInterfaceReceiverConstraint)
                    {
                        if (!TypeHierarchyEnumeration.ImplementsInterface(type, knownReceiverType, true)) continue;
                    }
                    else
                    {
                        if (!SymbolEqualityComparer.Default.Equals(type.OriginalDefinition,
                                knownReceiverType.OriginalDefinition) &&
                            !TypeHierarchyEnumeration.DerivesFrom(type, knownReceiverType))
                            continue;
                    }

                    AddKnownInterfaceImplementation(type, target, targets, cancellationToken);
                }

                if (!target.IsAbstract || TypeHierarchyEnumeration.HasMethodBody(target, cancellationToken))
                    targets.Add(target);

                return targets;
            }

            foreach (var type in TypeHierarchyEnumeration.EnumerateAllNamedTypes(compilation.Assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddKnownInterfaceImplementation(type, target, targets, cancellationToken);
            }

            if (!target.IsAbstract || TypeHierarchyEnumeration.HasMethodBody(target, cancellationToken))
                targets.Add(target);

            return targets;
        }

        if (target.IsVirtual || target.IsAbstract || target.IsOverride)
        {
            var baseType = target.ContainingType;
            if (baseType != null)
            {
                if (hasExactReceiverType &&
                    knownReceiverType != null &&
                    (SymbolEqualityComparer.Default.Equals(knownReceiverType.OriginalDefinition,
                         baseType.OriginalDefinition) ||
                     TypeHierarchyEnumeration.DerivesFrom(knownReceiverType, baseType)))
                {
                    var exactReceiverTarget = ResolveDispatchTargetForSealedReceiver(target, knownReceiverType);
                    if (exactReceiverTarget != null) targets.Add(exactReceiverTarget.OriginalDefinition);

                    return targets;
                }

                if (knownReceiverType != null &&
                    knownReceiverType.IsSealed &&
                    (SymbolEqualityComparer.Default.Equals(knownReceiverType.OriginalDefinition,
                         baseType.OriginalDefinition) ||
                     TypeHierarchyEnumeration.DerivesFrom(knownReceiverType, baseType)))
                {
                    var sealedReceiverTarget = ResolveDispatchTargetForSealedReceiver(target, knownReceiverType);
                    if (sealedReceiverTarget != null) targets.Add(sealedReceiverTarget.OriginalDefinition);

                    return targets;
                }

                foreach (var type in TypeHierarchyEnumeration.EnumerateAllNamedTypes(compilation.Assembly
                             .GlobalNamespace))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TypeHierarchyEnumeration.DerivesFrom(type, baseType)) continue;

                    foreach (var member in type.GetMembers())
                        if (member is IMethodSymbol method &&
                            TypeHierarchyEnumeration.OverridesTargetMethod(method, target))
                            targets.Add(method.OriginalDefinition);
                }
            }

            if (!target.IsAbstract) targets.Add(target);

            return targets;
        }

        targets.Add(target);
        return targets;
    }

    internal static IMethodSymbol? ResolveKnownInterfaceImplementation(
        INamedTypeSymbol receiverType,
        IMethodSymbol interfaceMethod,
        CancellationToken cancellationToken)
    {
        var implementation = receiverType.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol;
        if (implementation != null) return implementation;

        if (receiverType.TypeKind != TypeKind.Interface) return null;

        foreach (var member in receiverType.GetMembers(interfaceMethod.Name))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is IMethodSymbol candidate &&
                TypeHierarchyEnumeration.HasMethodBody(candidate, cancellationToken) &&
                HasMatchingSignature(candidate, interfaceMethod))
                return candidate;
        }

        return null;
    }

    private static bool HasMatchingSignature(IMethodSymbol candidate, IMethodSymbol interfaceMethod)
    {
        if (candidate.Parameters.Length != interfaceMethod.Parameters.Length ||
            !SymbolEqualityComparer.Default.Equals(candidate.ReturnType, interfaceMethod.ReturnType))
            return false;

        for (var i = 0; i < candidate.Parameters.Length; i++)
        {
            var candidateParameter = candidate.Parameters[i];
            var interfaceParameter = interfaceMethod.Parameters[i];
            if (candidateParameter.RefKind != interfaceParameter.RefKind ||
                !SymbolEqualityComparer.Default.Equals(candidateParameter.Type, interfaceParameter.Type))
                return false;
        }

        return true;
    }

    private static IMethodSymbol? ResolveDispatchTargetForSealedReceiver(IMethodSymbol targetMethod,
        INamedTypeSymbol sealedReceiverType)
    {
        for (var type = sealedReceiverType; type != null; type = type.BaseType)
            foreach (var member in type.GetMembers())
                if (member is IMethodSymbol method &&
                    (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition,
                         targetMethod.OriginalDefinition) ||
                     TypeHierarchyEnumeration.OverridesTargetMethod(method, targetMethod) ||
                     TypeHierarchyEnumeration.ExplicitlyImplements(method, targetMethod)))
                    return method;

        if (!targetMethod.IsAbstract) return targetMethod;

        return null;
    }
}
