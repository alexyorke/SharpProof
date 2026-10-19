namespace SharpProof.HmInference;
// Test oracle: immutable type terms, integer variables and explicit substitution.
// Deliberately independent of HmType/HmScheme and the production substitution graph.
internal sealed class ReferenceW
{
    private sealed record Type(int Variable, string Name, params Type[] Arguments)
    {
        public string Name { get; init; } = Name ?? throw new ArgumentNullException(nameof(Name));
        public Type[] Arguments { get; init; } = Arguments ?? throw new ArgumentNullException(nameof(Arguments));
    }

    private sealed record Scheme(Type Type, HashSet<int> Bound)
    {
        public Type Type { get; init; } = Type ?? throw new ArgumentNullException(nameof(Type));
        public HashSet<int> Bound { get; init; } = Bound ?? throw new ArgumentNullException(nameof(Bound));
    }

    private readonly Dictionary<int, Type> substitution = [];
    private int next;
    public static string? Infer(HmExpression expression)
    {
        global::System.ArgumentNullException.ThrowIfNull(expression);
        var inference = new ReferenceW();
        try
        {
            var type = inference.Expression(expression, []);
            var names = new Dictionary<int, string>();
            var text = inference.Format(type, names);
            return names.Count == 0 ? text : $"forall {string.Join(" ", names.Values)}. {text}";
        }
        catch (InferenceException)
        {
            return null;
        }
    }

    private Type Fresh()
    {
        return new(next++, "?");
    }

    private static Type Arrow(Type input, Type output)
    {
        global::System.ArgumentNullException.ThrowIfNull(input);
        global::System.ArgumentNullException.ThrowIfNull(output);
        return new(-1, "->", input, output);
    }

    private Type Apply(Type type)
    {
        global::System.ArgumentNullException.ThrowIfNull(type);
        return type.Variable >= 0 && substitution.TryGetValue(type.Variable, out var replacement) ? Apply(replacement) : type with
        {
            Arguments = type.Arguments.Select(Apply).ToArray()
        };
    }

    private HashSet<int> Free(Type type)
    {
        global::System.ArgumentNullException.ThrowIfNull(type);
        type = Apply(type);
        return type.Variable >= 0 ? [type.Variable] : type.Arguments.SelectMany(t =>
        {
            global::System.ArgumentNullException.ThrowIfNull(t);
            return Free(t);
        }).ToHashSet();
    }

    private void Unify(Type left, Type right)
    {
        left = Apply(left);
        right = Apply(right);
        if (left.Variable >= 0)
        {
            if (left.Variable == right.Variable)
            {
                return;
            }

            if (Free(right).Contains(left.Variable))
            {
                throw new InferenceException("Reference occurs check.");
            }

            substitution[left.Variable] = right;
        }
        else if (right.Variable >= 0)
        {
            Unify(right, left);
        }
        else
        {
            if (left.Name != right.Name || left.Arguments.Length != right.Arguments.Length)
            {
                throw new InferenceException("Reference type conflict.");
            }

            for (var i = 0; i < left.Arguments.Length; i++)
            {
                Unify(left.Arguments[i], right.Arguments[i]);
            }
        }
    }

    private Type Expression(HmExpression expression, Dictionary<string, Scheme> environment)
    {
        switch (expression)
        {
            case HmVariable variable:
                if (!environment.TryGetValue(variable.Name, out var scheme))
                {
                    throw new InferenceException("Reference unbound variable.");
                }

                var fresh = scheme.Bound.ToDictionary(i => i, _ => Fresh());
                return Instantiate(scheme.Type);
                Type Instantiate(Type type)
                {
                    global::System.ArgumentNullException.ThrowIfNull(type);
                    type = Apply(type);
                    return fresh.TryGetValue(type.Variable, out var replacement) ? replacement : type with
                    {
                        Arguments = type.Arguments.Select(Instantiate).ToArray()
                    };
                }

            case HmLiteral literal:
                return new(-1, literal.Kind == HmLiteralKind.Number ? "int" : "bool");
            case HmLambda lambda:
                var input = Fresh();
                var nested = new Dictionary<string, Scheme>(environment)
                {
                    [lambda.Parameter] = new(input, [])
                };
                return Arrow(input, Expression(lambda.Body, nested));
            case HmApply application:
                var function = Expression(application.Function, environment);
                var argument = Expression(application.Argument, environment);
                var output = Fresh();
                Unify(function, Arrow(argument, output));
                return Apply(output);
            case HmLet let:
                var value = Apply(Expression(let.Value, environment));
                var bound = Free(value);
                foreach (var entry in environment.Values)
                {
                    var free = Free(entry.Type);
                    free.ExceptWith(entry.Bound);
                    bound.ExceptWith(free);
                }

                var bodyEnvironment = new Dictionary<string, Scheme>(environment)
                {
                    [let.Name] = new(value, bound)
                };
                return Expression(let.Body, bodyEnvironment);
            default:
                throw new InvalidOperationException("Reference grammar exceeded.");
        }
    }

    private string Format(Type type, Dictionary<int, string> names)
    {
        global::System.ArgumentNullException.ThrowIfNull(names);
        type = Apply(type);
        if (type.Variable >= 0)
        {
            if (!names.TryGetValue(type.Variable, out var name))
            {
                names.Add(type.Variable, name = "T" + (names.Count + 1));
            }

            return name;
        }

        return type.Name == "->" ? $"({Format(type.Arguments[0], names)} -> {Format(type.Arguments[1], names)})" : type.Name;
    }
}
