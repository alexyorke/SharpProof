using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PurelySharp.Analyzer.Engine.Symbolic
{
    public sealed class SymbolicInvariantService
    {
        public SymbolicInvariantSnapshot GetInvariantsAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default)
        {
            var facts = SymbolicProgramPointFacts
                .CollectAncestorReachabilityConditions(site, semanticModel, cancellationToken)
                .Concat(SymbolicProgramPointFacts
                    .CollectPriorAssignmentFacts(site, semanticModel, cancellationToken))
                .Select(static fact => fact.ToString() ?? string.Empty)
                .ToArray();

            return new SymbolicInvariantSnapshot(site.SpanStart, facts);
        }

        public SymbolicInvariantSnapshot GetForInitialEntryInvariants(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default)
        {
            var facts = SymbolicProgramPointFacts
                .CollectAncestorReachabilityConditions(forStatement, semanticModel, cancellationToken)
                .Concat(SymbolicProgramPointFacts
                    .CollectPriorAssignmentFacts(forStatement, semanticModel, cancellationToken))
                .Concat(SymbolicProgramPointFacts.CollectForInitializerFacts(forStatement, semanticModel, cancellationToken))
                .Select(static fact => fact.ToString() ?? string.Empty)
                .ToArray();

            return new SymbolicInvariantSnapshot(forStatement.SpanStart, facts);
        }
    }

    public sealed class SymbolicInvariantSnapshot
    {
        public SymbolicInvariantSnapshot(int spanStart, IReadOnlyList<string> facts)
        {
            SpanStart = spanStart;
            Facts = facts;
        }

        public int SpanStart { get; }

        public IReadOnlyList<string> Facts { get; }
    }
}
