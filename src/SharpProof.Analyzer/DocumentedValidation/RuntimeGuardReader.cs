namespace SharpProof.Analyzer;

internal enum RejectionKind
{
    AtLeast,
    AtMost,
    Equal,
    NotEqual,
    Null,
    NotNull
}

// One comparison that rejects a parameter's value, in canonical form. For
// example "value < c" rejects as AtMost(c - 1), so each integral comparison has
// exactly one encoding and its negation is the accepted set.
internal readonly record struct RejectionAtom(RejectionKind Kind, long Constant)
{
    internal RejectionAtom Negate()
    {
        return Kind switch
        {
            RejectionKind.AtLeast => new(RejectionKind.AtMost, Constant - 1),
            RejectionKind.AtMost => new(RejectionKind.AtLeast, Constant + 1),
            RejectionKind.Equal => new(RejectionKind.NotEqual, Constant),
            RejectionKind.NotEqual => new(RejectionKind.Equal, Constant),
            RejectionKind.Null => new(RejectionKind.NotNull, 0),
            _ => new(RejectionKind.Null, 0)
        };
    }

    // The values this rejection lets through, as a fact on the parameter.
    // Rejecting "!= c" accepts exactly c. Rejecting "== c" leaves every other
    // value, which is not an interval, so it is Unknown.
    internal DeclaredValidation Accepted()
    {
        return Kind switch
        {
            RejectionKind.AtLeast => new(false, null, Constant - 1, false),
            RejectionKind.AtMost => new(false, Constant + 1, null, false),
            RejectionKind.NotEqual => new(false, Constant, Constant, false),
            RejectionKind.Null => new(true, null, null, false),
            _ => DeclaredValidation.Opaque
        };
    }

    // Builds the canonical rejection for "value <op> constant". Boundary
    // constants whose adjacent value does not exist are refused rather than
    // wrapped, so every accepted encoding is exact.
    internal static bool TryCanonical(BinaryOperatorKind kind, long constant, out RejectionAtom atom)
    {
        atom = default;
        switch (kind)
        {
            case BinaryOperatorKind.LessThan when constant > long.MinValue:
                atom = new(RejectionKind.AtMost, constant - 1);
                return true;
            case BinaryOperatorKind.LessThanOrEqual when constant < long.MaxValue:
                atom = new(RejectionKind.AtMost, constant);
                return true;
            case BinaryOperatorKind.GreaterThan when constant < long.MaxValue:
                atom = new(RejectionKind.AtLeast, constant + 1);
                return true;
            case BinaryOperatorKind.GreaterThanOrEqual when constant > long.MinValue:
                atom = new(RejectionKind.AtLeast, constant);
                return true;
            case BinaryOperatorKind.Equals:
                atom = new(RejectionKind.Equal, constant);
                return true;
            case BinaryOperatorKind.NotEquals:
                atom = new(RejectionKind.NotEqual, constant);
                return true;
            default:
                return false;
        }
    }
}

// Reads the validation a method body performs at entry: the leading statements
// that reject some inputs before any other work. Each rejection is a condition
// that throws, returns a validation result, or calls an ArgumentException
// ThrowIf method. A rejection whose meaning is not one canonical comparison on
// a single parameter leaves each parameter it mentions Unknown, and so does any
// later rejection that mentions a parameter, since this reader stops at the
// first statement it does not understand.
internal static class RuntimeGuardReader
{
    private static readonly ImmutableHashSet<string> ValidationResultMethods =
        ImmutableHashSet.Create(StringComparer.Ordinal, "BadRequest", "ValidationProblem", "UnprocessableEntity");

    private static readonly ImmutableHashSet<string> ThrowIfMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ThrowIfNull",
            "ThrowIfNullOrEmpty",
            "ThrowIfNegative",
            "ThrowIfNegativeOrZero",
            "ThrowIfZero",
            "ThrowIfGreaterThan",
            "ThrowIfGreaterThanOrEqual",
            "ThrowIfLessThan",
            "ThrowIfLessThanOrEqual",
            "ThrowIfEqual",
            "ThrowIfNotEqual");

    // The validation each parameter is known to be subject to at runtime.
    // Parameters the body never rejects are absent and have no runtime facts.
    internal static ImmutableDictionary<IParameterSymbol, DeclaredValidation> Read(
        Compilation compilation,
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        var facts = ImmutableDictionary.CreateBuilder<IParameterSymbol, DeclaredValidation>(SymbolEqualityComparer.Default);
        var block = BodyOperation(compilation, method, cancellationToken);
        if (block is null)
        {
            return facts.ToImmutable();
        }

        var parameters = method.Parameters
            .Where(static parameter => parameter.RefKind == RefKind.None)
            .ToImmutableHashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
        var statements = block.Operations;
        var index = 0;
        while (index < statements.Length && TryReadRejection(statements[index], parameters, facts))
        {
            index++;
        }

        for (var late = index; late < statements.Length; late++)
        {
            MarkLateRejections(statements[late], parameters, facts);
        }

        return facts.ToImmutable();
    }

    private static IBlockOperation? BodyOperation(
        Compilation compilation,
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        if (method.DeclaringSyntaxReferences.IsDefaultOrEmpty ||
            method.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not MethodDeclarationSyntax { Body: { } body } declaration)
        {
            return null;
        }

        return SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree)
            .GetOperation(body, cancellationToken) as IBlockOperation;
    }

    // Returns false when the statement is not a rejection, which ends the
    // leading run. A rejection that is not understood still counts as one, so
    // the reader continues past it with its parameters marked Unknown.
    private static bool TryReadRejection(
        IOperation statement,
        ImmutableHashSet<IParameterSymbol> parameters,
        ImmutableDictionary<IParameterSymbol, DeclaredValidation>.Builder facts)
    {
        switch (Strip(statement))
        {
            case IConditionalOperation { WhenFalse: null } conditional when RejectsOnEntry(conditional.WhenTrue):
                foreach (var disjunct in Disjuncts(conditional.Condition))
                {
                    ReadDisjunct(disjunct, parameters, facts);
                }

                return true;

            case IExpressionStatementOperation { Operation: IInvocationOperation invocation }
                when IsThrowIf(invocation):
                if (TryReadThrowIf(invocation, out var subject, out var atom) && subject is not null && TypeFits(subject, atom))
                {
                    Accept(facts, subject, atom.Accepted());
                }
                else
                {
                    MarkMentioned(invocation, parameters, facts);
                }

                return true;

            case IThrowOperation:
                // An unconditional throw ends the body, so nothing about any
                // parameter is established by a guard here.
                foreach (var parameter in parameters)
                {
                    Accept(facts, parameter, DeclaredValidation.Opaque);
                }

                return true;

            default:
                return false;
        }
    }

    private static void ReadDisjunct(
        IOperation disjunct,
        ImmutableHashSet<IParameterSymbol> parameters,
        ImmutableDictionary<IParameterSymbol, DeclaredValidation>.Builder facts)
    {
        var mentioned = ParametersMentioned(disjunct, parameters);
        if (mentioned.IsEmpty)
        {
            // A rejection on a value that is no parameter, such as a field, does
            // not restrict any parameter's entry value.
            return;
        }

        if (mentioned.Length == 1 &&
            TryRead(disjunct, out var subject, out var atom) &&
            subject is not null &&
            SymbolEqualityComparer.Default.Equals(subject, mentioned[0]) &&
            TypeFits(subject, atom))
        {
            Accept(facts, subject, atom.Accepted());
            return;
        }

        foreach (var parameter in mentioned)
        {
            Accept(facts, parameter, DeclaredValidation.Opaque);
        }
    }

    private static void MarkLateRejections(
        IOperation statement,
        ImmutableHashSet<IParameterSymbol> parameters,
        ImmutableDictionary<IParameterSymbol, DeclaredValidation>.Builder facts)
    {
        if (ContainsRejection(statement))
        {
            MarkMentioned(statement, parameters, facts);
        }
    }

    private static void MarkMentioned(
        IOperation operation,
        ImmutableHashSet<IParameterSymbol> parameters,
        ImmutableDictionary<IParameterSymbol, DeclaredValidation>.Builder facts)
    {
        foreach (var parameter in ParametersMentioned(operation, parameters))
        {
            Accept(facts, parameter, DeclaredValidation.Opaque);
        }
    }

    private static void Accept(
        ImmutableDictionary<IParameterSymbol, DeclaredValidation>.Builder facts,
        IParameterSymbol parameter,
        DeclaredValidation fact)
    {
        var current = facts.TryGetValue(parameter, out var existing) ? existing : DeclaredValidation.None;
        facts[parameter] = current.And(fact);
    }

    private static bool TypeFits(IParameterSymbol parameter, RejectionAtom atom)
    {
        return atom.Kind is RejectionKind.Null or RejectionKind.NotNull
            ? parameter.Type.IsReferenceType
            : IntegralDomain.TryGet(parameter.Type, out _, out _);
    }

    private static bool IsThrowIf(IInvocationOperation invocation)
    {
        return NamespaceNames.Of(invocation.TargetMethod.ContainingNamespace) == "System" &&
            invocation.TargetMethod.ContainingType is { Name: "ArgumentNullException" or "ArgumentOutOfRangeException" or "ArgumentException" } &&
            ThrowIfMethods.Contains(invocation.TargetMethod.Name);
    }

    private static bool TryReadThrowIf(
        IInvocationOperation invocation,
        out IParameterSymbol? subject,
        out RejectionAtom atom)
    {
        subject = null;
        atom = default;
        if (invocation.Arguments.IsDefaultOrEmpty ||
            Strip(invocation.Arguments[0].Value) is not IParameterReferenceOperation value)
        {
            return false;
        }

        subject = value.Parameter;
        var name = invocation.TargetMethod.Name;
        switch (name)
        {
            case "ThrowIfNull" or "ThrowIfNullOrEmpty":
                atom = new(RejectionKind.Null, 0);
                return true;
            case "ThrowIfNegative":
                return RejectionAtom.TryCanonical(BinaryOperatorKind.LessThan, 0, out atom);
            case "ThrowIfNegativeOrZero":
                return RejectionAtom.TryCanonical(BinaryOperatorKind.LessThanOrEqual, 0, out atom);
            case "ThrowIfZero":
                return RejectionAtom.TryCanonical(BinaryOperatorKind.Equals, 0, out atom);
        }

        if (invocation.Arguments.Length != 2 || !TryConstant(invocation.Arguments[1].Value, out var constant))
        {
            return false;
        }

        var kind = name switch
        {
            "ThrowIfGreaterThan" => BinaryOperatorKind.GreaterThan,
            "ThrowIfGreaterThanOrEqual" => BinaryOperatorKind.GreaterThanOrEqual,
            "ThrowIfLessThan" => BinaryOperatorKind.LessThan,
            "ThrowIfLessThanOrEqual" => BinaryOperatorKind.LessThanOrEqual,
            "ThrowIfEqual" => BinaryOperatorKind.Equals,
            _ => BinaryOperatorKind.NotEquals
        };
        return RejectionAtom.TryCanonical(kind, constant, out atom);
    }

    // Reads one rejecting disjunct as a single canonical comparison on a
    // parameter. Anything else, such as a conjunction, a call, or a member access
    // on the parameter, is not a canonical rejection.
    private static bool TryRead(IOperation operation, out IParameterSymbol? subject, out RejectionAtom atom)
    {
        subject = null;
        atom = default;
        switch (Strip(operation))
        {
            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } negation:
                if (!TryRead(negation.Operand, out subject, out var inner))
                {
                    return false;
                }

                atom = inner.Negate();
                return true;

            case IIsPatternOperation { Pattern: IConstantPatternOperation constant } isNull
                when Parameter(isNull.Value) is { } nullSubject && IsNullLiteral(constant.Value):
                subject = nullSubject;
                atom = new(RejectionKind.Null, 0);
                return true;

            case IIsPatternOperation { Pattern: INegatedPatternOperation { Pattern: IConstantPatternOperation constant } } isNotNull
                when Parameter(isNotNull.Value) is { } notNullSubject && IsNullLiteral(constant.Value):
                subject = notNullSubject;
                atom = new(RejectionKind.NotNull, 0);
                return true;

            case IBinaryOperation comparison:
                return TryReadComparison(comparison, out subject, out atom);

            default:
                return false;
        }
    }

    private static bool TryReadComparison(IBinaryOperation comparison, out IParameterSymbol? subject, out RejectionAtom atom)
    {
        subject = null;
        atom = default;
        var left = Strip(comparison.LeftOperand);
        var right = Strip(comparison.RightOperand);
        IOperation other;
        bool subjectOnLeft;
        if (left is IParameterReferenceOperation leftParameter)
        {
            subject = leftParameter.Parameter;
            other = right;
            subjectOnLeft = true;
        }
        else if (right is IParameterReferenceOperation rightParameter)
        {
            subject = rightParameter.Parameter;
            other = left;
            subjectOnLeft = false;
        }
        else
        {
            return false;
        }

        if (IsNullLiteral(other))
        {
            atom = comparison.OperatorKind switch
            {
                BinaryOperatorKind.Equals => new(RejectionKind.Null, 0),
                BinaryOperatorKind.NotEquals => new(RejectionKind.NotNull, 0),
                _ => default
            };
            return atom.Kind is RejectionKind.Null or RejectionKind.NotNull;
        }

        if (!TryConstant(other, out var constant))
        {
            return false;
        }

        var kind = subjectOnLeft ? comparison.OperatorKind : Mirror(comparison.OperatorKind);
        return RejectionAtom.TryCanonical(kind, constant, out atom);
    }

    // "c < value" is "value > c" once the operands are swapped.
    private static BinaryOperatorKind Mirror(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThan,
            BinaryOperatorKind.LessThanOrEqual => BinaryOperatorKind.GreaterThanOrEqual,
            BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThan,
            BinaryOperatorKind.GreaterThanOrEqual => BinaryOperatorKind.LessThanOrEqual,
            _ => kind
        };
    }

    // A rejection written as "a || b" rejects when either holds, so each
    // disjunct is a separate rejection.
    private static ImmutableArray<IOperation> Disjuncts(IOperation condition)
    {
        var builder = ImmutableArray.CreateBuilder<IOperation>();
        CollectDisjuncts(condition, builder);
        return builder.ToImmutable();
    }

    private static void CollectDisjuncts(IOperation condition, ImmutableArray<IOperation>.Builder builder)
    {
        var stripped = Strip(condition);
        if (stripped is IBinaryOperation { OperatorKind: BinaryOperatorKind.ConditionalOr or BinaryOperatorKind.Or } disjunction)
        {
            CollectDisjuncts(disjunction.LeftOperand, builder);
            CollectDisjuncts(disjunction.RightOperand, builder);
            return;
        }

        builder.Add(stripped);
    }

    // A branch that rejects: a block of one such statement, a throw, or a return
    // of a validation result.
    private static bool RejectsOnEntry(IOperation branch)
    {
        switch (Strip(branch))
        {
            case IBlockOperation { Operations.Length: 1 } block:
                return RejectsOnEntry(block.Operations[0]);
            case IThrowOperation:
                return true;
            case IReturnOperation { ReturnedValue: { } value }:
                return IsValidationResult(value);
            default:
                return false;
        }
    }

    private static bool IsValidationResult(IOperation value)
    {
        return Strip(value) is IInvocationOperation invocation &&
            ValidationResultMethods.Contains(invocation.TargetMethod.Name) &&
            NamespaceNames.Of(invocation.TargetMethod.ContainingNamespace).StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal);
    }

    private static bool ContainsRejection(IOperation operation)
    {
        switch (Strip(operation))
        {
            case IThrowOperation:
                return true;
            case IReturnOperation { ReturnedValue: { } value } when IsValidationResult(value):
                return true;
            case IExpressionStatementOperation { Operation: IInvocationOperation invocation } when IsThrowIf(invocation):
                return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (ContainsRejection(child))
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<IParameterSymbol> ParametersMentioned(
        IOperation operation,
        ImmutableHashSet<IParameterSymbol> parameters)
    {
        var builder = ImmutableArray.CreateBuilder<IParameterSymbol>();
        CollectParameters(operation, parameters, builder);
        return builder.ToImmutable().Distinct<IParameterSymbol>(SymbolEqualityComparer.Default).ToImmutableArray();
    }

    private static void CollectParameters(
        IOperation operation,
        ImmutableHashSet<IParameterSymbol> parameters,
        ImmutableArray<IParameterSymbol>.Builder builder)
    {
        if (operation is IParameterReferenceOperation reference && parameters.Contains(reference.Parameter))
        {
            builder.Add(reference.Parameter);
        }

        foreach (var child in operation.ChildOperations)
        {
            CollectParameters(child, parameters, builder);
        }
    }

    private static IParameterSymbol? Parameter(IOperation operation)
    {
        return Strip(operation) is IParameterReferenceOperation reference ? reference.Parameter : null;
    }

    private static bool IsNullLiteral(IOperation operation)
    {
        return Strip(operation) is ILiteralOperation literal &&
            (literal.Type is null || literal.ConstantValue is { HasValue: true, Value: null });
    }

    private static bool TryConstant(IOperation operation, out long value)
    {
        var constant = Strip(operation).ConstantValue;
        value = 0;
        if (!constant.HasValue || constant.Value is null)
        {
            return false;
        }

        switch (constant.Value)
        {
            case sbyte or byte or short or ushort or int or uint or long:
                value = Convert.ToInt64(constant.Value, CultureInfo.InvariantCulture);
                return true;
            case ulong unsigned when unsigned <= long.MaxValue:
                value = (long)unsigned;
                return true;
            default:
                return false;
        }
    }

    // Implicit conversions and parentheses do not change the value compared.
    private static IOperation Strip(IOperation operation)
    {
        while (true)
        {
            if (operation is IConversionOperation { IsImplicit: true } conversion)
            {
                operation = conversion.Operand;
            }
            else if (operation is IParenthesizedOperation parenthesized)
            {
                operation = parenthesized.Operand;
            }
            else
            {
                return operation;
            }
        }
    }
}
