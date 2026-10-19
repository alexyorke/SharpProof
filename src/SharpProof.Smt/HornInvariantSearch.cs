namespace SharpProof.Smt;

// Proposes loop invariants with Z3's Spacer engine. Each query is a Horn
// clause: its assumptions imply its goal. An invariant relation in a goal is
// a head and elsewhere a body atom. Spacer searches the clauses' unbounded
// integer reading, where bitvector arithmetic does not wrap; only bitvector
// and boolean clauses are searched. A proposal is a guess: callers keep only
// what the proof kernel then proves over bitvectors.
public static class HornInvariantSearch
{
    public static ImmutableDictionary<IrMemberId, IrTerm> Propose(IrFactory factory, IReadOnlyList<VerificationQuery> clauses,
        IReadOnlyDictionary<IrMemberId, ImmutableArray<IrTerm>> states, uint rlimit, CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(clauses, nameof(clauses));
        ArgumentNullGuard.NotNull(states, nameof(states));
        using var context = new Context();
        using var owner = new Z3ExpressionOwner();
        var encoder = new BvEncoder(context, factory, owner, enableInvariantRelations: true);
        try
        {
            context.UpdateParamValue("rlimit", rlimit.ToString(CultureInfo.InvariantCulture));
            using var interrupt = cancellationToken.Register(context.Interrupt);
            return new Search(context, factory, owner, encoder, states, rlimit, cancellationToken).Run(clauses);
        }
        catch (Exception exception) when (exception is UnsupportedIrEncodingException or SmtResourceLimitException or Z3Exception)
        { return ImmutableDictionary<IrMemberId, IrTerm>.Empty; }
        finally
        { encoder.DisposeReferences(); }
    }

    private sealed class Search(Context context, IrFactory factory, Z3ExpressionOwner owner, BvEncoder encoder,
        IReadOnlyDictionary<IrMemberId, ImmutableArray<IrTerm>> states, uint rlimit, CancellationToken cancellationToken)
    {
        private readonly SmtQueryResourceMeter _meter = (states is null ? throw new System.ArgumentNullException(nameof(states)) : (encoder is null ? throw new System.ArgumentNullException(nameof(encoder)) : (owner is null ? throw new System.ArgumentNullException(nameof(owner)) : (factory is null ? throw new System.ArgumentNullException(nameof(factory)) : (context is null ? throw new System.ArgumentNullException(nameof(context)) : new(rlimit, cancellationToken))))));
        private readonly ImmutableDictionary<string, IrTerm>.Builder _names = ImmutableDictionary.CreateBuilder<string, IrTerm>(StringComparer.Ordinal);
        private readonly ImmutableDictionary<string, FuncDecl>.Builder _relations = ImmutableDictionary.CreateBuilder<string, FuncDecl>(StringComparer.Ordinal);
        private readonly ImmutableDictionary<string, Expr>.Builder _integers = ImmutableDictionary.CreateBuilder<string, Expr>(StringComparer.Ordinal);
        private int _unknownCount;
        private readonly ImmutableDictionary<uint, Expr>.Builder _converted = ImmutableDictionary.CreateBuilder<uint, Expr>();
        private readonly ImmutableDictionary<string, Expr>.Builder _applications = ImmutableDictionary.CreateBuilder<string, Expr>(StringComparer.Ordinal);
        private readonly ImmutableDictionary<string, (string Function, ImmutableArray<Expr> Arguments, uint Width)>.Builder _applied = ImmutableDictionary.CreateBuilder<string, (string Function, ImmutableArray<Expr> Arguments, uint Width)>(StringComparer.Ordinal);
        private readonly ImmutableList<FuncDecl>.Builder _relationOrder = ImmutableList.CreateBuilder<FuncDecl>();
        private FuncDecl? _error;

        internal ImmutableDictionary<IrMemberId, IrTerm> Run(IReadOnlyList<VerificationQuery> clauses)
        {
            clauses = RepositoryParameterGuard.NotNull(clauses, nameof(clauses));
            var proposals = ImmutableDictionary.CreateBuilder<IrMemberId, IrTerm>();
            _error = context.MkFuncDecl("error", [], context.BoolSort);
            var rules = ImmutableArray.CreateBuilder<BoolExpr>();
            foreach (var clause in clauses)
            {
                var body = clause.Assumptions.Select(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (Atom(encoder.EncodeBoolean(assumption.Predicate, _meter)))).ToImmutableList();
                foreach (var (guards, head) in Heads(encoder.EncodeBoolean(clause.Goal.Predicate, _meter)))
                { rules.Add((BoolExpr)Integer(Own(context.MkImplies(Own(context.MkAnd([.. body, .. guards])), head)))); }
            }
            if (encoder.ReferenceFacts.Count != 0 || encoder.Relations!.Count == 0)
            { return proposals.ToImmutable(); }
            using var fixedpoint = context.MkFixedpoint();
            using var parameters = context.MkParams();
            parameters.Add("engine", "spacer");
            // The host owns SIGINT and forwards cancellation through Context.Interrupt().
            using var interruptParameter = context.MkSymbol("ctrl_c");
            parameters.Add(interruptParameter, false);
            fixedpoint.Parameters = parameters;
            foreach (var relation in _relationOrder)
            { fixedpoint.RegisterRelation(relation); }
            fixedpoint.RegisterRelation(_error);
            foreach (var converted in rules.ToImmutable())
            {
                var rule = Ackermann(converted);
                var (constants, constantOrder) = CollectIntegerConstants(rule);
                fixedpoint.AddRule(constants.Count == 0 ? rule : Own(context.MkForall([.. constantOrder.Select(name => name is null ? throw new ArgumentNullException(nameof(name)) : constants[name])], rule)));
            }
            if (fixedpoint.Query(Own((BoolExpr)context.MkApp(_error))) != Status.UNSATISFIABLE)
            { return proposals.ToImmutable(); }
            foreach (var relation in encoder.RelationDeclarations)
            {
                if (!_relations.TryGetValue(relation.Value.Name.ToString(), out var integer))
                { continue; }
                var state = states.TryGetValue(relation.Key, out var known)
                    ? known : throw new ArgumentException("Every invariant relation requires a state.");
                var domain = integer.Domain;
                if (state.Length != domain.Length)
                { throw new ArgumentException("An invariant state must match its relation's arity."); }
                var arguments = state.Select((term, ordinal) =>
                    ((ordinal < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(ordinal)) : (term is null ? throw new System.ArgumentNullException(nameof(term)) : (Own(context.MkConst("state" + ordinal.ToString(CultureInfo.InvariantCulture), domain[ordinal])))))).ToImmutableArray();
                for (var ordinal = 0; ordinal < state.Length; ordinal++)
                { _names[arguments[ordinal].FuncDecl.Name.ToString()] = state[ordinal]; }
                var cover = Own(fixedpoint.GetCoverDelta(-1, integer));
                if (Translate(Own(cover.SubstituteVars(arguments.ToArray()))) is { } invariant && invariant.Type == factory.BooleanType)
                { proposals[relation.Key] = invariant; }
            }
            return proposals.ToImmutable();
        }

        // A rule whose uninterpreted applications became unknowns also
        // requires equal unknowns for equal arguments (Ackermann's reduction).
        private BoolExpr Ackermann(BoolExpr rule)
        {
            var (constants, _) = CollectIntegerConstants(rule);
            var applied = constants.Keys.Where(_applied.ContainsKey).OrderBy(name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (name), StringComparer.Ordinal).ToImmutableArray();
            var congruence = ImmutableArray.CreateBuilder<BoolExpr>();
            // A bitvector unknown keeps its signed range.
            foreach (var name in applied)
            {
                if (_applied[name].Width is var width and > 0)
                {
                    var bound = System.Numerics.BigInteger.One << (int)(width - 1);
                    var value = (ArithExpr)constants[name];
                    congruence.Add(Own(context.MkLe(Own(context.MkInt((-bound).ToString(CultureInfo.InvariantCulture))), value)));
                    congruence.Add(Own(context.MkLt(value, Own(context.MkInt(bound.ToString(CultureInfo.InvariantCulture))))));
                }
            }
            for (var left = 0; left < applied.Length; left++)
            {
                for (var right = left + 1; right < applied.Length; right++)
                {
                    var (function, arguments, _) = _applied[applied[left]];
                    var (other, otherArguments, _) = _applied[applied[right]];
                    if (function != other || arguments.Length != otherArguments.Length)
                    { continue; }
                    congruence.Add(Own(context.MkImplies(
                        Own(context.MkAnd([.. arguments.Select((argument, ordinal) => ((ordinal < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(ordinal)) : (argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (Own(context.MkEq(argument, otherArguments[ordinal]))))))])),
                        Own(context.MkEq(constants[applied[left]], constants[applied[right]])))));
                }
            }
            if (congruence.Count == 0 || !rule.IsImplies)
            { return rule; }
            return Own(context.MkImplies(Own(context.MkAnd([.. congruence, (BoolExpr)rule.Args[0]])), (BoolExpr)rule.Args[1]));
        }

        // The unbounded integer reading of a bitvector formula: arithmetic does
        // not wrap, comparisons ignore signedness and references are integers;
        // an uninterpreted application (a field read, a length) is an unknown
        // per function and arguments, and any other operation with no integer
        // reading is an unknown, the same one wherever it recurs.
        private Expr Integer(Expr expression)
        {
            if (_converted.TryGetValue(expression.Id, out var converted))
            { return converted; }
            converted = IntegerCore(expression);
            _converted[expression.Id] = converted;
            return converted;
        }

        private Expr IntegerCore(Expr expression)
        {
            _meter.Consume();
            if (expression is BitVecNum number)
            {
                var value = number.BigInteger;
                var size = (int)number.SortSize;
                if (value >= System.Numerics.BigInteger.One << (size - 1))
                { value -= System.Numerics.BigInteger.One << size; }
                return Own(context.MkInt(value.ToString(CultureInfo.InvariantCulture)));
            }
            if (IsRelation(expression))
            {
                var name = expression.FuncDecl.Name.ToString();
                var arguments = expression.Args.Select(Integer).ToImmutableArray();
                if (!_relations.TryGetValue(name, out var relation))
                {
                    relation = context.MkFuncDecl(name, [.. arguments.Select(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (argument.Sort))], context.BoolSort);
                    _relations.AddUnique(name, relation);
                    _relationOrder.Add(relation);
                }
                return Own(context.MkApp(relation, arguments.ToArray()));
            }
            if (expression.IsConst && expression.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_UNINTERPRETED)
            {
                if (expression.Sort is not (BitVecSort or UninterpretedSort))
                { return expression; }
                var name = expression.FuncDecl.Name.ToString();
                if (!_integers.TryGetValue(name, out var integer))
                {
                    integer = Own(context.MkIntConst(name));
                    _integers.AddUnique(name, integer);
                }
                return integer;
            }
            // Source rules contain only encoder-authored applications;
            // quantification and native cover retrieval happen after conversion.
            var operands = expression.Args.Select(Integer).ToImmutableArray();
            if (expression.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_UNINTERPRETED)
            {
                var function = expression.FuncDecl.Name.ToString();
                var key = function + "(" + string.Join(",", operands.Select(operand => operand is null ? throw new System.ArgumentNullException(nameof(operand)) : (operand.Id.ToString(CultureInfo.InvariantCulture)))) + ")";
                if (!_applications.TryGetValue(key, out var application))
                {
                    var name = "applied" + _applications.Count.ToString(CultureInfo.InvariantCulture);
                    application = expression.Sort.Equals(context.BoolSort) ? Own(context.MkBoolConst(name)) : Own(context.MkIntConst(name));
                    _applications.AddUnique(key, application);
                    _applied.AddUnique(name, (function, operands, expression.Sort is BitVecSort sort ? sort.Size : 0));
                }
                return application;
            }
            // Completed recursive conversions have Bool/Int result sorts;
            // equality therefore compares those converted values directly.
            var numbers = operands.OfType<ArithExpr>().ToImmutableArray();
            var arithmetic = numbers.Length == operands.Length && operands.Length != 0;
            Expr? result = expression.FuncDecl.DeclKind switch
            {
                Z3_decl_kind.Z3_OP_TRUE or Z3_decl_kind.Z3_OP_FALSE => expression,
                Z3_decl_kind.Z3_OP_AND => context.MkAnd([.. operands.Cast<BoolExpr>()]),
                Z3_decl_kind.Z3_OP_OR => context.MkOr([.. operands.Cast<BoolExpr>()]),
                Z3_decl_kind.Z3_OP_NOT => context.MkNot((BoolExpr)operands[0]),
                Z3_decl_kind.Z3_OP_IMPLIES => context.MkImplies((BoolExpr)operands[0], (BoolExpr)operands[1]),
                Z3_decl_kind.Z3_OP_EQ => context.MkEq(operands[0], operands[1]),
                Z3_decl_kind.Z3_OP_ITE => context.MkITE((BoolExpr)operands[0], operands[1], operands[2]),
                Z3_decl_kind.Z3_OP_BADD when arithmetic => context.MkAdd(numbers.ToArray()),
                Z3_decl_kind.Z3_OP_BSUB when arithmetic => context.MkSub(numbers.ToArray()),
                Z3_decl_kind.Z3_OP_BMUL when arithmetic => context.MkMul(numbers.ToArray()),
                Z3_decl_kind.Z3_OP_BNEG when arithmetic => context.MkUnaryMinus(numbers[0]),
                Z3_decl_kind.Z3_OP_SIGN_EXT when arithmetic => operands[0],
                // The signed reading of the extended value, read unsigned.
                Z3_decl_kind.Z3_OP_ZERO_EXT when arithmetic && expression.Args[0].Sort is BitVecSort extended =>
                    context.MkITE(context.MkLt(numbers[0], context.MkInt(0)),
                        context.MkAdd(numbers[0], context.MkInt((System.Numerics.BigInteger.One << (int)extended.Size).ToString(CultureInfo.InvariantCulture))),
                        numbers[0]),
                Z3_decl_kind.Z3_OP_SLEQ or Z3_decl_kind.Z3_OP_ULEQ when arithmetic => context.MkLe(numbers[0], numbers[1]),
                Z3_decl_kind.Z3_OP_SLT or Z3_decl_kind.Z3_OP_ULT when arithmetic => context.MkLt(numbers[0], numbers[1]),
                Z3_decl_kind.Z3_OP_SGEQ or Z3_decl_kind.Z3_OP_UGEQ when arithmetic => context.MkGe(numbers[0], numbers[1]),
                Z3_decl_kind.Z3_OP_SGT or Z3_decl_kind.Z3_OP_UGT when arithmetic => context.MkGt(numbers[0], numbers[1]),
                _ => null
            };
            if (result != null && (result.Sort.Equals(context.BoolSort) || result.Sort is IntSort || ReferenceEquals(result, expression)))
            { return ReferenceEquals(result, expression) ? expression : Own(result); }
            return Unknown(expression);
        }

        private Expr Unknown(Expr expression)
        {
            // Integer memoizes the completed conversion before any later use.
            // This method therefore allocates once for each unknown expression.
            expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
            var name = "unknown" + _unknownCount.ToString(CultureInfo.InvariantCulture);
            Expr fresh = expression.Sort.Equals(context.BoolSort) ? Own(context.MkBoolConst(name)) : Own(context.MkIntConst(name));
            _unknownCount++;
            return fresh;
        }

        private T Own<T>(T expression) where T : Expr
        {
            expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
            _meter.Consume();
            return owner.Own(expression);
        }

        private bool IsRelation(Expr expression)
        {
            expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
            return expression.IsApp && expression.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_UNINTERPRETED &&
                encoder.Relations!.Values.Any(relation => relation is null ? throw new System.ArgumentNullException(nameof(relation)) : (relation.Name.ToString() == expression.FuncDecl.Name.ToString()));
        }

        private bool ContainsRelation(Expr expression)
        {
            expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
            _meter.Consume();
            return IsRelation(expression) || expression.IsApp && expression.Args.Any(ContainsRelation);
        }

        // A body fact. Each relation application in it is assumed outright,
        // and holds true in the fact, which only weakens the clause.
        private BoolExpr Atom(BoolExpr fact)
        {
            if (!ContainsRelation(fact) || IsRelation(fact))
            { return fact; }
            var applicationBuilder = ImmutableList.CreateBuilder<Expr>();
            Applications(fact, applicationBuilder);
            var applications = applicationBuilder.ToImmutable();
            var holds = applications.Select(_ => (Expr)Own(context.MkTrue())).ToArray();
            return Own(context.MkAnd([(BoolExpr)Own(fact.Substitute([.. applications], holds)), .. applications.Cast<BoolExpr>()]));
        }

        private void Applications(Expr expression, ImmutableList<Expr>.Builder applications)
        {
            applications = RepositoryParameterGuard.NotNull(applications, nameof(applications));
            _meter.Consume();
            if (IsRelation(expression))
            {
                if (!applications.Any(application => application is null ? throw new System.ArgumentNullException(nameof(application)) : (application.Equals(expression))))
                { applications.Add(expression); }
                return;
            }
            if (expression.IsApp)
            {
                foreach (var argument in expression.Args)
                { Applications(argument, applications); }
            }
            return;
        }

        // The heads a goal requires, each with the guards under which it must hold.
        private IEnumerable<(ImmutableArray<BoolExpr> Guards, BoolExpr Head)> Heads(BoolExpr goal)
        {
            if (!ContainsRelation(goal))
            { return [([Own(context.MkNot(goal))], Own((BoolExpr)context.MkApp(_error!)))]; }
            if (IsRelation(goal))
            { return [([], goal)]; }
            if (goal.IsAnd)
            { return [.. goal.Args.SelectMany(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (Heads((BoolExpr)argument)))]; }
            if (goal.IsOr && goal.Args.Count(ContainsRelation) == 1)
            {
                var guards = goal.Args.Where(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (!ContainsRelation(argument)))
                    .Select(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (Own(context.MkNot((BoolExpr)argument)))).ToImmutableArray();
                return [.. Heads((BoolExpr)goal.Args.Single(ContainsRelation)).Select(head => (guards.Concat(head.Guards).ToImmutableArray(), head.Head))];
            }
            throw new UnsupportedIrEncodingException();
        }

        // Collect free constants from authored integerized rules. Every node is
        // a Bool/Int application; nonrelation applications became fresh constants.
        // Quantification is added only after this traversal.
        private (ImmutableDictionary<string, Expr> Values, ImmutableList<string> Order) CollectIntegerConstants(Expr expression)
        {
            expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
            var constants = ImmutableDictionary.CreateBuilder<string, Expr>(StringComparer.Ordinal);
            var order = ImmutableList.CreateBuilder<string>();
            CollectIntegerConstantsCore(expression, constants, order);
            return (constants.ToImmutable(), order.ToImmutable());
        }

        private void CollectIntegerConstantsCore(
            Expr expression, ImmutableDictionary<string, Expr>.Builder constants, ImmutableList<string>.Builder order)
        {
            expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
            constants = RepositoryParameterGuard.NotNull(constants, nameof(constants));
            order = RepositoryParameterGuard.NotNull(order, nameof(order));
            _meter.Consume();
            if (expression.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_UNINTERPRETED && !IsRelation(expression) &&
                expression.FuncDecl.Name.ToString() != "error")
            {
                var name = expression.FuncDecl.Name.ToString();
                if (!constants.ContainsKey(name))
                { order.Add(name); }
                constants[name] = expression;
                return;
            }
            foreach (var argument in expression.Args)
            { CollectIntegerConstantsCore(argument, constants, order); }
        }
        // The IR meaning of a cover over boolean and bitvector state.
        private IrTerm? Translate(Expr expression)
        {
            expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
            _meter.Consume();
            var arguments = expression.IsApp ? expression.Args : [];
            var literal = SmtNativeUtilities.DecodeIntegerLiteral(factory, expression, out var nativeKind);
            switch (nativeKind)
            {
                case Z3_decl_kind.Z3_OP_TRUE:
                    return factory.Boolean(true);
                case Z3_decl_kind.Z3_OP_FALSE:
                    return factory.Boolean(false);
                case Z3_decl_kind.Z3_OP_UNINTERPRETED when expression.NumArgs == 0:
                    return _names.TryGetValue(expression.FuncDecl.Name.ToString(), out var state) ? state : null;
            }
            if (literal is not null)
            { return literal; }
            ImmutableArray<IrTerm?> operands = [.. arguments.Select(Translate)];
            return SmtNativeUtilities.ReconstructHornApplication(factory, expression, operands);
        }
    }
}
