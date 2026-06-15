using Microsoft.Z3;

namespace SearchLib.Smt
{
    internal sealed class Z3FormulaEncoder : IDisposable
    {
        private readonly Context _context = new();
        private readonly Dictionary<string, Expr> _variables = new(StringComparer.Ordinal);

        public BoolExpr EncodeCondition(SmtFormula formula)
        {
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
                SmtNullConstant => _context.MkInt(0),
                SmtVariable variable => GetOrCreateVariable(variable),
                SmtUnaryFormula unaryFormula => EncodeUnary(unaryFormula),
                SmtBinaryFormula binaryFormula => EncodeBinary(binaryFormula),
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

        private Expr EncodeBinary(SmtBinaryFormula formula)
        {
            return formula.Operator switch
            {
                SmtBinaryOperator.And => _context.MkAnd(EncodeCondition(formula.Left), EncodeCondition(formula.Right)),
                SmtBinaryOperator.Or => _context.MkOr(EncodeCondition(formula.Left), EncodeCondition(formula.Right)),
                SmtBinaryOperator.Equal => _context.MkEq(Encode(formula.Left), Encode(formula.Right)),
                SmtBinaryOperator.NotEqual => _context.MkNot(_context.MkEq(Encode(formula.Left), Encode(formula.Right))),
                SmtBinaryOperator.LessThan => _context.MkLt((ArithExpr)Encode(formula.Left), (ArithExpr)Encode(formula.Right)),
                SmtBinaryOperator.LessThanOrEqual => _context.MkLe((ArithExpr)Encode(formula.Left), (ArithExpr)Encode(formula.Right)),
                SmtBinaryOperator.GreaterThan => _context.MkGt((ArithExpr)Encode(formula.Left), (ArithExpr)Encode(formula.Right)),
                SmtBinaryOperator.GreaterThanOrEqual => _context.MkGe((ArithExpr)Encode(formula.Left), (ArithExpr)Encode(formula.Right)),
                _ => throw new InvalidOperationException("Unsupported SMT binary operator."),
            };
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
                SmtValueKind.Reference => _context.MkIntConst(variable.Name),
                _ => throw new InvalidOperationException("Unsupported SMT variable kind."),
            };

            _variables.Add(variable.Name, created);
            return created;
        }
    }
}
