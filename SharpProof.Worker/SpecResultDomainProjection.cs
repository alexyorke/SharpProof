namespace SharpProof.Worker;
#pragma warning disable IDE0055 // Compact spec projection preserves the fixed production-size ceiling.
internal readonly record struct SpecResultProjection(IrVarId? NonNullVariable, IrVarId? LengthVariable) {
    internal bool HasFacts => NonNullVariable.HasValue || LengthVariable.HasValue;
}
internal static class SpecResultDomainProjection {
    internal static bool TryCreate(IrFactory factory, ApiSpecTemplate template, IrVarId resultVariable,
        out SpecResultProjection projection, out ImmutableArray<IrTerm> evidencePredicates) {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(template);
        var kind = factory.GetTypeInfo(factory.GetVariableInfo(resultVariable).Type).Kind;
        var nullness = Project(template.Facets.Nullness.Result);
        var cardinality = Project(template.Facets.Cardinality.Result, template.Facets.Cardinality.ExactCount);
        if (nullness == NullnessValue.Bottom || cardinality.IsBottom)
            return Fail(out projection, out evidencePredicates);
        var evidence = ImmutableArray.CreateBuilder<IrTerm>(2);
        IrVarId? nonNullVariable = null;
        if (nullness != NullnessDomain.Instance.Top) {
            if (kind is not (IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence))
                return Fail(out projection, out evidencePredicates);
            nonNullVariable = CreateProxy(factory, template, resultVariable, "nonnull", factory.BooleanType);
            var nonNull = factory.Variable(nonNullVariable.Value);
            evidence.Add(nullness == NullnessValue.NonNull ? nonNull : factory.Unary(IrUnaryOperator.Not, nonNull));
        }
        IrVarId? lengthVariable = null;
        if (cardinality != SequenceCardinalityDomain.Instance.Top) {
            if (kind != IrTypeKind.Sequence || nullness != NullnessValue.NonNull)
                return Fail(out projection, out evidencePredicates);
            lengthVariable = CreateProxy(factory, template, resultVariable, "length", factory.IntegerType);
            if (!TryCreateIntervalPredicate(factory, factory.Variable(lengthVariable.Value),
                    cardinality.Length, out var predicate) ||
                predicate == null)
                return Fail(out projection, out evidencePredicates);
            evidence.Add(predicate);
        }
        projection = new SpecResultProjection(nonNullVariable, lengthVariable);
        evidencePredicates = evidence.ToImmutable();
        return true;
    }
    internal static bool TryCreateIntervalPredicate(
        IrFactory factory, IrTerm value, IntervalValue interval, out IrTerm? predicate) {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Type != factory.IntegerType || interval.IsBottom) {
            predicate = null;
            return false;
        }
        if (interval.IsSingleton) {
            predicate = factory.Binary(IrBinaryOperator.Equal, value, factory.Integer(interval.SingletonValue));
            return true;
        }
        IrTerm? Bound(long? limit, IrBinaryOperator operation) =>
            limit.HasValue ? factory.Binary(operation, value, factory.Integer(limit.Value)) : null;
        var lower = Bound(interval.LowerBound, IrBinaryOperator.GreaterThanOrEqual);
        var upper = Bound(interval.UpperBound, IrBinaryOperator.LessThanOrEqual);
        predicate = lower == null ? upper : upper == null ? lower :
            factory.Binary(IrBinaryOperator.AndAlso, lower, upper);
        return true;
    }
    internal static IrTerm Rewrite(
        IrFactory factory, IrTerm root, IReadOnlyDictionary<IrVarId, SpecResultProjection> projections) {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(projections);
        if (projections.Count == 0) return root;
        var memo = new Dictionary<IrId, IrTerm>();
        return Visit(root);
        IrTerm Visit(IrTerm term) {
            if (memo.TryGetValue(term.Id, out var cached)) return cached;
            var result = term switch {
                IrUnaryTerm unary => factory.Unary(unary.Operator, Visit(unary.Operand)),
                IrBinaryTerm binary => VisitBinary(binary),
                IrConditionalTerm conditional => factory.Conditional(Visit(conditional.Condition),
                    Visit(conditional.WhenTrue), Visit(conditional.WhenFalse)),
                IrCastTerm cast => factory.Cast(cast.Type, Visit(cast.Operand)),
                IrLengthTerm length => VisitLength(length),
                _ => term
            };
            memo.Add(term.Id, result);
            return result;
        }
        IrTerm VisitBinary(IrBinaryTerm binary) =>
            IsEquality(binary.Operator) && RewriteEquality(binary) is { } equality ? equality :
                factory.Binary(binary.Operator, Visit(binary.Left), Visit(binary.Right));
        IrTerm? RewriteEquality(IrBinaryTerm binary) {
            if (binary.Left is IrVariableTerm left && binary.Right is IrVariableTerm right &&
                left.Variable == right.Variable &&
                projections.ContainsKey(left.Variable))
                return factory.Boolean(binary.Operator == IrBinaryOperator.Equal);
            var variable = binary.Left is IrVariableTerm leftVariable && binary.Right is IrNullTerm ? leftVariable :
                binary.Right is IrVariableTerm rightVariable && binary.Left is IrNullTerm ? rightVariable : null;
            if (variable == null || !projections.TryGetValue(variable.Variable, out var projection) ||
                projection.NonNullVariable is not { } proxy)
                return null;
            var nonNull = factory.Variable(proxy);
            return binary.Operator == IrBinaryOperator.NotEqual ? nonNull :
                factory.Unary(IrUnaryOperator.Not, nonNull);
        }
        IrTerm VisitLength(IrLengthTerm length) =>
            length.Value is IrVariableTerm variable && projections.TryGetValue(variable.Variable, out var projection) &&
            projection.LengthVariable is { } proxy
                ? factory.Variable(proxy) : factory.Length(Visit(length.Value));
    }
    private static bool IsEquality(IrBinaryOperator operation) =>
        operation is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual;
    private static NullnessValue Project(SpecNullness value) => value switch {
        SpecNullness.NonNull => NullnessDomain.Instance.AssumeNonNull(NullnessDomain.Instance.Top),
        SpecNullness.Null => NullnessDomain.Instance.AssumeNull(NullnessDomain.Instance.Top),
        SpecNullness.MaybeNull or SpecNullness.Unknown or SpecNullness.NotApplicable =>
            NullnessDomain.Instance.Top,
        _ => NullnessDomain.Instance.Bottom
    };
    private static SequenceCardinalityValue Project(SpecCardinality value, long? count) =>
        value switch {
            SpecCardinality.Empty => SequenceCardinalityDomain.Instance.AssumeEmpty(SequenceCardinalityDomain.Instance.Top),
            SpecCardinality.NonEmpty => SequenceCardinalityDomain.Instance.AssumeNonEmpty(SequenceCardinalityDomain.Instance.Top),
            SpecCardinality.Exact when count.HasValue => SequenceCardinalityDomain.Instance.KnownLength(count.Value),
            SpecCardinality.Unknown or SpecCardinality.NotApplicable => SequenceCardinalityDomain.Instance.Top,
            _ => SequenceCardinalityDomain.Instance.Bottom
        };
    private static IrVarId CreateProxy(
        IrFactory factory, ApiSpecTemplate template, IrVarId result, string facet, IrTypeId type) =>
        factory.CreateVariable($"spec-result-{facet}:{template.Target.WitnessIdentifier}:" +
            result.Value.ToString(CultureInfo.InvariantCulture), type);
    private static bool Fail(out SpecResultProjection projection, out ImmutableArray<IrTerm> evidence) {
        projection = default;
        evidence = [];
        return false;
    }
}
