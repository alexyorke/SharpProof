using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.FlowAnalysis;
using System.Collections.Immutable;
using System;
using System.IO;
using System.Globalization;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;
using System.Threading;

namespace SharpProof.Analyzer.Engine
{

    internal partial class PurityAnalysisEngine
    {

        private static bool TryCreateMissingOwnedResourceDisposalResult(
            PurityAnalysisState state,
            IMethodSymbol containingMethodSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out PurityAnalysisResult result)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = PurityAnalysisResult.Pure;

            var ownedResources = new Dictionary<SymbolicTerm, ISymbol?>();
            var releasedResources = new HashSet<SymbolicTerm>();
            foreach (var fact in state.PathState.Facts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!fact.Polarity ||
                    fact.Confidence != SymbolicFactConfidence.Exact)
                {
                    continue;
                }

                if (TryGetExactResourceRelease(fact, out var releasedResource, out _))
                {
                    releasedResources.Add(releasedResource);
                    continue;
                }

                switch (fact.Atom)
                {
                    case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime:
                        ownedResources[lifetime.Resource] = fact.Symbol;
                        break;
                    case SymbolicDisposalAtom { State: SymbolicDisposalState.NotDisposed } disposal:
                        ownedResources[disposal.Resource] = fact.Symbol;
                        break;
                }
            }

            foreach (var resource in ownedResources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsResourceReleased(resource.Key, releasedResources, state, new HashSet<SymbolicTerm>()))
                {
                    continue;
                }

                if (resource.Value != null &&
                    IsOwnedResourceReleasedOnAllSyntaxPaths(containingMethodSymbol, resource.Value, semanticModel, cancellationToken))
                {
                    continue;
                }

                var syntax = containingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
                if (syntax == null)
                {
                    return false;
                }

                result = PurityAnalysisResult.Impure(
                    syntax,
                    PurityEvidence.Create(
                        "resource_missing_dispose",
                        ruleName: "ResourceLifetimeAnalysis",
                        syntaxNode: syntax,
                        symbol: resource.Value,
                    catalogSource: "symbolic_resource_lifetime"));
                return true;
            }

            if (TryFindAliasedOwnedResourceLostByReassignment(
                    containingMethodSymbol,
                    semanticModel,
                    cancellationToken,
                    out var aliasLeakSyntax,
                    out var aliasLeakSymbol))
            {
                result = PurityAnalysisResult.Impure(
                    aliasLeakSyntax,
                    PurityEvidence.Create(
                        "resource_missing_dispose",
                        ruleName: "ResourceLifetimeAnalysis",
                        syntaxNode: aliasLeakSyntax,
                        symbol: aliasLeakSymbol,
                        catalogSource: "symbolic_resource_lifetime.alias-preserve"));
                return true;
            }

            return false;
        }

        private static bool IsOwnedResourceReleasedOnAllSyntaxPaths(
            IMethodSymbol containingMethodSymbol,
            ISymbol resourceSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var syntaxReference in containingMethodSymbol.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (syntaxReference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax { Body: { } body } methodDeclaration)
                {
                    continue;
                }

                var methodSemanticModel = semanticModel.Compilation.GetSemanticModel(methodDeclaration.SyntaxTree);
                for (var index = 0; index < body.Statements.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!DeclaresSymbol(body.Statements[index], resourceSymbol, methodSemanticModel, cancellationToken))
                    {
                        continue;
                    }

                    var remainingStatements = body.Statements.Skip(index + 1).ToArray();
                    var summary = AnalyzeResourceReleaseStatements(
                        remainingStatements,
                        initiallyReleased: false,
                        endIsTerminal: true,
                        resourceSymbol,
                        methodSemanticModel,
                        cancellationToken);
                    return summary.AllTerminalPathsReleased;
                }
            }

            return false;
        }

        private static bool DeclaresSymbol(
            StatementSyntax statement,
            ISymbol resourceSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var declarator in statement.DescendantNodesAndSelf().OfType<VariableDeclaratorSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is { } declaredSymbol &&
                    SymbolEqualityComparer.Default.Equals(declaredSymbol, resourceSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static ResourceReleasePathSummary AnalyzeResourceReleaseStatements(
            IReadOnlyList<StatementSyntax> statements,
            bool initiallyReleased,
            bool endIsTerminal,
            ISymbol resourceSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var allTerminalPathsReleased = true;
            var currentStates = new List<bool> { initiallyReleased };

            foreach (var statement in statements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (currentStates.Count == 0)
                {
                    break;
                }

                var nextStates = new List<bool>();
                foreach (var released in currentStates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var summary = AnalyzeResourceReleaseStatement(
                        statement,
                        released,
                        resourceSymbol,
                        semanticModel,
                        cancellationToken);
                    allTerminalPathsReleased &= summary.AllTerminalPathsReleased;
                    nextStates.AddRange(summary.FallthroughReleasedStates);
                }

                currentStates = nextStates;
            }

            if (endIsTerminal)
            {
                allTerminalPathsReleased &= currentStates.All(static released => released);
                currentStates.Clear();
            }

            return new ResourceReleasePathSummary(
                allTerminalPathsReleased,
                currentStates.ToImmutableArray());
        }

        private static ResourceReleasePathSummary AnalyzeResourceReleaseStatement(
            StatementSyntax statement,
            bool initiallyReleased,
            ISymbol resourceSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (statement is ReturnStatementSyntax returnStatement)
            {
                return new ResourceReleasePathSummary(
                    initiallyReleased || IsReturnedSymbol(returnStatement, resourceSymbol, semanticModel, cancellationToken),
                    ImmutableArray<bool>.Empty);
            }

            if (statement is IfStatementSyntax ifStatement)
            {
                var thenSummary = AnalyzeResourceReleaseStatements(
                    GetStatementList(ifStatement.Statement),
                    initiallyReleased,
                    endIsTerminal: false,
                    resourceSymbol,
                    semanticModel,
                    cancellationToken);
                var elseSummary = ifStatement.Else == null
                    ? new ResourceReleasePathSummary(true, ImmutableArray.Create(initiallyReleased))
                    : AnalyzeResourceReleaseStatements(
                        GetStatementList(ifStatement.Else.Statement),
                        initiallyReleased,
                        endIsTerminal: false,
                        resourceSymbol,
                        semanticModel,
                        cancellationToken);

                return new ResourceReleasePathSummary(
                    thenSummary.AllTerminalPathsReleased && elseSummary.AllTerminalPathsReleased,
                    thenSummary.FallthroughReleasedStates.AddRange(elseSummary.FallthroughReleasedStates));
            }

            if (statement is SwitchStatementSyntax switchStatement)
            {
                return AnalyzeSwitchResourceReleaseStatement(
                    switchStatement,
                    initiallyReleased,
                    resourceSymbol,
                    semanticModel,
                    cancellationToken);
            }

            if (statement is WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax)
            {
                return new ResourceReleasePathSummary(
                    true,
                    ImmutableArray.Create(initiallyReleased));
            }

            if (statement is DoStatementSyntax doStatement)
            {
                return AnalyzeResourceReleaseStatements(
                    GetStatementList(doStatement.Statement),
                    initiallyReleased,
                    endIsTerminal: false,
                    resourceSymbol,
                    semanticModel,
                    cancellationToken);
            }

            if (statement is TryStatementSyntax { Finally.Block: { } finallyBlock } &&
                FinallyBlockReleasesResource(finallyBlock, resourceSymbol, semanticModel, cancellationToken))
            {
                return new ResourceReleasePathSummary(
                    true,
                    ImmutableArray.Create(true));
            }

            var released = initiallyReleased ||
                DisposesSymbol(statement, resourceSymbol, semanticModel, cancellationToken);
            return new ResourceReleasePathSummary(
                true,
                ImmutableArray.Create(released));
        }

        private static ResourceReleasePathSummary AnalyzeSwitchResourceReleaseStatement(
            SwitchStatementSyntax switchStatement,
            bool initiallyReleased,
            ISymbol resourceSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var allTerminalPathsReleased = true;
            var fallthroughStates = ImmutableArray.CreateBuilder<bool>();
            var hasDefault = false;

            foreach (var section in switchStatement.Sections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hasDefault |= section.Labels.OfType<DefaultSwitchLabelSyntax>().Any();
                var summary = AnalyzeResourceReleaseStatements(
                    section.Statements.ToArray(),
                    initiallyReleased,
                    endIsTerminal: false,
                    resourceSymbol,
                    semanticModel,
                    cancellationToken);

                allTerminalPathsReleased &= summary.AllTerminalPathsReleased;
                fallthroughStates.AddRange(summary.FallthroughReleasedStates);
            }

            if (!hasDefault)
            {
                fallthroughStates.Add(initiallyReleased);
            }

            return new ResourceReleasePathSummary(
                allTerminalPathsReleased,
                fallthroughStates.ToImmutable());
        }

        private static IReadOnlyList<StatementSyntax> GetStatementList(StatementSyntax statement)
        {
            return statement is BlockSyntax block
                ? block.Statements.ToArray()
                : new[] { statement };
        }

        private static bool FinallyBlockReleasesResource(
            BlockSyntax finallyBlock,
            ISymbol resourceSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var summary = AnalyzeResourceReleaseStatements(
                finallyBlock.Statements.ToArray(),
                initiallyReleased: false,
                endIsTerminal: false,
                resourceSymbol,
                semanticModel,
                cancellationToken);

            return summary.AllTerminalPathsReleased &&
                summary.FallthroughReleasedStates.Length > 0 &&
                summary.FallthroughReleasedStates.All(static released => released);
        }

        private static bool IsReturnedSymbol(
            ReturnStatementSyntax returnStatement,
            ISymbol resourceSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (returnStatement.Expression == null ||
                semanticModel.GetSymbolInfo(returnStatement.Expression, cancellationToken).Symbol is not { } returnedSymbol)
            {
                return false;
            }

            return GetResourceSymbolsVisibleAt(
                    resourceSymbol,
                    returnStatement,
                    semanticModel,
                    cancellationToken)
                .Contains(returnedSymbol);
        }

        private static bool DisposesSymbol(
            StatementSyntax statement,
            ISymbol resourceSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var relatedSymbols = GetResourceSymbolsVisibleAt(
                resourceSymbol,
                statement,
                semanticModel,
                cancellationToken);
            foreach (var invocation in statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    memberAccess.Name.Identifier.ValueText is not (nameof(IDisposable.Dispose) or "DisposeAsync") ||
                    semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not { } disposedSymbol)
                {
                    continue;
                }

                if (relatedSymbols.Contains(disposedSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<ISymbol> GetResourceSymbolsVisibleAt(
            ISymbol resourceSymbol,
            SyntaxNode observationSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var containingBlock = observationSyntax
                .AncestorsAndSelf()
                .OfType<BlockSyntax>()
                .LastOrDefault();
            if (containingBlock == null)
            {
                return new HashSet<ISymbol>(SymbolEqualityComparer.Default)
                {
                    resourceSymbol
                };
            }

            return GetRelatedLocalAliases(
                resourceSymbol,
                observationSyntax,
                containingBlock,
                semanticModel,
                cancellationToken);
        }

        private readonly struct ResourceReleasePathSummary
        {
            public ResourceReleasePathSummary(
                bool allTerminalPathsReleased,
                ImmutableArray<bool> fallthroughReleasedStates)
            {
                AllTerminalPathsReleased = allTerminalPathsReleased;
                FallthroughReleasedStates = fallthroughReleasedStates;
            }

            public bool AllTerminalPathsReleased { get; }

            public ImmutableArray<bool> FallthroughReleasedStates { get; }
        }

        private static bool TryFindAliasedOwnedResourceLostByReassignment(
            IMethodSymbol containingMethodSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SyntaxNode syntax,
            out ISymbol? symbol)
        {
            foreach (var syntaxReference in containingMethodSymbol.DeclaringSyntaxReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (syntaxReference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax methodDeclaration ||
                    methodDeclaration.Body == null)
                {
                    continue;
                }

                var methodSemanticModel = semanticModel.Compilation.GetSemanticModel(methodDeclaration.SyntaxTree);
                foreach (var declarator in methodDeclaration.Body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (declarator.Initializer?.Value == null ||
                        methodSemanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol resourceLocal ||
                        methodSemanticModel.GetOperation(declarator.Initializer.Value, cancellationToken) is not { } initializerOperation ||
                        !IsOwnedDisposableObjectCreationValue(initializerOperation, methodSemanticModel.Compilation))
                    {
                        continue;
                    }

                    var aliases = methodDeclaration.Body.DescendantNodes()
                        .OfType<VariableDeclaratorSyntax>()
                        .Where(aliasDeclarator => aliasDeclarator.SpanStart > declarator.SpanStart &&
                                                  aliasDeclarator.Initializer?.Value != null &&
                                                  methodSemanticModel.GetSymbolInfo(aliasDeclarator.Initializer.Value, cancellationToken).Symbol is ILocalSymbol initializerSymbol &&
                                                  SymbolEqualityComparer.Default.Equals(initializerSymbol, resourceLocal))
                        .Select(aliasDeclarator => methodSemanticModel.GetDeclaredSymbol(aliasDeclarator, cancellationToken))
                        .OfType<ILocalSymbol>()
                        .ToArray();
                    if (aliases.Length == 0)
                    {
                        continue;
                    }

                    var reassignment = FindLocalReassignmentAfter(
                        resourceLocal,
                        declarator.SpanStart,
                        methodDeclaration.Body,
                        methodSemanticModel,
                        cancellationToken);
                    if (reassignment == null)
                    {
                        continue;
                    }

                    if (WasAnySymbolDisposedInSpan(
                            aliases.Prepend<ISymbol>(resourceLocal),
                            methodDeclaration.Body,
                            declarator.SpanStart,
                            reassignment.SpanStart,
                            methodSemanticModel,
                            cancellationToken) ||
                        WasAnySymbolDisposedInSpan(
                            aliases,
                            methodDeclaration.Body,
                            reassignment.SpanStart,
                            methodDeclaration.Body.Span.End,
                            methodSemanticModel,
                            cancellationToken) ||
                        IsAnySymbolReturnedAfter(
                            aliases,
                            reassignment.SpanStart,
                            methodDeclaration.Body,
                            methodSemanticModel,
                            cancellationToken))
                    {
                        continue;
                    }

                    syntax = methodDeclaration;
                    symbol = aliases[0];
                    return true;
                }
            }

            syntax = null!;
            symbol = null;
            return false;
        }

        private static AssignmentExpressionSyntax? FindLocalReassignmentAfter(
            ILocalSymbol localSymbol,
            int spanStart,
            SyntaxNode searchRoot,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var assignment in searchRoot.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (assignment.SpanStart <= spanStart ||
                    semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not ILocalSymbol assignedLocal ||
                    !SymbolEqualityComparer.Default.Equals(assignedLocal, localSymbol))
                {
                    continue;
                }

                return assignment;
            }

            return null;
        }

        private static bool WasAnySymbolDisposedInSpan(
            IEnumerable<ISymbol> symbols,
            SyntaxNode searchRoot,
            int spanStart,
            int spanEnd,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var symbolSet = new HashSet<ISymbol>(symbols, SymbolEqualityComparer.Default);
            foreach (var invocation in searchRoot.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (invocation.SpanStart < spanStart ||
                    invocation.SpanStart >= spanEnd ||
                    invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    memberAccess.Name.Identifier.ValueText is not (nameof(IDisposable.Dispose) or "DisposeAsync") ||
                    semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not { } disposedSymbol)
                {
                    continue;
                }

                if (symbolSet.Contains(disposedSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAnySymbolReturnedAfter(
            IEnumerable<ISymbol> symbols,
            int spanStart,
            SyntaxNode searchRoot,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var symbolSet = new HashSet<ISymbol>(symbols, SymbolEqualityComparer.Default);
            foreach (var returnStatement in searchRoot.DescendantNodes().OfType<ReturnStatementSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (returnStatement.SpanStart <= spanStart ||
                    returnStatement.Expression == null ||
                    semanticModel.GetSymbolInfo(returnStatement.Expression, cancellationToken).Symbol is not { } returnedSymbol)
                {
                    continue;
                }

                if (symbolSet.Contains(returnedSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsResourceReleased(
            SymbolicTerm resource,
            HashSet<SymbolicTerm> releasedResources,
            PurityAnalysisState state,
            HashSet<SymbolicTerm> visitedTerms)
        {
            if (releasedResources.Contains(resource))
            {
                return true;
            }

            if (!visitedTerms.Add(resource))
            {
                return false;
            }

            foreach (var aliasTerm in EnumerateSymbolicAliasTerms(resource, state))
            {
                if (IsResourceReleased(aliasTerm, releasedResources, state, visitedTerms))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
