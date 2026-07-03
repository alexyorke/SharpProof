using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace PurelySharp.Symbolic.Ir
{
    internal static class SymbolicOwnershipFactFactory
    {
        internal static ImmutableArray<SymbolicFact> CreateFreshOwned(
            SymbolicTerm value,
            SyntaxNode source,
            string provenance,
            ISymbol? symbol = null,
            string? evidenceKey = null)
        {
            return ImmutableArray.Create(
                CreateFact(new SymbolicFreshnessAtom(value), source, provenance + ".fresh", symbol, evidenceKey),
                CreateFact(new SymbolicOwnershipAtom(value, Escaped: false), source, provenance + ".owned", symbol, evidenceKey),
                CreateFact(new SymbolicResourceLifetimeAtom(value, SymbolicResourceLifetimeState.Owned), source, provenance + ".lifetime", symbol, evidenceKey));
        }

        internal static SymbolicFact CreateAlias(
            SymbolicTerm source,
            SymbolicTerm target,
            bool mayAlias,
            SyntaxNode syntax,
            string provenance,
            ISymbol? symbol = null,
            string? evidenceKey = null)
        {
            return CreateFact(new SymbolicAliasAtom(source, target, mayAlias), syntax, provenance, symbol, evidenceKey);
        }

        internal static SymbolicFact CreateBorrow(
            SymbolicTerm owner,
            SymbolicTerm borrow,
            SymbolicBorrowKind kind,
            SyntaxNode syntax,
            string provenance,
            ISymbol? symbol = null,
            string? evidenceKey = null)
        {
            return CreateFact(new SymbolicBorrowAtom(owner, borrow, kind), syntax, provenance, symbol, evidenceKey);
        }

        internal static SymbolicFact CreateEscape(
            SymbolicTerm value,
            SymbolicEscapeKind kind,
            SyntaxNode syntax,
            string provenance,
            ISymbol? symbol = null,
            string? evidenceKey = null)
        {
            return CreateFact(new SymbolicEscapeAtom(value, kind), syntax, provenance, symbol, evidenceKey);
        }

        internal static SymbolicFact CreateReturnedOwnership(
            SymbolicTerm value,
            SyntaxNode syntax,
            string provenance,
            ISymbol? symbol = null,
            string? evidenceKey = null)
        {
            return CreateFact(new SymbolicReturnedOwnershipAtom(value), syntax, provenance, symbol, evidenceKey);
        }

        internal static SymbolicFact CreateMutation(
            SymbolicTerm target,
            bool callerVisible,
            SyntaxNode syntax,
            string provenance,
            ISymbol? symbol = null,
            string? evidenceKey = null)
        {
            return CreateFact(new SymbolicMutationAtom(target, callerVisible), syntax, provenance, symbol, evidenceKey);
        }

        internal static SymbolicFact CreateDisposal(
            SymbolicTerm resource,
            SymbolicDisposalState state,
            SyntaxNode syntax,
            string provenance,
            ISymbol? symbol = null,
            string? evidenceKey = null)
        {
            return CreateFact(new SymbolicDisposalAtom(resource, state), syntax, provenance, symbol, evidenceKey);
        }

        internal static SymbolicFact CreateResourceLifetime(
            SymbolicTerm resource,
            SymbolicResourceLifetimeState state,
            SyntaxNode syntax,
            string provenance,
            ISymbol? symbol = null,
            string? evidenceKey = null)
        {
            return CreateFact(new SymbolicResourceLifetimeAtom(resource, state), syntax, provenance, symbol, evidenceKey);
        }

        private static SymbolicFact CreateFact(
            SymbolicAtom atom,
            SyntaxNode syntax,
            string provenance,
            ISymbol? symbol,
            string? evidenceKey)
        {
            return new SymbolicFact(
                atom,
                Polarity: true,
                SymbolicFactConfidence.Exact,
                provenance,
                syntax?.Span ?? TextSpan.FromBounds(0, 0),
                symbol,
                evidenceKey);
        }
    }
}
