using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine;

[Flags]
internal enum PurityAssignmentTargetEffects
{
    None = 0,
    AssignValue = 1,
    AdvanceVersion = 2,
    ClearConcreteType = 4,
    CallerVisibleMutation = 8,
    DeclaredBorrow = 16
}

internal enum PurityDelegateUpdateKind
{
    None, Assign, AssignIfResolved, Add, UnresolvedTarget, UnresolvedWrittenLocals
}

internal sealed record PurityAssignmentTarget(
    IOperation Operation, ISymbol? Symbol, ImmutableArray<ILocalSymbol> WrittenLocals,
    IOperation? Value, SyntaxNode DefinitionSyntax, SyntaxNode MutationSyntax,
    PurityAssignmentTargetEffects Effects, PurityDelegateUpdateKind DelegateUpdate,
    bool UseCurrentValueState);

internal sealed record PurityAssignmentEnvelope(
    PurityAnalysisState SourceState, ImmutableArray<PurityAssignmentTarget> Targets)
{
    internal static bool TryCreate(
        IOperation operation, PurityAnalysisState state, PurityAnalysisContext context,
        out PurityAssignmentEnvelope envelope)
    {
        var targets = ImmutableArray.CreateBuilder<PurityAssignmentTarget>();
        void Add(
            IOperation target, ISymbol? symbol, IOperation? value,
            PurityAssignmentTargetEffects effects, PurityDelegateUpdateKind delegates = PurityDelegateUpdateKind.None,
            SyntaxNode? definition = null, SyntaxNode? mutation = null,
            bool expandClosure = true, bool currentValueState = false) =>
            targets.Add(CreateTarget(
                target, symbol, value, definition ?? operation.Syntax, mutation ?? operation.Syntax,
                effects, delegates, context, expandClosure, currentValueState));

        void AddValue(IOperation target, ISymbol? symbol, IOperation value, SyntaxNode? definition = null) =>
            Add(target, symbol, value,
                PurityAssignmentTargetEffects.AssignValue |
                PurityAssignmentTargetEffects.AdvanceVersion |
                PurityAssignmentTargetEffects.CallerVisibleMutation,
                symbol != null && target.Type?.TypeKind == TypeKind.Delegate
                    ? PurityDelegateUpdateKind.Assign : PurityDelegateUpdateKind.None,
                definition);

        switch (operation)
        {
            case ICompoundAssignmentOperation compound:
            {
                var symbol = TryResolveTrackedSymbol(compound.Target, state);
                var delegates = symbol != null && compound.Target.Type?.TypeKind == TypeKind.Delegate
                    ? compound.OperatorKind == BinaryOperatorKind.Add
                        ? PurityDelegateUpdateKind.Add : PurityDelegateUpdateKind.UnresolvedTarget
                    : PurityDelegateUpdateKind.None;
                Add(compound.Target, symbol, compound.Value,
                    PurityAssignmentTargetEffects.AdvanceVersion |
                    PurityAssignmentTargetEffects.CallerVisibleMutation, delegates);
                break;
            }
            case ICoalesceAssignmentOperation coalesce:
            {
                var symbol = TryResolveTrackedSymbol(coalesce.Target, state);
                if (symbol is IParameterSymbol)
                    Add(coalesce.Target, symbol, coalesce.Value, PurityAssignmentTargetEffects.AdvanceVersion);
                else if (symbol is ILocalSymbol local && state.IsDefinitelyNullLocalSymbol(local))
                    AddValue(coalesce.Target, symbol, coalesce.Value);
                break;
            }
            case IDeconstructionAssignmentOperation deconstruction:
                if (SymbolicDeconstructionPlan.TryPair(
                        deconstruction.Target, deconstruction.Value,
                        target => ResolveDeconstructionTarget(target, state, context), out var assignments))
                    foreach (var assignment in assignments)
                        if (!assignment.Target.IsDiscard)
                            AddValue(
                                assignment.Target.Operation, assignment.Target.Symbol, assignment.Value,
                                assignment.Target.Operation.Syntax);
                break;
            case IAssignmentOperation assignment:
                AddValue(assignment.Target, TryResolveTrackedSymbol(assignment.Target, state), assignment.Value);
                break;
            case IIncrementOrDecrementOperation increment:
                Add(increment.Target, TryResolveTrackedSymbol(increment.Target, state), null,
                    PurityAssignmentTargetEffects.AdvanceVersion |
                    PurityAssignmentTargetEffects.ClearConcreteType |
                    PurityAssignmentTargetEffects.CallerVisibleMutation);
                break;
            case IVariableDeclaratorOperation { Initializer.Value: { } initializer } declarator:
                Add(declarator, declarator.Symbol, initializer, PurityAssignmentTargetEffects.DeclaredBorrow,
                    expandClosure: false);
                break;
            case IVariableDeclarationGroupOperation group:
                foreach (var declaration in group.Declarations)
                foreach (var declarator in declaration.Declarators)
                    if (declarator.Initializer?.Value is { } value)
                        Add(declarator, declarator.Symbol, value,
                            PurityAssignmentTargetEffects.AssignValue |
                            PurityAssignmentTargetEffects.DeclaredBorrow,
                            declarator.Symbol.Type?.TypeKind == TypeKind.Delegate
                                ? PurityDelegateUpdateKind.AssignIfResolved : PurityDelegateUpdateKind.None,
                            expandClosure: false, currentValueState: true);
                break;
            case IInvocationOperation invocation:
            {
                var arguments = invocation.Arguments
                    .Where(static argument => argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out).ToArray();
                if (arguments.Length == 0) return Fail(out envelope);
                foreach (var argument in arguments)
                    Add(argument,
                        TryResolveTrackedSymbol(SkipImplicitConversions(argument.Value), state), null,
                        PurityAssignmentTargetEffects.AdvanceVersion |
                        PurityAssignmentTargetEffects.ClearConcreteType,
                        PurityDelegateUpdateKind.UnresolvedWrittenLocals);
                break;
            }
            default:
                return Fail(out envelope);
        }

        envelope = new(state, targets.ToImmutable());
        return true;
    }

    private static bool Fail(out PurityAssignmentEnvelope envelope)
    {
        envelope = null!;
        return false;
    }

    private static PurityAssignmentTarget CreateTarget(
        IOperation operation, ISymbol? symbol, IOperation? value,
        SyntaxNode definition, SyntaxNode mutation, PurityAssignmentTargetEffects effects,
        PurityDelegateUpdateKind delegates, PurityAnalysisContext context,
        bool expandClosure, bool currentValueState)
    {
        var locals = symbol is not ILocalSymbol local
            ? ImmutableArray<ILocalSymbol>.Empty
            : expandClosure ? CollectWrittenLocals(local, context) : ImmutableArray.Create(local);
        return new(operation, symbol, locals, value, definition, mutation, effects, delegates, currentValueState);
    }

    private static ImmutableArray<ILocalSymbol> CollectWrittenLocals(
        ILocalSymbol local, PurityAnalysisContext context)
    {
        var result = ImmutableArray.CreateBuilder<ILocalSymbol>();
        AddWrittenLocals(local, context, result, new HashSet<ISymbol>(SymbolEq.Default));
        return result.ToImmutable();
    }

    private static void AddWrittenLocals(
        ILocalSymbol local, PurityAnalysisContext context,
        ImmutableArray<ILocalSymbol>.Builder result, HashSet<ISymbol> visited)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (!visited.Add(local)) return;
        result.Add(local);
        foreach (var initializer in RuleAnalysisHelper.EnumerateRefLocalInitializerOperations(
                     local, context.SemanticModel, context.CancellationToken))
            if (TryResolveSymbol(initializer) is ILocalSymbol target)
                AddWrittenLocals(target, context, result, visited);
    }

    private static ISymbol? ResolveDeconstructionTarget(
        IOperation operation, PurityAnalysisState state, PurityAnalysisContext context)
    {
        operation = SkipImplicitConversions(operation) ?? operation;
        if (TryResolveTrackedSymbol(operation, state) is { } tracked) return tracked;
        if (operation is IDeclarationExpressionOperation declaration)
        {
            if (TryResolveTrackedSymbol(declaration.Expression, state) is { } declared) return declared;
            if (declaration.Syntax is DeclarationExpressionSyntax
                { Designation: SingleVariableDesignationSyntax designation })
                return context.SemanticModel.GetDeclaredSymbol(designation, context.CancellationToken);
        }
        if (operation.Syntax is SingleVariableDesignationSyntax single)
            return context.SemanticModel.GetDeclaredSymbol(single, context.CancellationToken);
        return operation.Syntax is IdentifierNameSyntax identifier
            ? context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol : null;
    }

}

internal static class PurityAssignmentTransition
{
    internal static PurityAnalysisState Apply(
        PurityAssignmentEnvelope envelope, PurityAnalysisState state, PurityAnalysisContext context)
    {
        foreach (var target in envelope.Targets) state = ApplyTarget(envelope, target, state, context);
        return state;
    }

    private static PurityAnalysisState ApplyTarget(
        PurityAssignmentEnvelope envelope, PurityAssignmentTarget target,
        PurityAnalysisState state, PurityAnalysisContext context)
    {
        var valueState = target.UseCurrentValueState ? state : envelope.SourceState;
        if (Has(target, PurityAssignmentTargetEffects.AssignValue) && target.Value != null)
            state = ApplyValue(target, state, valueState, context);
        else if (Has(target, PurityAssignmentTargetEffects.AdvanceVersion))
            state = ApplyVersionEffects(target, state);

        if (Has(target, PurityAssignmentTargetEffects.CallerVisibleMutation))
            state = PurityResourceStateFacts.AddCallerVisibleMutationFact(
                state, target.Operation, envelope.SourceState, target.MutationSyntax);
        var sidecarState = target.UseCurrentValueState ? state : envelope.SourceState;
        state = ApplyDelegateUpdate(target, state, sidecarState, context.CancellationToken);
        if (Has(target, PurityAssignmentTargetEffects.DeclaredBorrow) &&
            target is { Value: { } value, Symbol: ILocalSymbol local })
            state = PurityOperationTransfer.ApplyDeclaredBorrow(
                state, local, value, context.SemanticModel, context.CancellationToken);
        return state;
    }

    private static bool Has(PurityAssignmentTarget target, PurityAssignmentTargetEffects effect) =>
        (target.Effects & effect) != 0;

    private static PurityAnalysisState ApplyVersionEffects(
        PurityAssignmentTarget target, PurityAnalysisState state)
    {
        foreach (var local in target.WrittenLocals)
        {
            if (Has(target, PurityAssignmentTargetEffects.ClearConcreteType))
                state = state.WithoutLocalConcreteType(local);
            state = state.WithSmtSymbolDefinitionVersion(local, target.DefinitionSyntax);
        }
        return target.Symbol is IParameterSymbol parameter
            ? state.WithSmtSymbolDefinitionVersion(parameter, target.DefinitionSyntax) : state;
    }

    private static PurityAnalysisState ApplyValue(
        PurityAssignmentTarget target, PurityAnalysisState state,
        PurityAnalysisState valueState, PurityAnalysisContext context)
    {
        if (target.Symbol is IParameterSymbol parameter)
        {
            if (Has(target, PurityAssignmentTargetEffects.AdvanceVersion))
                state = state.WithSmtSymbolDefinitionVersion(parameter, target.DefinitionSyntax);
            state = ApplyCanonicalAssignment(state, parameter, target.Value!, valueState, context);
        }
        var incomingState = state;
        var localAliases = target.WrittenLocals
            .Select(local => (Local: local, Aliases: CaptureAliases(local, incomingState)))
            .ToArray();
        foreach (var (local, aliases) in localAliases)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (Has(target, PurityAssignmentTargetEffects.AdvanceVersion))
                state = state.WithSmtSymbolDefinitionVersion(local, target.Value!.Syntax);
            state = ReplayAliases(state, aliases, target.Value!.Syntax);
            state = ApplyCanonicalAssignment(state, local, target.Value, valueState, context);
            state = ApplyConcreteType(state, local, target.Value, valueState, context.SemanticModel.Compilation);
            state = ApplyAcquisitions(state, local, target.Value, valueState, context.SemanticModel.Compilation);
        }
        return state;
    }

    private static PurityAnalysisState ApplyCanonicalAssignment(
        PurityAnalysisState state, ISymbol target, IOperation value,
        PurityAnalysisState valueState, PurityAnalysisContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (value.Syntax is ExpressionSyntax)
        {
            var transition = SymbolicOperationTransfer.ApplyAssignment(
                state.PathState, target, value.Syntax, context.SemanticModel, context.CancellationToken,
                state.GetSmtSymbolVersion, valueState.GetSmtSymbolVersion,
                provenance: "analyzer.assignment", bindingProvenance: "analyzer.assignment",
                evidenceKey: "analyzer.assignment.value");
            if (transition.IsExact) state = state.WithPathState(transition.State);
        }
        var source = TryResolveTrackedSymbol(value, valueState);
        return source == null || SymbolEq.AreEqual(source, target) ||
               SymbolicFactFactory.GetTrackedSymbolType(source)?.IsReferenceType != true ||
               SymbolicFactFactory.GetTrackedSymbolType(target)?.IsReferenceType != true
            ? state
            : PurityOperationTransfer.ApplyReferenceRelationship(
                state, source, valueState, target, SymbolicLifetimeOperationKind.Alias, value.Syntax,
                "analyzer.assignment.alias", "evidence.assignment.alias");
    }

    private static PurityAnalysisState ApplyConcreteType(
        PurityAnalysisState state, ILocalSymbol local, IOperation value,
        PurityAnalysisState valueState, Compilation compilation) =>
        PurityConcreteReceiverResolver.TryResolveKnownConcreteType(
            value, valueState, compilation, out var concreteType)
            ? state.WithLocalConcreteType(local, concreteType, value.Syntax)
            : state.WithoutLocalConcreteType(local);

    private static PurityAnalysisState ApplyAcquisitions(
        PurityAnalysisState state, ILocalSymbol local, IOperation value,
        PurityAnalysisState valueState, Compilation compilation)
    {
        var term = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(local, state);
        if (PurityKnownBclSemantics.IsOwnedLocalArrayValue(value, valueState, compilation))
            state = ApplyLifetime(state, term, local, value, SymbolicLifetimeOperationKind.CreateOwnedValue,
                "analyzer.array.acquire", "evidence.array.acquire");
        if (PurityResourceStateFacts.IsOwnedDisposableObjectCreationValue(value) &&
            !PurityResourceStateFacts.HasReleasedResourceFact(term, state))
            state = ApplyLifetime(state, term, local, value, SymbolicLifetimeOperationKind.AcquireDisposable,
                "analyzer.resource.acquire", "evidence.resource.acquire");
        if (SkipImplicitConversions(value) is IObjectCreationOperation creation &&
            RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(creation.Type))
            state = ApplyLifetime(state, term, local, value, SymbolicLifetimeOperationKind.CreateOwnedValue,
                "analyzer.object.acquire", "evidence.object.acquire");
        return state;
    }

    private static PurityAnalysisState ApplyLifetime(
        PurityAnalysisState state, SymbolicTerm term, ISymbol symbol, IOperation value,
        SymbolicLifetimeOperationKind kind, string provenance, string evidence) =>
        PurityOperationTransfer.ApplyLifetime(
            state, term, kind, value.Syntax, provenance, symbol, evidence);

    private static PurityAnalysisState ApplyDelegateUpdate(
        PurityAssignmentTarget target, PurityAnalysisState state,
        PurityAnalysisState valueState, CancellationToken cancellationToken)
    {
        if (target.DelegateUpdate == PurityDelegateUpdateKind.None) return state;
        if (target.DelegateUpdate == PurityDelegateUpdateKind.UnresolvedWrittenLocals)
        {
            foreach (var local in target.WrittenLocals)
                if (local.Type?.TypeKind == TypeKind.Delegate)
                    state = state.WithDelegateTarget(local, PotentialTargets.Unresolved);
            return state;
        }
        if (target.Symbol == null) return state;
        if (target.DelegateUpdate == PurityDelegateUpdateKind.UnresolvedTarget)
            return state.WithDelegateTarget(target.Symbol, PotentialTargets.Unresolved);

        var valueTargets = target.Value == null
            ? null : ResolvePotentialTargets(target.Value, valueState, cancellationToken);
        if (target.DelegateUpdate == PurityDelegateUpdateKind.AssignIfResolved && valueTargets == null)
            return state;
        if (target.DelegateUpdate == PurityDelegateUpdateKind.Add)
            return valueTargets != null &&
                   valueState.DelegateTargetMap.TryGetValue(target.Symbol, out var currentTargets)
                ? state.WithDelegateTarget(target.Symbol, PotentialTargets.Merge(currentTargets, valueTargets.Value))
                : state.WithDelegateTarget(target.Symbol, PotentialTargets.Unresolved);

        var assignedTargets = valueTargets ?? PotentialTargets.Unresolved;
        if (target.WrittenLocals.IsDefaultOrEmpty)
            return state.WithDelegateTarget(target.Symbol, assignedTargets);
        foreach (var local in target.WrittenLocals) state = state.WithDelegateTarget(local, assignedTargets);
        return state;
    }

    private readonly record struct PreservedAliases(
        ImmutableArray<ISymbol> Owned, ImmutableArray<ISymbol> Disposed);

    private static PreservedAliases CaptureAliases(ISymbol symbol, PurityAnalysisState state) =>
        new(GetAliases(symbol, state, owned: true), GetAliases(symbol, state, owned: false));

    private static ImmutableArray<ISymbol> GetAliases(
        ISymbol reassigned, PurityAnalysisState state, bool owned)
    {
        var term = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(reassigned, state);
        var preserve = owned
            ? HasUnreleasedOwnedResourceObligation(term, state)
            : PurityResourceStateFacts.HasDisposedResourceFactForTerm(term, state);
        if (!preserve) return ImmutableArray<ISymbol>.Empty;
        var result = ImmutableArray.CreateBuilder<ISymbol>();
        var seen = new HashSet<ISymbol>(SymbolEq.Default);
        foreach (var fact in state.PathState.Facts)
            if (fact is { Polarity: true, Confidence: SymbolicFactConfidence.Exact,
                    Atom: SymbolicAliasAtom { MayAlias: true } alias, Symbol: { } symbol } &&
                Equals(alias.Source, term) && !SymbolEq.AreEqual(symbol, reassigned) &&
                seen.Add(symbol))
                result.Add(symbol);
        return result.ToImmutable();
    }

    private static PurityAnalysisState ReplayAliases(
        PurityAnalysisState state, PreservedAliases aliases, SyntaxNode source)
    {
        foreach (var alias in aliases.Owned) state = ReplayAlias(state, alias, source, owned: true);
        foreach (var alias in aliases.Disposed) state = ReplayAlias(state, alias, source, owned: false);
        return state;
    }

    private static PurityAnalysisState ReplayAlias(
        PurityAnalysisState state, ISymbol alias, SyntaxNode source, bool owned) =>
        PurityOperationTransfer.ApplyLifetime(
            state, PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(alias, state),
            owned ? SymbolicLifetimeOperationKind.AcquireDisposable : SymbolicLifetimeOperationKind.Dispose,
            source, owned ? "analyzer.resource.alias-preserve" : "analyzer.resource.alias-preserve.disposed",
            alias, owned ? "evidence.resource.alias-preserve" : "evidence.resource.alias-preserve.disposed");

    private static bool HasUnreleasedOwnedResourceObligation(SymbolicTerm term, PurityAnalysisState state)
    {
        var owned = state.PathState.Facts.Any(fact =>
            fact.Polarity && fact.Confidence == SymbolicFactConfidence.Exact &&
            (fact.Atom is SymbolicResourceLifetimeAtom
                { State: SymbolicResourceLifetimeState.Owned } lifetime && Equals(lifetime.Resource, term) ||
             fact.Atom is SymbolicDisposalAtom
                { State: SymbolicDisposalState.NotDisposed } disposal && Equals(disposal.Resource, term)));
        return owned && !SymbolicStateMerger.HasExactResourceRelease(state.PathState, term);
    }
}
