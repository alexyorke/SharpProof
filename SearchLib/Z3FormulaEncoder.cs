using Microsoft.Z3;

namespace SearchLib.Smt
{
    internal sealed class Z3FormulaEncoder : IDisposable
    {
        private readonly Context _context = new();
        private readonly Sort _referenceSort;
        private readonly Expr _nullReference;
        private readonly Dictionary<string, Expr> _variables = new(StringComparer.Ordinal);

        public Z3FormulaEncoder()
        {
            _referenceSort = _context.MkUninterpretedSort("Reference");
            _nullReference = _context.MkConst("null_reference", _referenceSort);
        }

        public BoolExpr EncodeCondition(SmtFormula formula)
        {
            if (formula.Kind != SmtValueKind.Bool)
            {
                throw new InvalidOperationException("Only boolean SMT formulas can be used as conditions.");
            }

            return (BoolExpr)Encode(formula);
        }

        public Solver CreateSolver(TimeSpan timeout)
        {
            var solver = _context.MkSolver();
            var parameters = _context.MkParams();
            parameters.Add("timeout", (uint)Math.Max(1, timeout.TotalMilliseconds));
            solver.Parameters = parameters;
            return solver;
        }

        public BoolExpr Negate(SmtFormula formula)
        {
            return _context.MkNot(EncodeCondition(formula));
        }

        public void Dispose()
        {
            _context.Dispose();
        }

        private Expr Encode(SmtFormula formula)
        {
            return formula switch
            {
                SmtBooleanConstant booleanConstant => booleanConstant.Value ? _context.MkTrue() : _context.MkFalse(),
                SmtIntegerConstant integerConstant => _context.MkInt(integerConstant.Value),
                SmtNullConstant => _nullReference,
                SmtVariable variable => GetOrCreateVariable(variable),
                SmtUnaryFormula unaryFormula => EncodeUnary(unaryFormula),
                SmtBinaryFormula binaryFormula => EncodeBinary(binaryFormula),
                SmtIntegerUnaryTerm integerUnaryTerm => EncodeIntegerUnary(integerUnaryTerm),
                SmtIntegerBinaryTerm integerBinaryTerm => EncodeIntegerBinary(integerBinaryTerm),
                _ => throw new InvalidOperationException("Unsupported SMT formula node."),
            };
        }

        private Expr EncodeUnary(SmtUnaryFormula formula)
        {
            return formula.Operator switch
            {
                SmtUnaryOperator.Not => _context.MkNot(EncodeCondition(formula.Operand)),
                _ => throw new InvalidOperationException("Unsupported SMT unary operator."),
            };
        }

        private Expr EncodeIntegerUnary(SmtIntegerUnaryTerm term)
        {
            return term.Operator switch
            {
                SmtIntegerUnaryOperator.Negate => _context.MkUnaryMinus(EncodeInteger(term.Operand)),
                _ => throw new InvalidOperationException("Unsupported SMT integer unary operator."),
            };
        }

        private Expr EncodeBinary(SmtBinaryFormula formula)
        {
            return formula.Operator switch
            {
                SmtBinaryOperator.And => _context.MkAnd(EncodeCondition(formula.Left), EncodeCondition(formula.Right)),
                SmtBinaryOperator.Or => _context.MkOr(EncodeCondition(formula.Left), EncodeCondition(formula.Right)),
                SmtBinaryOperator.Equal => _context.MkEq(Encode(formula.Left), Encode(formula.Right)),
                SmtBinaryOperator.NotEqual => _context.MkNot(_context.MkEq(Encode(formula.Left), Encode(formula.Right))),
                SmtBinaryOperator.LessThan => _context.MkLt(EncodeInteger(formula.Left), EncodeInteger(formula.Right)),
                SmtBinaryOperator.LessThanOrEqual => _context.MkLe(EncodeInteger(formula.Left), EncodeInteger(formula.Right)),
                SmtBinaryOperator.GreaterThan => _context.MkGt(EncodeInteger(formula.Left), EncodeInteger(formula.Right)),
                SmtBinaryOperator.GreaterThanOrEqual => _context.MkGe(EncodeInteger(formula.Left), EncodeInteger(formula.Right)),
                _ => throw new InvalidOperationException("Unsupported SMT binary operator."),
            };
        }

        private Expr EncodeIntegerBinary(SmtIntegerBinaryTerm term)
        {
            return term.Operator switch
            {
                SmtIntegerBinaryOperator.Add => _context.MkAdd(EncodeInteger(term.Left), EncodeInteger(term.Right)),
                SmtIntegerBinaryOperator.Subtract => _context.MkSub(EncodeInteger(term.Left), EncodeInteger(term.Right)),
                SmtIntegerBinaryOperator.Multiply => _context.MkMul(EncodeInteger(term.Left), EncodeInteger(term.Right)),
                _ => throw new InvalidOperationException("Unsupported SMT integer binary operator."),
            };
        }

        private ArithExpr EncodeInteger(SmtFormula formula)
        {
            if (formula.Kind != SmtValueKind.Int)
            {
                throw new InvalidOperationException("Only integer SMT formulas can be encoded as arithmetic expressions.");
            }

            return (ArithExpr)Encode(formula);
        }

        private Expr GetOrCreateVariable(SmtVariable variable)
        {
            if (_variables.TryGetValue(variable.Name, out var existing))
            {
                return existing;
            }

            Expr created = variable.Kind switch
            {
                SmtValueKind.Bool => _context.MkBoolConst(variable.Name),
                SmtValueKind.Int => _context.MkIntConst(variable.Name),
                SmtValueKind.Reference => _context.MkConst(variable.Name, _referenceSort),
                _ => throw new InvalidOperationException("Unsupported SMT variable kind."),
            };

            _variables.Add(variable.Name, created);
            return created;
        }
    }
}
