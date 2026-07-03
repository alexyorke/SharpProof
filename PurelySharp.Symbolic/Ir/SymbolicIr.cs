using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Ir
{
    internal enum SymbolicFactConfidence
    {
        Exact,
        Approximate,
        Unsupported,
    }

    internal enum SymbolicBinaryTermOperator
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        Remainder,
    }

    internal enum SymbolicRelationOperator
    {
        Equal,
        NotEqual,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
    }

    internal enum SymbolicStringPredicateKind
    {
        Contains,
        StartsWith,
        EndsWith,
        RegexMatch,
    }

    internal enum SymbolicExceptionPreconditionKind
    {
        DivideByZero,
        NullDereference,
        ArgumentNull,
        IndexOutOfRange,
        ArgumentOutOfRange,
        NegativeLength,
        NegativeStackAllocLength,
        CheckedOverflow,
        InvalidCast,
        UnboxNull,
        NullableValueWithoutValue,
        SwitchExpressionNoMatch,
        DirectThrow,
    }

    internal enum SymbolicBorrowKind
    {
        Shared,
        Mutable,
    }

    internal enum SymbolicEscapeKind
    {
        Unknown,
        Return,
        Argument,
        Field,
        Property,
        DelegateCapture,
        CollectionElement,
        RefAlias,
    }

    internal enum SymbolicDisposalState
    {
        NotDisposed,
        Disposed,
        MaybeDisposed,
    }

    internal enum SymbolicResourceLifetimeState
    {
        Owned,
        Borrowed,
        Escaped,
        Returned,
        Released,
    }

    internal abstract record SymbolicTerm(SmtValueKind Kind);

    internal sealed record SymbolicBooleanConstantTerm(bool Value) : SymbolicTerm(SmtValueKind.Bool);

    internal sealed record SymbolicIntegerConstantTerm(long Value) : SymbolicTerm(SmtValueKind.Int);

    internal sealed record SymbolicStringConstantTerm(string Value) : SymbolicTerm(SmtValueKind.String);

    internal sealed record SymbolicNullTerm() : SymbolicTerm(SmtValueKind.Reference);

    internal sealed record SymbolicVariableTerm(string Name, SmtValueKind ValueKind) : SymbolicTerm(ValueKind);

    internal sealed record SymbolicStringContentTerm(SymbolicTerm Reference) : SymbolicTerm(SmtValueKind.String);

    internal sealed record SymbolicStringConcatTerm(SymbolicTerm Left, SymbolicTerm Right) : SymbolicTerm(SmtValueKind.String);

    internal sealed record SymbolicNullableHasValueTerm(string NullableName) : SymbolicTerm(SmtValueKind.Bool);

    internal sealed record SymbolicLengthTerm(SymbolicTerm Value) : SymbolicTerm(SmtValueKind.Int);

    internal sealed record SymbolicCountTerm(SymbolicTerm Value) : SymbolicTerm(SmtValueKind.Int);

    internal sealed record SymbolicBinaryTerm(
        SymbolicBinaryTermOperator Operator,
        SymbolicTerm Left,
        SymbolicTerm Right) : SymbolicTerm(SmtValueKind.Int);

    internal sealed record SymbolicConditionalTerm(
        SymbolicCondition Condition,
        SymbolicTerm WhenTrue,
        SymbolicTerm WhenFalse) : SymbolicTerm(WhenTrue.Kind);

    internal abstract record SymbolicAtom;

    internal sealed record SymbolicTruthAtom(SymbolicTerm Condition) : SymbolicAtom;

    internal sealed record SymbolicRelationAtom(
        SymbolicRelationOperator Operator,
        SymbolicTerm Left,
        SymbolicTerm Right) : SymbolicAtom;

    internal sealed record SymbolicStringPredicateAtom(
        SymbolicStringPredicateKind Predicate,
        SymbolicTerm Value,
        SymbolicTerm Argument,
        RegexOptions RegexOptions = RegexOptions.None) : SymbolicAtom;

    internal sealed record SymbolicBoundsAtom(
        SymbolicTerm Index,
        SymbolicTerm Length,
        bool IncludeLowerBound,
        bool IncludeUpperBound) : SymbolicAtom;

    internal sealed record SymbolicFreshnessAtom(SymbolicTerm Value) : SymbolicAtom;

    internal sealed record SymbolicOwnershipAtom(SymbolicTerm Value, bool Escaped) : SymbolicAtom;

    internal sealed record SymbolicAliasAtom(SymbolicTerm Source, SymbolicTerm Target, bool MayAlias) : SymbolicAtom;

    internal sealed record SymbolicBorrowAtom(
        SymbolicTerm Owner,
        SymbolicTerm Borrow,
        SymbolicBorrowKind Kind) : SymbolicAtom;

    internal sealed record SymbolicEscapeAtom(SymbolicTerm Value, SymbolicEscapeKind Kind) : SymbolicAtom;

    internal sealed record SymbolicReturnedOwnershipAtom(SymbolicTerm Value) : SymbolicAtom;

    internal sealed record SymbolicMutationAtom(SymbolicTerm Target, bool CallerVisible) : SymbolicAtom;

    internal sealed record SymbolicDisposalAtom(SymbolicTerm Resource, SymbolicDisposalState State) : SymbolicAtom;

    internal sealed record SymbolicResourceLifetimeAtom(
        SymbolicTerm Resource,
        SymbolicResourceLifetimeState State) : SymbolicAtom;

    internal sealed record SymbolicTypeTestAtom(SymbolicTerm Value, string TypeKey) : SymbolicAtom;

    internal sealed record SymbolicExceptionPreconditionAtom(
        SymbolicExceptionPreconditionKind Kind,
        SymbolicTerm? Subject,
        SymbolicCondition Trigger) : SymbolicAtom;

    internal sealed record SymbolicFact(
        SymbolicAtom Atom,
        bool Polarity,
        SymbolicFactConfidence Confidence,
        string Provenance,
        TextSpan SourceSpan,
        ISymbol? Symbol,
        string? EvidenceKey)
    {
        public static SymbolicFact Exact(SymbolicAtom atom, SyntaxNode node, string provenance, ISymbol? symbol = null, string? evidenceKey = null)
        {
            return new SymbolicFact(atom, true, SymbolicFactConfidence.Exact, provenance, node.Span, symbol, evidenceKey);
        }

        public SymbolicFact Negate()
        {
            return this with { Polarity = !Polarity };
        }
    }

    internal abstract record SymbolicCondition;

    internal sealed record SymbolicConstantCondition(bool Value) : SymbolicCondition;

    internal sealed record SymbolicFactCondition(SymbolicFact Fact) : SymbolicCondition;

    internal sealed record SymbolicNotCondition(SymbolicCondition Operand) : SymbolicCondition;

    internal sealed record SymbolicBinaryCondition(
        SymbolicConditionOperator Operator,
        SymbolicCondition Left,
        SymbolicCondition Right) : SymbolicCondition;

    internal enum SymbolicConditionOperator
    {
        And,
        Or,
    }

    internal sealed class SymbolicState
    {
        public SymbolicState(
            IEnumerable<SymbolicFact>? facts = null,
            IEnumerable<SymbolicCondition>? pathConditions = null)
        {
            Facts = facts?.ToImmutableArray() ?? ImmutableArray<SymbolicFact>.Empty;
            PathConditions = pathConditions?.ToImmutableArray() ?? ImmutableArray<SymbolicCondition>.Empty;
        }

        public ImmutableArray<SymbolicFact> Facts { get; }

        public ImmutableArray<SymbolicCondition> PathConditions { get; }

        public SymbolicState AddFact(SymbolicFact fact)
        {
            return new SymbolicState(Facts.Add(fact), PathConditions);
        }

        public SymbolicState AddPathCondition(SymbolicCondition condition)
        {
            return new SymbolicState(Facts, PathConditions.Add(condition));
        }
    }
}
