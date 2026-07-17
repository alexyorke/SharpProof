using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer.Engine.Analysis;

internal static class CallGraphBuilder
{

    public static ImmutableDictionary<IMethodSymbol, ImmutableHashSet<IMethodSymbol>> Build(
        Compilation compilation,
        Func<SyntaxTree, SemanticModel> getSemanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var edges = new Dictionary<IMethodSymbol, ImmutableHashSet<IMethodSymbol>>(SymbolEq.Default);
        var allNamedTypes = TypeHierarchyEnumeration
            .EnumerateAllNamedTypes(compilation.Assembly.GlobalNamespace, cancellationToken)
            .ToImmutableArray();
        var dispatchTargetCache =
            new Dictionary<IMethodSymbol, ImmutableArray<IMethodSymbol>>(SymbolEq.Default);

        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = getSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);
            var operations = root.DescendantNodes().Select(n => model.GetOperation(n, cancellationToken))
                .OfType<IMethodBodyOperation>();
            foreach (var body in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var containingMethod =
                    model.GetEnclosingSymbol(body.Syntax.SpanStart, cancellationToken) as IMethodSymbol;
                if (containingMethod == null) continue;
                var callees = new HashSet<IMethodSymbol>(SymbolEq.Default);
                var delegateTargetsBySymbol =
                    new Dictionary<ISymbol, HashSet<IMethodSymbol>>(SymbolEq.Default);
                foreach (var inv in body.Descendants().OfType<IInvocationOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (inv.Syntax != null &&
                        ExecutionVisibility.IsInStaticallyUnreachableBranch(
                            inv.Syntax,
                            model,
                            cancellationToken))
                        continue;

                    if (inv.TargetMethod != null)
                    {
                        var target = inv.TargetMethod.OriginalDefinition;
                        callees.Add(target);

                        // Expand potential dynamic targets for interface/virtual dispatch within the current compilation,
                        // except when the call is explicitly to base, where dispatch is constrained to the immediate base target.
                        var isBaseReference = SymbolicDispatchFacts.IsBaseReference(inv.Instance);
                        if (!isBaseReference)
                            foreach (var impl in GetPotentialTargetsForVirtualOrInterfaceCall(target, allNamedTypes,
                                         dispatchTargetCache, cancellationToken))
                                callees.Add(impl);
                    }
                }

                // Include user-defined operator methods and conversion operators
                foreach (var bin in body.Descendants().OfType<IBinaryOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (bin.OperatorMethod != null) callees.Add(bin.OperatorMethod.OriginalDefinition);
                }

                foreach (var un in body.Descendants().OfType<IUnaryOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (un.OperatorMethod != null) callees.Add(un.OperatorMethod.OriginalDefinition);
                }

                foreach (var compound in body.Descendants().OfType<ICompoundAssignmentOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (compound.OperatorMethod != null) callees.Add(compound.OperatorMethod.OriginalDefinition);
                }

                foreach (var incrementOrDecrement in body.Descendants().OfType<IIncrementOrDecrementOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (incrementOrDecrement.OperatorMethod != null)
                        callees.Add(incrementOrDecrement.OperatorMethod.OriginalDefinition);
                }

                foreach (var conv in body.Descendants().OfType<IConversionOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var method = conv.Conversion.MethodSymbol;
                    if (conv.Conversion.IsUserDefined && method != null) callees.Add(method.OriginalDefinition);
                }

                // Include constructor initializer targets (base()/this())
                foreach (var ctorBody in body.Descendants().OfType<IConstructorBodyOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var init = ctorBody.Initializer;
                    if (init is IInvocationOperation initInv && initInv.TargetMethod != null)
                        callees.Add(initInv.TargetMethod.OriginalDefinition);
                }

                foreach (var methodRef in body.Descendants().OfType<IMethodReferenceOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (methodRef.Method != null) callees.Add(methodRef.Method.OriginalDefinition);
                }

                // Include property accessor methods when properties are referenced
                foreach (var propRef in body.Descendants().OfType<IPropertyReferenceOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var getter = propRef.Property?.GetMethod;
                    if (getter != null) callees.Add(getter.OriginalDefinition);
                }

                foreach (var del in body.Descendants().OfType<IDelegateCreationOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (del.Target is IMethodReferenceOperation target && target.Method != null)
                        callees.Add(target.Method.OriginalDefinition);
                }

                foreach (var anon in body.Descendants().OfType<IAnonymousFunctionOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (anon.Symbol != null) callees.Add(anon.Symbol.OriginalDefinition);
                }

                // Conservatively add edges for awaited invocations
                foreach (var awaitOp in body.Descendants().OfType<IAwaitOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var awaitedInv in awaitOp.Operation.DescendantsAndSelf().OfType<IInvocationOperation>())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (awaitedInv.TargetMethod != null) callees.Add(awaitedInv.TargetMethod.OriginalDefinition);
                    }
                }

                // Capture delegate assignments and initializations to map symbols -> potential target methods
                foreach (var assignment in body.Descendants().OfType<IAssignmentOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var targetSymbol = PurityAnalysisEngine.TryResolveSymbol(assignment.Target);
                    if (targetSymbol == null) continue;
                    // If target is a property, include its setter in call graph
                    if (assignment.Target is IPropertyReferenceOperation propTarget &&
                        propTarget.Property?.SetMethod != null)
                        callees.Add(propTarget.Property.SetMethod.OriginalDefinition);
                    AddDelegateTarget(
                        targetSymbol,
                        assignment.Value,
                        delegateTargetsBySymbol,
                        callees);
                }

                // Capture delegate compound assignments (e.g., "+=") to accumulate targets
                foreach (var compound in body.Descendants().OfType<ICompoundAssignmentOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (compound.Target?.Type?.TypeKind != TypeKind.Delegate) continue;
                    var targetSymbol = PurityAnalysisEngine.TryResolveSymbol(compound.Target);
                    if (targetSymbol == null) continue;
                    AddDelegateTarget(
                        targetSymbol,
                        compound.Value,
                        delegateTargetsBySymbol,
                        callees);
                }

                // For compound property assignments and increment/decrement, include property setters
                foreach (var compoundProp in body.Descendants().OfType<ICompoundAssignmentOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (compoundProp.Target is IPropertyReferenceOperation prop && prop.Property?.SetMethod != null)
                        callees.Add(prop.Property.SetMethod.OriginalDefinition);
                }

                foreach (var incdec in body.Descendants().OfType<IIncrementOrDecrementOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (incdec.Target is IPropertyReferenceOperation prop && prop.Property != null)
                    {
                        if (prop.Property.GetMethod != null)
                            callees.Add(prop.Property.GetMethod.OriginalDefinition);
                        if (prop.Property.SetMethod != null)
                            callees.Add(prop.Property.SetMethod.OriginalDefinition);
                    }
                }

                // Capture event handler subscriptions (+=) mapping event symbols to potential handler targets
                foreach (var evtAssign in body.Descendants().OfType<IEventAssignmentOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var eventSymbol = PurityAnalysisEngine.TryResolveSymbol(evtAssign.EventReference);
                    if (eventSymbol == null) continue;
                    AddDelegateTarget(
                        eventSymbol,
                        evtAssign.HandlerValue,
                        delegateTargetsBySymbol,
                        callees);
                }

                foreach (var group in body.Descendants().OfType<IVariableDeclarationGroupOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var decl in group.Declarations)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        foreach (var d in decl.Declarators)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (d.Initializer?.Value is { } initializer)
                                AddDelegateTarget(
                                    d.Symbol,
                                    initializer,
                                    delegateTargetsBySymbol,
                                    callees);
                        }
                    }
                }

                // For delegate invocations, add edges to mapped potential targets
                foreach (var inv in body.Descendants().OfType<IInvocationOperation>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (inv.TargetMethod?.Name == "Invoke" &&
                        inv.TargetMethod.ContainingType?.TypeKind == TypeKind.Delegate)
                    {
                        var sym = PurityAnalysisEngine.TryResolveSymbol(inv.Instance);
                        if (sym != null && delegateTargetsBySymbol.TryGetValue(sym, out var targets))
                            foreach (var t in targets)
                                callees.Add(t.OriginalDefinition);
                    }
                }

                edges[containingMethod.OriginalDefinition] =
                    ImmutableHashSet.CreateRange<IMethodSymbol>(SymbolEq.Default, callees);
            }
        }

        // Optional CFG-guided pass to conservatively add invocation edges discovered per-block
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = getSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);
            var methods = root.DescendantNodes()
                .Where(static node => node is BaseMethodDeclarationSyntax or
                    AccessorDeclarationSyntax or
                    LocalFunctionStatementSyntax)
                .Select(n => model.GetDeclaredSymbol(n, cancellationToken))
                .OfType<IMethodSymbol>();
            foreach (var method in methods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (method == null) continue;
                var declSyntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
                if (declSyntaxRef == null) continue;
                var bodyNode = declSyntaxRef.GetSyntax(cancellationToken);
                ControlFlowGraph? cfg = null;
                try
                {
                    cfg = ControlFlowGraph.Create(bodyNode, model);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    cfg = null;
                }

                if (cfg == null) continue;
                if (!edges.TryGetValue(method.OriginalDefinition, out var callerSet))
                    callerSet = ImmutableHashSet<IMethodSymbol>.Empty;
                var callerSetBuilder = callerSet.ToBuilder();
                foreach (var block in cfg.Blocks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var op in block.Operations)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (op is IInvocationOperation inv && inv.TargetMethod != null)
                        {
                            var target = inv.TargetMethod.OriginalDefinition;
                            callerSetBuilder.Add(target);

                            var isBaseReference = SymbolicDispatchFacts.IsBaseReference(inv.Instance);
                            if (!isBaseReference)
                                foreach (var impl in GetPotentialTargetsForVirtualOrInterfaceCall(target, allNamedTypes,
                                             dispatchTargetCache, cancellationToken))
                                    callerSetBuilder.Add(impl);
                        }
                    }
                }

                edges[method.OriginalDefinition] = callerSetBuilder.ToImmutable();
            }
        }

        return edges.ToImmutableDictionary(SymbolEq.Default);
    }

    private static void AddDelegateTarget(
        ISymbol targetSymbol,
        IOperation value,
        Dictionary<ISymbol, HashSet<IMethodSymbol>> delegateTargetsBySymbol,
        HashSet<IMethodSymbol> callees)
    {
        value = PurityAnalysisEngine.SkipImplicitConversions(value) ?? value;
        if (value is IDelegateCreationOperation delegateCreation)
            value = PurityAnalysisEngine.SkipImplicitConversions(delegateCreation.Target) ?? delegateCreation.Target;

        var targetMethod = value switch
        {
            IMethodReferenceOperation methodReference => methodReference.Method?.OriginalDefinition,
            IAnonymousFunctionOperation anonymousFunction => anonymousFunction.Symbol?.OriginalDefinition,
            _ => null
        };
        if (targetMethod == null) return;

        callees.Add(targetMethod);
        if (!delegateTargetsBySymbol.TryGetValue(targetSymbol, out var targets))
        {
            targets = new HashSet<IMethodSymbol>(SymbolEq.Default);
            delegateTargetsBySymbol[targetSymbol] = targets;
        }

        targets.Add(targetMethod);
    }

    private static ImmutableArray<IMethodSymbol> GetPotentialTargetsForVirtualOrInterfaceCall(
        IMethodSymbol target,
        ImmutableArray<INamedTypeSymbol> allNamedTypes,
        Dictionary<IMethodSymbol, ImmutableArray<IMethodSymbol>> dispatchTargetCache,
        CancellationToken cancellationToken)
    {
        var targetDefinition = target.OriginalDefinition;
        if (dispatchTargetCache.TryGetValue(targetDefinition, out var cachedTargets)) return cachedTargets;

        var targets =
            ResolvePotentialTargetsForVirtualOrInterfaceCall(targetDefinition, allNamedTypes, cancellationToken)
                .ToImmutableArray();
        dispatchTargetCache[targetDefinition] = targets;
        return targets;
    }

    private static IEnumerable<IMethodSymbol> ResolvePotentialTargetsForVirtualOrInterfaceCall(
        IMethodSymbol target,
        ImmutableArray<INamedTypeSymbol> allNamedTypes,
        CancellationToken cancellationToken)
    {
        // Interface dispatch: include implementations in types that implement the interface
        if (target.ContainingType?.TypeKind == TypeKind.Interface)
        {
            foreach (var type in allNamedTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TypeHierarchyEnumeration.ImplementsInterface(type, target.ContainingType)) continue;
                var impl = type.FindImplementationForInterfaceMember(target) as IMethodSymbol;
                if (impl != null) yield return impl.OriginalDefinition;
            }

            if (!target.IsAbstract || TypeHierarchyEnumeration.HasMethodBody(target, cancellationToken))
                yield return target;
            yield break;
        }

        // Virtual/abstract dispatch: include overrides declared in derived types within this compilation
        if (target.IsVirtual || target.IsAbstract || target.IsOverride)
        {
            var baseType = target.ContainingType;
            if (baseType != null)
                foreach (var type in allNamedTypes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TypeHierarchyEnumeration.DerivesFrom(type, baseType)) continue;
                    foreach (var member in type.GetMembers())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (member is IMethodSymbol m &&
                            TypeHierarchyEnumeration.OverridesTargetMethod(m, target))
                            yield return m.OriginalDefinition;
                    }
                }
        }
    }

}
