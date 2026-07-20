namespace SharpProof.Tools.Shared;

public sealed class ToolOptionSet<T> {
    private readonly Dictionary<string, Action<T, ToolArgumentReader, string>> _handlers =
        new(StringComparer.Ordinal);

    public ToolOptionSet<T> Add(Action<T, ToolArgumentReader, string> handler, params string[] names) {
        foreach (var name in names) _handlers.Add(name, handler);
        return this;
    }

    public void Parse(string[] arguments, T options, int startIndex = 0, Action<T, string>? positional = null) {
        var reader = new ToolArgumentReader(arguments, startIndex);
        while (reader.MoveNext()) {
            var argument = reader.Current;
            if (_handlers.TryGetValue(argument, out var handler)) handler(options, reader, argument);
            else if (positional != null) positional(options, argument);
            else throw new ArgumentException($"Unknown option '{argument}'.");
        }
    }
}

public sealed class ToolArgumentReader(string[] arguments, int startIndex = 0) {
    private int _index = startIndex - 1;

    public string Current => arguments[_index];

    public bool MoveNext() => ++_index < arguments.Length;

    public string RequiredValue(string option, string? error = null) {
        if (_index + 1 >= arguments.Length)
            throw new ArgumentException(error ?? option + " requires a value.");
        return arguments[++_index];
    }

    public int Int32(string option, int minimum = int.MinValue, string? missingError = null, string? invalidError = null) {
        var value = RequiredValue(option, missingError);
        if (int.TryParse(value, out var parsed) && parsed >= minimum) return parsed;
        var requirement = minimum switch {
            0 => "a non-negative integer value",
            1 => "a positive integer value",
            _ => "an integer value"
        };
        throw new ArgumentException(invalidError ?? option + " requires " + requirement + ".");
    }

    public TEnum DefinedEnum<TEnum>(string option, string requirement) where TEnum : struct, Enum {
        var value = RequiredValue(option).Trim();
        if (Enum.TryParse<TEnum>(value, true, out var parsed) && Enum.IsDefined(typeof(TEnum), parsed))
            return parsed;
        throw new ArgumentException(option + " " + requirement);
    }
}
