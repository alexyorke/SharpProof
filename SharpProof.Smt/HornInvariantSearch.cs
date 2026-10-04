namespace SharpProof.Smt;

// Proposes loop invariants with Z3's Spacer engine. Each query is a Horn
// clause: its assumptions imply its goal. An invariant relation in a goal is
// a head and elsewhere a body atom. Spacer searches the clauses' unbounded
// integer reading, where bitvector arithmetic does not wrap; only bitvector
// and boolean clauses are searched. A proposal is a guess: callers keep only
// what the proof kernel then proves over bitvectors.
public static class HornInvariantSearch
{
    public static IReadOnlyDictionary<IrMemberId, IrTerm> Propose(IrFactory factory, IReadOnlyList<VerificationQuery> clauses,
        IReadOnlyDictionary<IrMemberId, ImmutableArray<IrTerm>> states, uint rlimit, CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(clauses, nameof(clauses));
        ArgumentNullGuard.NotNull(states, nameof(states));
        using var context = new Context();
        using var owner = new Z3ExpressionOwner();
        var encoder = new BvEncoder(context, factory, owner) { Relations = [] };
        try
        {
            context.UpdateParamValue("rlimit", rlimit.ToString(CultureInfo.InvariantCulture));
            using var interrupt = cancellationToken.Register(context.Interrupt);
            return new Search(context, factory, owner, encoder, states, rlimit, cancellationToken).Run(clauses);
        }
        catch (Exception exception) when (exception is UnsupportedIrEncodingException or SmtResourceLimitException or Z3Exception)
        { return new Dictionary<IrMemberId, IrTerm>(); }
        finally
        { encoder.DisposeReferences(); }
    }

    private sealed class Search(Context context, IrFactory factory, Z3ExpressionOwner owner, BvEncoder encoder,
        IReadOnlyDictionary<IrMemberId, ImmutableArray<IrTerm>> states, uint rlimit, CancellationToken cancellationToken)
    {
        private readonly SmtQueryResourceMeter _meter = new(rlimit, cancellationToken);
        private readonly Dictionary<string, IrTerm> _names = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FuncDecl> _relations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Expr> _integers = new(StringComparer.Ordinal);
        private readonly Dictionary<uint, Expr> _unknowns = [];
        private readonly Dictionary<uint, Expr> _converted = [];
        private readonly Dictionary<string, Expr> _applications = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Function, Expr[] Arguments, uint Width)> _applied = new(StringComparer.Ordinal);
        private FuncDecl? _error;

        internal Dictionary<IrMemberId, IrTerm> Run(IReadOnlyList<VerificationQuery> clauses)
        {
            var proposals = new Dictionary<IrMemberId, IrTerm>();
            _error = context.MkFuncDecl("error", [], context.BoolSort);
            var rules = new List<BoolExpr>();
            foreach (var clause in clauses)
            {
                var body = clause.Assumptions.Select(assumption => Atom(encoder.EncodeBoolean(assumption.Predicate, _meter))).ToList();
                foreach (var (guards, head) in Heads(encoder.EncodeBoolean(clause.Goal.Predicate, _meter)))
                { rules.Add((BoolExpr)Integer(Own(context.MkImplies(Own(context.MkAnd([.. body, .. guards])), head)))); }
            }
            if (encoder.ReferenceFacts.Count != 0 || encoder.Relations!.Count == 0)
            { return proposals; }
            using var fixedpoint = context.MkFixedpoint();
            using var parameters = context.MkParams();
            parameters.Add("engine", "spacer");
            fixedpoint.Parameters = parameters;
            foreach (var relation in _relations.Values)
            { fixedpoint.RegisterRelation(relation); }
            fixedpoint.RegisterRelation(_error);
            foreach (var converted in rules)
            {
                var rule = Ackermann(converted);
                var constants = new Dictionary<string, Expr>(StringComparer.Ordinal);
                Collect(rule, constants);
                fixedpoint.AddRule(constants.Count == 0 ? rule : Own(context.MkForall([.. constants.Values], rule)));
            }
            if (fixedpoint.Query(Own((BoolExpr)context.MkApp(_error))) != Status.UNSATISFIABLE)
            { return proposals; }
            foreach (var relation in encoder.Relations)
            {
                if (!_relations.TryGetValue(relation.Value.Name.ToString(), out var integer))
                { continue; }
                var state = states[relation.Key];
                var domain = integer.Domain;
                var arguments = state.Select((term, ordinal) =>
                    Own(context.MkConst("state" + ordinal.ToString(CultureInfo.InvariantCulture), domain[ordinal]))).ToArray();
                for (var ordinal = 0; ordinal < state.Length; ordinal++)
                { _names[arguments[ordinal].FuncDecl.Name.ToString()] = state[ordinal]; }
                var cover = Own(fixedpoint.GetCoverDelta(-1, integer));
                if (Translate(Own(cover.SubstituteVars(arguments))) is { } invariant && invariant.Type == factory.BooleanType)
                { proposals[relation.Key] = invariant; }
            }
            return proposals;
        }

        // A rule whose uninterpreted applications became unknowns also
        // requires equal unknowns for equal arguments (Ackermann's reduction).
        private BoolExpr Ackermann(BoolExpr rule)
        {
            var constants = new Dictionary<string, Expr>(StringComparer.Ordinal);
            Collect(rule, constants);
            var applied = constants.Keys.Where(_applied.ContainsKey).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            var congruence = new List<BoolExpr>();
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
                        Own(context.MkAnd([.. arguments.Select((argument, ordinal) => Own(context.MkEq(argument, otherArguments[ordinal])))])),
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
                var arguments = expression.Args.Select(Integer).ToArray();
                if (!_relations.TryGetValue(name, out var relation))
                {
                    relation = context.MkFuncDecl(name, [.. arguments.Select(argument => argument.Sort)], context.BoolSort);
                    _relations.Add(name, relation);
                }
                return Own(context.MkApp(relation, arguments));
            }
            if (expression.IsConst && expression.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_UNINTERPRETED)
            {
                if (expression.Sort is not (BitVecSort or UninterpretedSort))
                { return expression; }
                var name = expression.FuncDecl.Name.ToString();
                if (!_integers.TryGetValue(name, out var integer))
                {
                    integer = Own(context.MkIntConst(name));
                    _integers.Add(name, integer);
                }
                return integer;
            }
            if (!expression.IsApp)
            { throw new UnsupportedIrEncodingException(); }
            var operands = expression.Args.Select(Integer).ToArray();
            if (expression.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_UNINTERPRETED)
            {
                var function = expression.FuncDecl.Name.ToString();
                var key = function + "(" + string.Join(",", operands.Select(operand => operand.Id.ToString(CultureInfo.InvariantCulture))) + ")";
                if (!_applications.TryGetValue(key, out var application))
                {
                    var name = "applied" + _applications.Count.ToString(CultureInfo.InvariantCulture);
                    application = expression.Sort.Equals(context.BoolSort) ? Own(context.MkBoolConst(name)) : Own(context.MkIntConst(name));
                    _applications.Add(key, application);
                    _applied.Add(name, (function, operands, expression.Sort is BitVecSort sort ? sort.Size : 0));
                }
                return application;
            }
            if (expression.FuncDecl.DeclKind is Z3_decl_kind.Z3_OP_EQ or Z3_decl_kind.Z3_OP_DISTINCT &&
                operands.Any(operand => !operand.Sort.Equals(context.BoolSort) && operand.Sort is not IntSort))
            {
                return operands.Length == 2 && operands[0].Equals(operands[1])
                    ? Own(expression.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_EQ ? context.MkTrue() : context.MkFalse())
                    : Unknown(expression);
            }
            var numbers = operands.OfType<ArithExpr>().ToArray();
            var arithmetic = numbers.Length == operands.Length && operands.Length != 0;
            Expr? result = expression.FuncDecl.DeclKind switch
            {
                Z3_decl_kind.Z3_OP_TRUE or Z3_decl_kind.Z3_OP_FALSE => expression,
                Z3_decl_kind.Z3_OP_AND => context.MkAnd([.. operands.Cast<BoolExpr>()]),
                Z3_decl_kind.Z3_OP_OR => context.MkOr([.. operands.Cast<BoolExpr>()]),
                Z3_decl_kind.Z3_OP_NOT => context.MkNot((BoolExpr)operands[0]),
                Z3_decl_kind.Z3_OP_IMPLIES => context.MkImplies((BoolExpr)operands[0], (BoolExpr)operands[1]),
                Z3_decl_kind.Z3_OP_IFF => context.MkIff((BoolExpr)operands[0], (BoolExpr)operands[1]),
                Z3_decl_kind.Z3_OP_EQ => context.MkEq(operands[0], operands[1]),
                Z3_decl_kind.Z3_OP_DISTINCT => context.MkDistinct(operands),
                Z3_decl_kind.Z3_OP_ITE => context.MkITE((BoolExpr)operands[0], operands[1], operands[2]),
                Z3_decl_kind.Z3_OP_BADD when arithmetic => context.MkAdd(numbers),
                Z3_decl_kind.Z3_OP_BSUB when arithmetic => context.MkSub(numbers),
                Z3_decl_kind.Z3_OP_BMUL when arithmetic => context.MkMul(numbers),
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
            if (_unknowns.TryGetValue(expression.Id, out var known))
            { return known; }
            var name = "unknown" + _unknowns.Count.ToString(CultureInfo.InvariantCulture);
            Expr fresh = expression.Sort.Equals(context.BoolSort) ? Own(context.MkBoolConst(name)) : Own(context.MkIntConst(name));
            _unknowns.Add(expression.Id, fresh);
            return fresh;
        }

        private T Own<T>(T expression) where T : Expr
        {
            _meter.Consume();
            return owner.Own(expression);
        }

        private bool IsRelation(Expr expression)
        {
            return expression.IsApp && expression.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_UNINTERPRETED &&
                encoder.Relations!.Values.Any(relation => relation.Name.ToString() == expression.FuncDecl.Name.ToString());
        }

        private bool ContainsRelation(Expr expression)
        {
            _meter.Consume();
            return IsRelation(expression) || expression.IsApp && expression.Args.Any(ContainsRelation);
        }

        // A body fact. Each relation application in it is assumed outright,
        // and holds true in the fact, which only weakens the clause.
        private BoolExpr Atom(BoolExpr fact)
        {
            if (!ContainsRelation(fact) || IsRelation(fact))
            { return fact; }
            var applications = new List<Expr>();
            Applications(fact, applications);
            var holds = applications.Select(_ => (Expr)Own(context.MkTrue())).ToArray();
            return Own(context.MkAnd([(BoolExpr)Own(fact.Substitute([.. applications], holds)), .. applications.Cast<BoolExpr>()]));
        }

        private void Applications(Expr expression, List<Expr> applications)
        {
            _meter.Consume();
            if (IsRelation(expression))
            {
                if (!applications.Any(application => application.Equals(expression)))
                { applications.Add(expression); }
                return;
            }
            if (expression.IsApp)
            {
                foreach (var argument in expression.Args)
                { Applications(argument, applications); }
            }
        }

        // The heads a goal requires, each with the guards under which it must hold.
        private IEnumerable<(BoolExpr[] Guards, BoolExpr Head)> Heads(BoolExpr goal)
        {
            if (!ContainsRelation(goal))
            { return [([Own(context.MkNot(goal))], Own((BoolExpr)context.MkApp(_error!)))]; }
            if (IsRelation(goal))
            { return [([], goal)]; }
            if (goal.IsAnd)
            { return [.. goal.Args.SelectMany(argument => Heads((BoolExpr)argument))]; }
            if (goal.IsImplies && !ContainsRelation(goal.Args[0]))
            { return [.. Heads((BoolExpr)goal.Args[1]).Select(head => (head.Guards.Prepend((BoolExpr)goal.Args[0]).ToArray(), head.Head))]; }
            if (goal.IsOr && goal.Args.Count(ContainsRelation) == 1)
            {
                var guards = goal.Args.Where(argument => !ContainsRelation(argument))
                    .Select(argument => Own(context.MkNot((BoolExpr)argument))).ToArray();
                return [.. Heads((BoolExpr)goal.Args.Single(ContainsRelation)).Select(head => (guards.Concat(head.Guards).ToArray(), head.Head))];
            }
            throw new UnsupportedIrEncodingException();
        }

        // Spacer reasons about bitvectors and booleans; any other symbol ends
        // the search.
        private void Collect(Expr expression, Dictionary<string, Expr> constants)
        {
            _meter.Consume();
            if (!expression.Sort.Equals(context.BoolSort) && expression.Sort is not IntSort)
            { throw new UnsupportedIrEncodingException(); }
            if (!expression.IsApp)
            {
                if (expression.IsQuantifier || expression.IsVar)
                { throw new UnsupportedIrEncodingException(); }
                return;
            }
            if (expression.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_UNINTERPRETED && !IsRelation(expression) &&
                expression.FuncDecl.Name.ToString() != "error")
            {
                if (expression.NumArgs != 0)
                { throw new UnsupportedIrEncodingException(); }
                constants[expression.FuncDecl.Name.ToString()] = expression;
                return;
            }
            foreach (var argument in expression.Args)
            { Collect(argument, constants); }
        }

        // The IR meaning of a cover over boolean and bitvector state.
        private IrTerm? Translate(Expr expression)
        {
            _meter.Consume();
            var arguments = expression.IsApp ? expression.Args : [];
            switch (expression.FuncDecl.DeclKind)
            {
                case Z3_decl_kind.Z3_OP_TRUE:
                    return factory.Boolean(true);
                case Z3_decl_kind.Z3_OP_FALSE:
                    return factory.Boolean(false);
                case Z3_decl_kind.Z3_OP_UNINTERPRETED when expression.NumArgs == 0:
                    return _names.TryGetValue(expression.FuncDecl.Name.ToString(), out var state) ? state : null;
                case Z3_decl_kind.Z3_OP_ANUM when expression is IntNum literal &&
                    literal.BigInteger >= long.MinValue && literal.BigInteger <= long.MaxValue:
                    {
                        var value = (long)literal.BigInteger;
                        return factory.Integer(factory.GetOrCreateIntegerType(value is >= int.MinValue and <= int.MaxValue ? 32 : 64, true), value);
                    }
                case Z3_decl_kind.Z3_OP_BNUM when expression is BitVecNum number && number.SortSize is 8 or 16 or 32 or 64:
                    {
                        var type = factory.GetOrCreateIntegerType((int)number.SortSize, true);
                        return factory.IntegerBits(type, number.BigInteger.IsZero ? 0UL : (ulong)(number.BigInteger & ulong.MaxValue));
                    }
            }
            IrTerm?[] operands = [.. arguments.Select(Translate)];
            if (operands.Any(operand => operand == null))
            { return null; }
            var terms = operands.Select(operand => operand!).ToArray();
            return expression.FuncDecl.DeclKind switch
            {
                Z3_decl_kind.Z3_OP_AND => Fold(IrBinaryOperator.AndAlso, terms),
                Z3_decl_kind.Z3_OP_OR => Fold(IrBinaryOperator.OrElse, terms),
                Z3_decl_kind.Z3_OP_NOT when terms.Length == 1 => factory.Unary(IrUnaryOperator.Not, terms[0]),
                Z3_decl_kind.Z3_OP_IMPLIES when terms.Length == 2 =>
                    factory.Binary(IrBinaryOperator.OrElse, factory.Unary(IrUnaryOperator.Not, terms[0]), terms[1]),
                Z3_decl_kind.Z3_OP_EQ when terms.Length == 2 => Compare(IrBinaryOperator.Equal, terms[0], terms[1], null),
                Z3_decl_kind.Z3_OP_DISTINCT when terms.Length == 2 => Compare(IrBinaryOperator.NotEqual, terms[0], terms[1], null),
                Z3_decl_kind.Z3_OP_ITE when terms.Length == 3 && terms[0].Type == factory.BooleanType => Conditional(terms[0], terms[1], terms[2]),
                Z3_decl_kind.Z3_OP_ADD => Fold(IrBinaryOperator.Add, terms),
                Z3_decl_kind.Z3_OP_MUL => Fold(IrBinaryOperator.Multiply, terms),
                Z3_decl_kind.Z3_OP_SUB => Fold(IrBinaryOperator.Subtract, terms),
                Z3_decl_kind.Z3_OP_UMINUS when terms.Length == 1 => factory.Unary(IrUnaryOperator.Negate, terms[0]),
                Z3_decl_kind.Z3_OP_LE when terms.Length == 2 => Compare(IrBinaryOperator.LessThanOrEqual, terms[0], terms[1], null),
                Z3_decl_kind.Z3_OP_LT when terms.Length == 2 => Compare(IrBinaryOperator.LessThan, terms[0], terms[1], null),
                Z3_decl_kind.Z3_OP_GE when terms.Length == 2 => Compare(IrBinaryOperator.GreaterThanOrEqual, terms[0], terms[1], null),
                Z3_decl_kind.Z3_OP_GT when terms.Length == 2 => Compare(IrBinaryOperator.GreaterThan, terms[0], terms[1], null),
                Z3_decl_kind.Z3_OP_BADD => Fold(IrBinaryOperator.Add, terms),
                Z3_decl_kind.Z3_OP_BMUL => Fold(IrBinaryOperator.Multiply, terms),
                Z3_decl_kind.Z3_OP_BSUB when terms.Length == 2 => Fold(IrBinaryOperator.Subtract, terms),
                Z3_decl_kind.Z3_OP_BNEG when terms.Length == 1 => factory.Unary(IrUnaryOperator.Negate, terms[0]),
                Z3_decl_kind.Z3_OP_SLEQ when terms.Length == 2 => Compare(IrBinaryOperator.LessThanOrEqual, terms[0], terms[1], true),
                Z3_decl_kind.Z3_OP_SLT when terms.Length == 2 => Compare(IrBinaryOperator.LessThan, terms[0], terms[1], true),
                Z3_decl_kind.Z3_OP_SGEQ when terms.Length == 2 => Compare(IrBinaryOperator.GreaterThanOrEqual, terms[0], terms[1], true),
                Z3_decl_kind.Z3_OP_SGT when terms.Length == 2 => Compare(IrBinaryOperator.GreaterThan, terms[0], terms[1], true),
                Z3_decl_kind.Z3_OP_ULEQ when terms.Length == 2 => Compare(IrBinaryOperator.LessThanOrEqual, terms[0], terms[1], false),
                Z3_decl_kind.Z3_OP_ULT when terms.Length == 2 => Compare(IrBinaryOperator.LessThan, terms[0], terms[1], false),
                Z3_decl_kind.Z3_OP_UGEQ when terms.Length == 2 => Compare(IrBinaryOperator.GreaterThanOrEqual, terms[0], terms[1], false),
                Z3_decl_kind.Z3_OP_UGT when terms.Length == 2 => Compare(IrBinaryOperator.GreaterThan, terms[0], terms[1], false),
                _ => null
            };
        }

        private IrTerm? Fold(IrBinaryOperator @operator, IrTerm[] terms)
        {
            if (terms.Length == 0)
            { return null; }
            var result = terms[0];
            foreach (var term in terms.Skip(1))
            {
                var (left, right) = Unify(result, term);
                result = factory.Binary(@operator, left, right);
            }
            return result;
        }

        // Bitvector comparisons choose their signedness; IR comparisons take
        // it from their operands' type.
        private IrTerm Compare(IrBinaryOperator @operator, IrTerm left, IrTerm right, bool? signed)
        {
            (left, right) = Unify(left, right);
            if (signed is { } kind && factory.GetTypeInfo(left.Type) is { Kind: IrTypeKind.Integer } info && info.Signed != kind)
            {
                var type = factory.GetOrCreateIntegerType(info.Width, kind);
                (left, right) = (factory.Cast(type, left), factory.Cast(type, right));
            }
            return factory.Binary(@operator, left, right);
        }

        private IrTerm Conditional(IrTerm condition, IrTerm whenTrue, IrTerm whenFalse)
        {
            var (left, right) = Unify(whenTrue, whenFalse);
            return factory.Conditional(condition, left, right);
        }

        // A literal takes the type of the term it meets.
        private (IrTerm Left, IrTerm Right) Unify(IrTerm left, IrTerm right)
        {
            return left is IrIntegerTerm && right is not IrIntegerTerm ? (Like(left, right.Type), right) : (left, Like(right, left.Type));
        }

        private IrTerm Like(IrTerm term, IrTypeId type)
        { return term.Type == type || factory.GetTypeInfo(type).Kind != IrTypeKind.Integer ? term : factory.Cast(type, term); }
    }
}
