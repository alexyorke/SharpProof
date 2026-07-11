using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static SymbolicVariableTerm CreateSymbolicReferenceTerm(ISymbol symbol, PurityAnalysisState currentState)
    {
        return new SymbolicVariableTerm(
            GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion),
            SmtValueKind.Reference);
    }

    internal static bool HasSymbolicBorrowFactForLocal(
        ILocalSymbol localSymbol,
        PurityAnalysisState currentState,
        SymbolicBorrowKind? borrowKind = null)
    {
        var localTerm = CreateSymbolicReferenceTerm(localSymbol, currentState);
        return HasSymbolicBorrowFactForTerm(
            localTerm,
            currentState,
            borrowKind,
            new HashSet<SymbolicTerm>());
    }

    internal static bool HasSymbolicBorrowerFactForSymbol(
        ISymbol ownerSymbol,
        PurityAnalysisState currentState)
    {
        var ownerTerm = CreateSymbolicReferenceTerm(ownerSymbol, currentState);
        return HasSymbolicBorrowerFactForTerm(
            ownerTerm,
            currentState,
            new HashSet<SymbolicTerm>());
    }

    internal static bool TryCreateMutableBorrowConflictEvidence(
        IOperation operation,
        ISymbol? targetSymbol,
        PurityAnalysisState currentState,
        string ruleName,
        out PurityEvidence evidence)
    {
        evidence = PurityEvidence.None;
        if (targetSymbol == null ||
            !HasSymbolicBorrowerFactForSymbol(targetSymbol, currentState))
            return false;

        evidence = PurityEvidence.Create(
            "mutable_state_write",
            ruleName,
            operation,
            operation.Syntax,
            targetSymbol,
            "analyzer.borrow.mutable-conflict");
        return true;
    }

    internal static bool TryCreateMutableBorrowConflictEvidence(
        IOperation operation,
        ISymbol? targetSymbol,
        PurityAnalysisState currentState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string ruleName,
        out PurityEvidence evidence)
    {
        if (TryCreateMutableBorrowConflictEvidence(
                operation,
                targetSymbol,
                currentState,
                ruleName,
                out evidence))
            return true;

        if (targetSymbol is ILocalSymbol targetLocal &&
            HasActiveRefLocalBorrowAfterWrite(
                targetLocal,
                operation.Syntax,
                semanticModel,
                cancellationToken))
        {
            evidence = PurityEvidence.Create(
                "mutable_state_write",
                ruleName,
                operation,
                operation.Syntax,
                targetLocal,
                "analyzer.borrow.mutable-conflict");
            return true;
        }

        evidence = PurityEvidence.None;
        return false;
    }

    private static bool HasActiveRefLocalBorrowAfterWrite(
        ILocalSymbol targetLocal,
        SyntaxNode writeSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var containingBlock = writeSyntax.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock == null) return false;

        var borrowedLocals = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var declarator in containingBlock.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (declarator.SpanStart >= writeSyntax.SpanStart ||
                    declarator.Initializer?.Value is not RefExpressionSyntax refExpression ||
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol refLocal ||
                    semanticModel.GetSymbolInfo(refExpression.Expression, cancellationToken).Symbol is not ILocalSymbol
                        sourceLocal)
                    continue;

                if ((SymbolEqualityComparer.Default.Equals(sourceLocal, targetLocal) ||
                     borrowedLocals.Contains(sourceLocal)) &&
                    borrowedLocals.Add(refLocal))
                    changed = true;
            }
        }

        foreach (var borrowedLocal in borrowedLocals.OfType<ILocalSymbol>())
            if (IsLocalUsedAfter(borrowedLocal, writeSyntax, containingBlock, semanticModel, cancellationToken))
                return true;

        return false;
    }

    private static bool IsLocalUsedAfter(
        ILocalSymbol localSymbol,
        SyntaxNode writeSyntax,
        BlockSyntax containingBlock,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var identifierName in containingBlock.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifierName.SpanStart <= writeSyntax.SpanStart) continue;

            if (semanticModel.GetSymbolInfo(identifierName, cancellationToken).Symbol is ILocalSymbol usedLocal &&
                SymbolEqualityComparer.Default.Equals(usedLocal, localSymbol))
                return true;
        }

        return false;
    }

    private static bool HasSymbolicBorrowerFactForTerm(
        SymbolicTerm ownerTerm,
        PurityAnalysisState currentState,
        HashSet<SymbolicTerm> visitedTerms)
    {
        if (!visitedTerms.Add(ownerTerm)) return false;

        foreach (var fact in currentState.PathState.Facts)
            if (fact.Polarity &&
                fact.Confidence == SymbolicFactConfidence.Exact &&
                fact.Atom is SymbolicBorrowAtom borrow &&
                Equals(borrow.Owner, ownerTerm))
                return true;

        foreach (var aliasTerm in EnumerateSymbolicAliasTerms(ownerTerm, currentState))
            if (HasSymbolicBorrowerFactForTerm(aliasTerm, currentState, visitedTerms))
                return true;

        return false;
    }

    private static bool HasSymbolicBorrowFactForTerm(
        SymbolicTerm localTerm,
        PurityAnalysisState currentState,
        SymbolicBorrowKind? borrowKind,
        HashSet<SymbolicTerm> visitedTerms)
    {
        if (!visitedTerms.Add(localTerm)) return false;

        foreach (var fact in currentState.PathState.Facts)
        {
            if (!fact.Polarity ||
                fact.Confidence != SymbolicFactConfidence.Exact ||
                fact.Atom is not SymbolicBorrowAtom borrow ||
                !Equals(borrow.Borrow, localTerm) ||
                (borrowKind.HasValue && borrow.Kind != borrowKind.Value))
                continue;

            return true;
        }

        foreach (var aliasTerm in EnumerateSymbolicAliasTerms(localTerm, currentState))
            if (HasSymbolicBorrowFactForTerm(aliasTerm, currentState, borrowKind, visitedTerms))
                return true;

        return false;
    }

    internal static bool HasSymbolicOwnedFactForSymbol(
        ISymbol symbol,
        PurityAnalysisState currentState)
    {
        var symbolTerm = CreateSymbolicReferenceTerm(symbol, currentState);
        return HasSymbolicOwnedFactForTerm(
            symbolTerm,
            currentState,
            new HashSet<SymbolicTerm>());
    }

    private static bool HasSymbolicOwnedFactForTerm(
        SymbolicTerm symbolTerm,
        PurityAnalysisState currentState,
        HashSet<SymbolicTerm> visitedTerms)
    {
        if (!visitedTerms.Add(symbolTerm)) return false;

        foreach (var fact in currentState.PathState.Facts)
        {
            if (!fact.Polarity ||
                fact.Confidence != SymbolicFactConfidence.Exact)
                continue;

            if (fact.Atom is SymbolicOwnershipAtom { Escaped: false } ownership &&
                Equals(ownership.Value, symbolTerm))
                return true;

            if (fact.Atom is SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime &&
                Equals(lifetime.Resource, symbolTerm))
                return true;
        }

        foreach (var aliasTerm in EnumerateSymbolicAliasTerms(symbolTerm, currentState))
            if (HasSymbolicOwnedFactForTerm(aliasTerm, currentState, visitedTerms))
                return true;

        return false;
    }

    private static IEnumerable<SymbolicTerm> EnumerateSymbolicAliasTerms(
        SymbolicTerm symbolTerm,
        PurityAnalysisState currentState)
    {
        foreach (var fact in currentState.PathState.Facts)
        {
            if (!fact.Polarity ||
                fact.Confidence != SymbolicFactConfidence.Exact ||
                fact.Atom is not SymbolicAliasAtom { MayAlias: true } alias)
                continue;

            if (Equals(alias.Target, symbolTerm)) yield return alias.Source;

            if (Equals(alias.Source, symbolTerm)) yield return alias.Target;
        }
    }

    private static PurityAnalysisState AddAssignedValueFact(
        PurityAnalysisState currentState,
        ISymbol targetSymbol,
        IOperation? valueOperation,
        PurityAnalysisState valueState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (valueOperation?.Syntax is not ExpressionSyntax valueExpression) return currentState;

        var nextState = AddAssignedAliasFact(
            currentState,
            targetSymbol,
            valueOperation,
            valueState);
        if (SymbolicReachabilityService.TryCreateAssignedValueFact(
                targetSymbol,
                valueExpression,
                semanticModel,
                cancellationToken,
                out var assignedFact,
                valueState.GetSmtSymbolVersion,
                currentState.GetSmtSymbolVersion) &&
            TryCreateSymbolicValueTerm(targetSymbol, currentState, out var targetTerm))
        {
            nextState = nextState.WithPathConditions(nextState.PathConditions.Add(assignedFact));
            nextState = AddAssignedSymbolicEqualityFact(
                nextState,
                targetTerm,
                valueExpression,
                valueState,
                semanticModel,
                SymbolicSemanticPipeline.LowerTerm,
                "analyzer.assignment",
                "analyzer.assignment.value",
                cancellationToken);
        }

        if (SymbolicReachabilityService.TryCreateBuiltInLengthAssignedValueFact(
                targetSymbol,
                valueExpression,
                semanticModel,
                cancellationToken,
                out var lengthAssignedFact,
                valueState.GetSmtSymbolVersion,
                currentState.GetSmtSymbolVersion) &&
            TryCreateSymbolicLengthTerm(targetSymbol, currentState, out var targetLengthTerm))
        {
            nextState = nextState.WithPathConditions(nextState.PathConditions.Add(lengthAssignedFact));
            nextState = AddAssignedSymbolicEqualityFact(
                nextState,
                targetLengthTerm,
                valueExpression,
                valueState,
                semanticModel,
                SymbolicSemanticPipeline.LowerLengthProjectionTerm,
                "analyzer.assignment.length",
                "analyzer.assignment.length",
                cancellationToken);
        }

        if (TryCreateReferenceBackedLengthFact(
                targetSymbol,
                valueExpression,
                currentState,
                valueState,
                semanticModel,
                cancellationToken,
                out var referenceLengthFact))
        {
            nextState = nextState.WithPathConditions(nextState.PathConditions.Add(referenceLengthFact));
            if (TryCreateSymbolicLengthTerm(targetSymbol, currentState, out var referenceTargetLengthTerm))
                nextState = AddAssignedSymbolicEqualityFact(
                nextState,
                referenceTargetLengthTerm,
                valueExpression,
                valueState,
                semanticModel,
                SymbolicSemanticPipeline.LowerLengthProjectionTerm,
                "analyzer.assignment.reference_length",
                "analyzer.assignment.reference_length",
                cancellationToken);
        }

        if (TryCreateCollectionExpressionLengthLowerBoundFact(
                targetSymbol,
                valueExpression,
                currentState,
                out var lowerBoundLengthFact))
        {
            nextState = nextState.WithPathConditions(nextState.PathConditions.Add(lowerBoundLengthFact));
            if (TryCreateCollectionExpressionLengthLowerBoundCondition(
                    targetSymbol,
                    valueExpression,
                    currentState,
                    out var lowerBoundCondition))
                nextState = nextState.WithPathConditionsAndState(
                    nextState.PathConditions,
                    nextState.PathState.AddPathCondition(lowerBoundCondition));
        }

        if (SymbolicReachabilityService.TryCreateStringContentAssignedValueFact(
                targetSymbol,
                valueExpression,
                semanticModel,
                cancellationToken,
                out var stringAssignedFact,
                valueState.GetSmtSymbolVersion,
                currentState.GetSmtSymbolVersion) &&
            TryCreateSymbolicStringContentTerm(targetSymbol, currentState, out var targetStringTerm))
        {
            nextState = nextState.WithPathConditions(nextState.PathConditions.Add(stringAssignedFact));
            nextState = AddAssignedSymbolicEqualityFact(
                nextState,
                targetStringTerm,
                valueExpression,
                valueState,
                semanticModel,
                SymbolicSemanticPipeline.LowerStringTerm,
                "analyzer.assignment.string",
                "analyzer.assignment.string",
                cancellationToken);
        }

        if (SymbolicReachabilityService.TryCreateAsExpressionAssignedValueFacts(
                targetSymbol,
                valueExpression,
                semanticModel,
                cancellationToken,
                out var asExpressionFacts,
                valueState.GetSmtSymbolVersion,
                currentState.GetSmtSymbolVersion))
        {
            nextState = nextState.WithPathConditions(nextState.PathConditions.AddRange(asExpressionFacts));
            if (SymbolicReachabilityService.TryCreateAsExpressionAssignedValueConditions(
                    targetSymbol,
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var asExpressionConditions,
                    valueState.GetSmtSymbolVersion,
                    currentState.GetSmtSymbolVersion))
                foreach (var asExpressionCondition in asExpressionConditions)
                    nextState = nextState.WithPathConditionsAndState(
                        nextState.PathConditions,
                        nextState.PathState.AddPathCondition(asExpressionCondition));
        }

        if (TryCreateReferenceBackedStringContentFact(
                targetSymbol,
                valueExpression,
                currentState,
                valueState,
                semanticModel,
                cancellationToken,
                out var referenceStringFact))
        {
            nextState = nextState.WithPathConditions(nextState.PathConditions.Add(referenceStringFact));
            if (TryCreateSymbolicStringContentTerm(targetSymbol, currentState, out var referenceTargetStringTerm))
                nextState = AddAssignedSymbolicEqualityFact(
                nextState,
                referenceTargetStringTerm,
                valueExpression,
                valueState,
                semanticModel,
                SymbolicSemanticPipeline.LowerStringTerm,
                "analyzer.assignment.reference_string",
                "analyzer.assignment.reference_string",
                cancellationToken);
        }

        if (SymbolicReachabilityService.TryCreateStringNonNullAssignedValueFact(
                targetSymbol,
                valueExpression,
                semanticModel,
                cancellationToken,
                out var stringNonNullFact,
                valueState.GetSmtSymbolVersion,
                currentState.GetSmtSymbolVersion) &&
            TryCreateSymbolicValueTerm(targetSymbol, currentState, out var targetReferenceTerm) &&
            targetReferenceTerm is { Kind: SmtValueKind.Reference })
        {
            nextState = nextState.WithPathConditions(nextState.PathConditions.Add(stringNonNullFact));
            var nonNullFact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.NotEqual,
                    targetReferenceTerm,
                    new SymbolicNullTerm()),
                valueExpression,
                "analyzer.assignment.string_nonnull",
                evidenceKey: "analyzer.assignment.string_nonnull");
            nextState = nextState.WithPathConditionsAndState(
                nextState.PathConditions,
                nextState.PathState.AddPathCondition(new SymbolicFactCondition(nonNullFact)));
        }

        return nextState;
    }

    private static PurityAnalysisState AddAssignedAliasFact(
        PurityAnalysisState currentState,
        ISymbol targetSymbol,
        IOperation valueOperation,
        PurityAnalysisState valueState)
    {
        var sourceSymbol = TryResolveTrackedSymbol(valueOperation, valueState);
        if (sourceSymbol == null ||
            SymbolEqualityComparer.Default.Equals(sourceSymbol, targetSymbol) ||
            SymbolicFactFactory.GetTrackedSymbolType(sourceSymbol)?.IsReferenceType != true ||
            SymbolicFactFactory.GetTrackedSymbolType(targetSymbol)?.IsReferenceType != true)
            return currentState;

        var sourceTerm = CreateSymbolicReferenceTerm(sourceSymbol, valueState);
        var targetTerm = CreateSymbolicReferenceTerm(targetSymbol, currentState);
        var aliasFact = SymbolicOwnershipFactFactory.CreateAlias(
            sourceTerm,
            targetTerm,
            true,
            valueOperation.Syntax,
            "analyzer.assignment.alias",
            targetSymbol,
            "evidence.assignment.alias");

        return currentState.WithPathConditionsAndState(
            currentState.PathConditions,
            currentState.PathState.AddFact(aliasFact));
    }

    private static PurityAnalysisState AddDeclaredBorrowFact(
        PurityAnalysisState currentState,
        ILocalSymbol declaredSymbol,
        IOperation initializerValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isRefInitializer = initializerValue.Syntax.Parent is RefExpressionSyntax ||
                               initializerValue.Syntax.Ancestors().OfType<RefExpressionSyntax>().Any();
        if (!isRefInitializer &&
            declaredSymbol.RefKind is not (RefKind.Ref or RefKind.Out or RefKind.In or RefKind.RefReadOnly))
            return currentState;

        var sourceSymbol = TryResolveTrackedSymbol(initializerValue, currentState) ??
                           TryResolveRefInitializerSymbol(initializerValue.Syntax, semanticModel, currentState,
                               cancellationToken);
        if (sourceSymbol == null) return currentState;

        var borrowKind = declaredSymbol.RefKind is RefKind.In or RefKind.RefReadOnly
            ? SymbolicBorrowKind.Shared
            : SymbolicBorrowKind.Mutable;
        var sourceTerm = CreateSymbolicReferenceTerm(sourceSymbol, currentState);
        var borrowTerm = CreateSymbolicReferenceTerm(declaredSymbol, currentState);
        var borrowFact = SymbolicOwnershipFactFactory.CreateBorrow(
            sourceTerm,
            borrowTerm,
            borrowKind,
            initializerValue.Syntax,
            "analyzer.declaration.borrow",
            declaredSymbol,
            "evidence.declaration.borrow");

        return currentState.WithPathConditionsAndState(
            currentState.PathConditions,
            currentState.PathState.AddFact(borrowFact));
    }

    private static ISymbol? TryResolveRefInitializerSymbol(
        SyntaxNode initializerSyntax,
        SemanticModel semanticModel,
        PurityAnalysisState currentState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var refExpression = initializerSyntax.AncestorsAndSelf().OfType<RefExpressionSyntax>().FirstOrDefault();
        if (refExpression == null) return null;

        if (semanticModel.GetOperation(refExpression.Expression, cancellationToken) is { } operation &&
            TryResolveTrackedSymbol(operation, currentState) is { } operationSymbol)
            return operationSymbol;

        return semanticModel.GetSymbolInfo(refExpression.Expression, cancellationToken).Symbol;
    }

    private static PurityAnalysisState AddAssignedSymbolicEqualityFact(
        PurityAnalysisState currentState,
        SymbolicTerm targetTerm,
        ExpressionSyntax valueExpression,
        PurityAnalysisState valueState,
        SemanticModel semanticModel,
        LowerAssignedSymbolicTerm lowerValueTerm,
        string provenance,
        string evidenceKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lowering = lowerValueTerm(
                valueExpression,
                new SymbolicLoweringContext(
                    semanticModel,
                    cancellationToken,
                    valueState.GetSmtSymbolVersion));
        if (lowering is not { IsExact: true, Value: { } valueTerm } ||
            !CanCompareSymbolicTerms(targetTerm, valueTerm))
            return currentState;

        var fact = SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                targetTerm,
                valueTerm),
            valueExpression,
            provenance,
            evidenceKey: evidenceKey);
        return currentState.WithPathConditionsAndState(
            currentState.PathConditions,
            currentState.PathState.AddPathCondition(new SymbolicFactCondition(fact)));
    }

    private static bool CanCompareSymbolicTerms(SymbolicTerm left, SymbolicTerm right)
    {
        return left.Kind == right.Kind ||
               (left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference) ||
               (right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference);
    }

    private static bool TryCreateSymbolicValueTerm(
        ISymbol symbol,
        PurityAnalysisState currentState,
        out SymbolicTerm term)
    {
        var type = SymbolicFactFactory.GetTrackedSymbolType(symbol);
        if (type != null &&
            SymbolicFactFactory.TryGetValueKind(
                type,
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                SymbolicTypeFacts.IsReferenceType,
                out var kind))
        {
            term = new SymbolicVariableTerm(
                GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion),
                kind);
            return true;
        }

        term = null!;
        return false;
    }

    private static bool TryCreateSymbolicLengthTerm(
        ISymbol symbol,
        PurityAnalysisState currentState,
        out SymbolicTerm term)
    {
        if (TryCreateSymbolicValueTerm(symbol, currentState, out var value) &&
            value.Kind == SmtValueKind.Reference)
        {
            term = new SymbolicLengthTerm(value);
            return true;
        }

        term = null!;
        return false;
    }

    private static bool TryCreateSymbolicStringContentTerm(
        ISymbol symbol,
        PurityAnalysisState currentState,
        out SymbolicTerm term)
    {
        if (SymbolicFactFactory.GetTrackedSymbolType(symbol)?.SpecialType == SpecialType.System_String &&
            TryCreateSymbolicValueTerm(symbol, currentState, out var value) &&
            value.Kind == SmtValueKind.Reference)
        {
            term = new SymbolicStringContentTerm(value);
            return true;
        }

        term = null!;
        return false;
    }

    private static bool TryCreateCollectionExpressionLengthLowerBoundCondition(
        ISymbol targetSymbol,
        ExpressionSyntax valueExpression,
        PurityAnalysisState currentState,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (UnwrapSmtFactExpression(valueExpression) is not CollectionExpressionSyntax collectionExpression ||
            !TryCreateSymbolicLengthTerm(targetSymbol, currentState, out var length))
            return false;

        var lowerBound = collectionExpression.Elements.Count(static element => element is ExpressionElementSyntax);
        if (lowerBound == 0 || !collectionExpression.Elements.Any(static element => element is SpreadElementSyntax))
            return false;

        condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThanOrEqual,
                length,
                new SymbolicIntegerConstantTerm(lowerBound)),
            valueExpression,
            "analyzer.assignment.collection_length",
            evidenceKey: "analyzer.assignment.collection_length"));
        return true;
    }

    private static bool TryCreateReferenceBackedLengthFact(
        ISymbol targetSymbol,
        ExpressionSyntax valueExpression,
        PurityAnalysisState currentState,
        PurityAnalysisState valueState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula fact)
    {
        return SymbolicReachabilityService.TryCreateReferenceBackedLengthFact(
            targetSymbol,
            valueExpression,
            semanticModel,
            cancellationToken,
            out fact,
            valueState.GetSmtSymbolVersion,
            currentState.GetSmtSymbolVersion);
    }

    private static bool TryCreateReferenceBackedStringContentFact(
        ISymbol targetSymbol,
        ExpressionSyntax valueExpression,
        PurityAnalysisState currentState,
        PurityAnalysisState valueState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula fact)
    {
        return SymbolicReachabilityService.TryCreateReferenceBackedStringContentFact(
            targetSymbol,
            valueExpression,
            semanticModel,
            cancellationToken,
            out fact,
            valueState.GetSmtSymbolVersion,
            currentState.GetSmtSymbolVersion);
    }

    private static bool TryCreateSymbolSmtValue(
        ISymbol symbol,
        PurityAnalysisState currentState,
        out SmtFormula formula)
    {
        return SymbolicFactFactory.TryCreateSymbolVariableFormula(
            GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion),
            SymbolicFactFactory.GetTrackedSymbolType(symbol),
            SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
            static type => type.IsReferenceType,
            out formula);
    }

    private static bool TryCreateStringContentFormula(
        ISymbol symbol,
        PurityAnalysisState currentState,
        out SmtFormula formula)
    {
        var type = SymbolicFactFactory.GetTrackedSymbolType(symbol);

        return SymbolicFactFactory.TryCreateStringContentFormula(
            GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion),
            type,
            out formula);
    }

    private static bool TryCreateBuiltInLengthFormula(
        ISymbol symbol,
        PurityAnalysisState currentState,
        out SmtFormula formula)
    {
        var type = symbol switch
        {
            ILocalSymbol localSymbol => localSymbol.Type,
            IParameterSymbol parameterSymbol => parameterSymbol.Type,
            _ => null
        };

        return SymbolicFactFactory.TryCreateBuiltInLengthFormula(
            GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion),
            type,
            out formula);
    }

    private static bool TryCreateCollectionExpressionLengthLowerBoundFact(
        ISymbol targetSymbol,
        ExpressionSyntax valueExpression,
        PurityAnalysisState currentState,
        out SmtFormula fact)
    {
        fact = null!;
        return TryCreateBuiltInLengthFormula(targetSymbol, currentState, out var targetLengthFormula) &&
               SymbolicFactFactory.TryCreateCollectionExpressionLengthLowerBoundFact(
                   targetLengthFormula,
                   UnwrapSmtFactExpression(valueExpression),
                   out fact);
    }

    private static ExpressionSyntax UnwrapSmtFactExpression(ExpressionSyntax expression)
    {
        return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
    }

    private delegate SymbolicLoweringResult<SymbolicTerm> LowerAssignedSymbolicTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context);
}
