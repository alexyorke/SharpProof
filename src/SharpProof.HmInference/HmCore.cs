namespace SharpProof.HmInference;

// Explicit core calculus, independent of C# overload resolution and annotations.
// Function application is curried. There are no effects, subtyping or higher ranks.
public abstract record HmExpression;
public sealed record HmVariable(string Name) : HmExpression;
public sealed record HmLambda(string Parameter, HmExpression Body) : HmExpression;
public sealed record HmApply(HmExpression Function, HmExpression Argument) : HmExpression;
public sealed record HmLet(string Name, HmExpression Value, HmExpression Body) : HmExpression;
public sealed record HmBinding(string Name, HmExpression Value);
public sealed record HmLetRec(IReadOnlyList<HmBinding> Bindings, HmExpression Body) : HmExpression;
public sealed record HmIf(HmExpression Condition, HmExpression Yes, HmExpression No) : HmExpression;
public sealed record HmTuple(params HmExpression[] Elements) : HmExpression;
public sealed record HmList(params HmExpression[] Elements) : HmExpression;
public enum HmLiteralKind { Int, Bool, String, Unit }
public sealed record HmLiteral(HmLiteralKind Kind) : HmExpression;

public static class HmCore
{
    public static HmScheme Infer(HmExpression expression, CancellationToken cancellationToken = default)
        => Infer(expression, new Dictionary<string, HmScheme>(), cancellationToken);

    public static HmScheme Infer(HmExpression expression, IReadOnlyDictionary<string, HmScheme> assumptions,
        CancellationToken cancellationToken = default)
    {
        var environment = Prelude();
        // Callers retain ownership of their assumptions. Copy the complete graph
        // together so aliases between assumptions survive without cross-run mutation.
        var copies = new Dictionary<HmType, HmType>();
        foreach (var assumption in assumptions)
            environment[assumption.Key] = new(Copy(assumption.Value.Type), assumption.Value.Quantified.Select(Copy).ToHashSet());
        var type = InferIn(expression, environment, cancellationToken);
        return HmScheme.Generalize(type, environment.Values);
        HmType Copy(HmType input)
        {
            input = input.Root;
            if (copies.TryGetValue(input, out var copy)) return copy;
            copy = input.IsVariable ? HmType.Variable() : new HmType(input.Name, input.Arguments.Select(Copy).ToArray());
            copies.Add(input, copy);
            return copy;
        }
    }

    public static string PrincipalType(HmExpression expression, CancellationToken cancellationToken = default)
    {
        var scheme = Infer(expression, cancellationToken);
        var names = new Dictionary<HmType, string>();
        var text = scheme.Type.Format(names);
        var quantified = names.Where(p => scheme.Quantified.Contains(p.Key)).Select(p => p.Value).ToArray();
        return quantified.Length == 0 ? text : $"forall {string.Join(" ", quantified)}. {text}";
    }

    private static HmType Arrow(HmType input, HmType output) => new("->", input, output);

    private static Dictionary<string, HmScheme> Prelude()
    {
        var result = new Dictionary<string, HmScheme>(StringComparer.Ordinal);
        foreach (var name in new[] { "+", "-", "*", "/" })
            result.Add(name, HmScheme.Monomorphic(Arrow(new("int"), Arrow(new("int"), new("int")))));
        result.Add("zero", HmScheme.Monomorphic(Arrow(new("int"), new("bool"))));
        var a = HmType.Variable();
        var b = HmType.Variable();
        result.Add("pair", HmScheme.Generalize(Arrow(a, Arrow(b, new("Tuple", a, b))), []));
        a = HmType.Variable();
        result.Add("nil", HmScheme.Generalize(new("List", a), []));
        a = HmType.Variable();
        result.Add("cons", HmScheme.Generalize(Arrow(a, Arrow(new("List", a), new("List", a))), []));
        a = HmType.Variable();
        result.Add("head", HmScheme.Generalize(Arrow(new("List", a), a), []));
        a = HmType.Variable();
        result.Add("tail", HmScheme.Generalize(Arrow(new("List", a), new("List", a)), []));
        a = HmType.Variable();
        result.Add("empty", HmScheme.Generalize(Arrow(new("List", a), new("bool")), []));
        a = HmType.Variable();
        result.Add("fix", HmScheme.Generalize(Arrow(Arrow(a, a), a), []));
        return result;
    }

    private static HmType InferIn(HmExpression expression, Dictionary<string, HmScheme> environment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (expression)
        {
            case HmVariable variable:
                return environment.TryGetValue(variable.Name, out var scheme) ? scheme.Instantiate()
                    : throw new InferenceException($"Unbound variable: {variable.Name}.");
            case HmLiteral literal:
                return new(literal.Kind switch { HmLiteralKind.Int => "int", HmLiteralKind.Bool => "bool", HmLiteralKind.String => "string", HmLiteralKind.Unit => "unit", _ => throw new InferenceException("Unknown literal.") });
            case HmLambda lambda:
                var parameter = HmType.Variable();
                var lambdaEnvironment = Extend(environment, lambda.Parameter, HmScheme.Monomorphic(parameter));
                return Arrow(parameter, InferIn(lambda.Body, lambdaEnvironment, cancellationToken));
            case HmApply application:
                var function = InferIn(application.Function, environment, cancellationToken);
                var argument = InferIn(application.Argument, environment, cancellationToken);
                var output = HmType.Variable();
                HmType.Unify(function, Arrow(argument, output));
                return output.Root;
            case HmLet let:
                var value = InferIn(let.Value, environment, cancellationToken);
                return InferIn(let.Body, Extend(environment, let.Name, HmScheme.Generalize(value, environment.Values)), cancellationToken);
            case HmLetRec recursive:
                if (recursive.Bindings.Count == 0 || recursive.Bindings.Select(b => b.Name).Distinct(StringComparer.Ordinal).Count() != recursive.Bindings.Count)
                    throw new InferenceException("Recursive groups need nonempty, distinct bindings.");
                var recursiveEnvironment = new Dictionary<string, HmScheme>(environment, StringComparer.Ordinal);
                var placeholders = recursive.Bindings.ToDictionary(b => b.Name, _ => HmType.Variable(), StringComparer.Ordinal);
                foreach (var binding in placeholders) recursiveEnvironment[binding.Key] = HmScheme.Monomorphic(binding.Value);
                foreach (var binding in recursive.Bindings)
                    HmType.Unify(placeholders[binding.Name], InferIn(binding.Value, recursiveEnvironment, cancellationToken));
                // Quantify only after the whole group has been solved, relative to
                // the substituted outer environment, never relative to the group.
                foreach (var binding in placeholders)
                    recursiveEnvironment[binding.Key] = HmScheme.Generalize(binding.Value, environment.Values);
                return InferIn(recursive.Body, recursiveEnvironment, cancellationToken);
            case HmIf conditional:
                HmType.Unify(InferIn(conditional.Condition, environment, cancellationToken), new("bool"));
                var yes = InferIn(conditional.Yes, environment, cancellationToken);
                HmType.Unify(yes, InferIn(conditional.No, environment, cancellationToken));
                return yes.Root;
            case HmTuple tuple:
                if (tuple.Elements.Length < 2) throw new InferenceException("Tuples need at least two elements.");
                return new("Tuple", tuple.Elements.Select(e => InferIn(e, environment, cancellationToken)).ToArray());
            case HmList list:
                var element = HmType.Variable();
                foreach (var item in list.Elements) HmType.Unify(element, InferIn(item, environment, cancellationToken));
                return new("List", element);
            default: throw new InferenceException("Unknown core expression.");
        }
    }

    private static Dictionary<string, HmScheme> Extend(Dictionary<string, HmScheme> environment, string name, HmScheme scheme)
    {
        var extended = new Dictionary<string, HmScheme>(environment, StringComparer.Ordinal) { [name] = scheme };
        return extended;
    }
}
