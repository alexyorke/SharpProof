using System.Collections.Immutable;

namespace SharpProof.HmInference;
// A mutable substitution graph with path compression. Each inference session owns it.
public sealed class HmType(string name, params HmType[] arguments)
{
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));
    public ImmutableArray<HmType> Arguments { get; } = (arguments ?? throw new ArgumentNullException(nameof(arguments))).ToImmutableArray();
    public HmType? Link { get; private set; }
    public bool IsVariable => Name == "?";
    public HmType Root => Link is null ? this : Link = Link.Root;

    public static HmType Variable()
    {
        return new("?");
    }

    public static HmType Function(IEnumerable<HmType> parameters, HmType result)
    {
        if (parameters is null)
        {
            throw new global::System.ArgumentNullException(nameof(parameters));
        }

        if (result is null)
        {
            throw new global::System.ArgumentNullException(nameof(result));
        }

        return new("Func", [.. parameters, result]);
    }

    public static void Unify(HmType left, HmType right)
    {
        if (left is null)
        {
            throw new ArgumentNullException(nameof(left));
        }

        if (right is null)
        {
            throw new ArgumentNullException(nameof(right));
        }

        left = left.Root;
        right = right.Root;
        if (ReferenceEquals(left, right))
        {
            return;
        }

        if (left.IsVariable)
        {
            if (Occurs(left, right))
            {
                throw new InferenceException("Infinite type (occurs check).");
            }

            left.Link = right;
            return;
        }

        if (right.IsVariable)
        {
            Unify(right, left);
            return;
        }

        if (left.Name != right.Name || left.Arguments.Length != right.Arguments.Length)
        {
            throw new InferenceException($"Conflicting types: {left.Name} and {right.Name}.");
        }

        for (var i = 0; i < left.Arguments.Length; i++)
        {
            Unify(left.Arguments[i], right.Arguments[i]);
        }
    }

    private static bool Occurs(HmType variable, HmType type)
    {
        if (variable is null)
        {
            throw new global::System.ArgumentNullException(nameof(variable));
        }

        if (type is null)
        {
            throw new global::System.ArgumentNullException(nameof(type));
        }

        return ReferenceEquals(variable, type.Root) || type.Root.Arguments.Any(t =>
        {
            if (t is null)
            {
                throw new global::System.ArgumentNullException(nameof(t));
            }

            return Occurs(variable, t);
        });
    }

    public HashSet<HmType> FreeVariables()
    {
        var found = new HashSet<HmType>();
        Visit(this);
        return found;
        void Visit(HmType node)
        {
            if (node is null)
            {
                throw new global::System.ArgumentNullException(nameof(node));
            }

            node = node.Root;
            if (node.IsVariable)
            {
                found.Add(node);
            }
            else
            {
                foreach (var argument in node.Arguments)
                {
                    Visit(argument);
                }
            }
        }
    }

    public string Format(Dictionary<HmType, string> variables)
    {
        if (variables is null)
        {
            throw new global::System.ArgumentNullException(nameof(variables));
        }

        var type = Root;
        if (type.IsVariable)
        {
            if (!variables.TryGetValue(type, out var name))
            {
                variables.Add(type, name = $"T{variables.Count + 1}");
            }

            return name;
        }

        if (type.Name == CSharpTypes.NullableReference)
        {
            return type.Arguments[0].Format(variables) + "?";
        }
        if (type.Name == "Tuple")
        {
            return $"({string.Join(", ", type.Arguments.Select(t => { if (t is null) { throw new global::System.ArgumentNullException(nameof(t)); } return t.Format(variables); }))})";
        }

        if (type.Name == "Func")
        {
            return $"global::System.Func<{string.Join(", ", type.Arguments.Select(t => { if (t is null) { throw new global::System.ArgumentNullException(nameof(t)); } return t.Format(variables); }))}>";
        }

        if (type.Name == "->")
        {
            return $"({type.Arguments[0].Format(variables)} -> {type.Arguments[1].Format(variables)})";
        }

        if (type.Name.StartsWith("[", StringComparison.Ordinal))
        {
            return type.Arguments[0].Format(variables) + type.Name;
        }

        if (type.Arguments.Length != 0)
        {
            return $"{type.Name}<{string.Join(", ", type.Arguments.Select(t => { if (t is null) { throw new global::System.ArgumentNullException(nameof(t)); } return t.Format(variables); }))}>";
        }

        return type.Name;
    }
}

public sealed class InferenceException : Exception
{
    public InferenceException()
    {
    }

    public InferenceException(string? message) : base(message)
    {
    }

    public InferenceException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}

// Quantification is explicit. Instantiation copies bound variables only: free
// variables must remain shared with the environment, including after unification.
public sealed record HmScheme(HmType Type, IReadOnlyCollection<HmType> Quantified)
{
    public HmType Type { get; } = Type ?? throw new ArgumentNullException(nameof(Type));
    public IReadOnlyCollection<HmType> Quantified { get; } = Quantified ?? throw new ArgumentNullException(nameof(Quantified));

    public static HmScheme Monomorphic(HmType type)
    {
        if (type is null)
        {
            throw new global::System.ArgumentNullException(nameof(type));
        }

        return new(type, new HashSet<HmType>());
    }

    public HashSet<HmType> FreeVariables()
    {
        var free = Type.FreeVariables();
        free.ExceptWith(Quantified);
        return free;
    }

    public static HmScheme Generalize(HmType type, IEnumerable<HmScheme> environment)
    {
        if (type is null)
        {
            throw new global::System.ArgumentNullException(nameof(type));
        }

        if (environment is null)
        {
            throw new global::System.ArgumentNullException(nameof(environment));
        }

        var quantified = type.FreeVariables();
        foreach (var scheme in environment)
        {
            quantified.ExceptWith(scheme.FreeVariables());
        }

        return new(type, quantified);
    }

    public HmType Instantiate()
    {
        var fresh = Quantified.ToDictionary(t =>
        {
            if (t is null)
            {
                throw new global::System.ArgumentNullException(nameof(t));
            }

            return t;
        }, _ => HmType.Variable());
        return Copy(Type);
        HmType Copy(HmType type)
        {
            type = type.Root;
            if (fresh.TryGetValue(type, out var replacement))
            {
                return replacement;
            }

            if (type.IsVariable)
            {
                return type;
            }

            return new(type.Name, type.Arguments.Select(Copy).ToArray());
        }
    }
}
