namespace SharpProof.Tools.Fuzz;

public sealed class FuzzCaseGenerator(int seed) {
    private static readonly Lazy<ImmutableSortedDictionary<string, ImmutableArray<ShapeRegistryEntry>>>
        RegistryByPrimaryShape =
            new(() => RegistryEntries
                .SelectMany(registryEntry => registryEntry.PrimaryShapeIds.Select(shapeId =>
                    new KeyValuePair<string, ShapeRegistryEntry>(shapeId, registryEntry)))
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToImmutableSortedDictionary(
                    group => group.Key,
                    group => group.Select(pair => pair.Value)
                        .Distinct()
                        .OrderBy(registryEntry => registryEntry.Id, StringComparer.Ordinal)
                        .ToImmutableArray(),
                    StringComparer.Ordinal));

    private static readonly Lazy<ImmutableArray<string>> OrderedGeneratorBackedShapeIds =
        new(() => RegistryByPrimaryShape.Value.Keys.ToImmutableArray());

    private readonly int _seed = seed;
    private static readonly IReadOnlyDictionary<string, Func<int, Random, string, string>> RegistryGenerators =
        new Dictionary<string, Func<int, Random, string, string>>(StringComparer.Ordinal) {
            ["PureArithmetic"] = CreateExpressionGenerator(
                "int x", "x + 1", "(x * 3) - 7", "(x / 2) + 9", "unchecked((x << 1) ^ 17)"),
            ["PureStringConcat"] = BuildPureStringConcat,
            ["PureListPattern"] = CreateExpressionGenerator(
                "int[] values", "values is [1, .., 3] ? 1 : 0", "values is [_, .. var rest] ? rest.Length : 0"),
            ["PureInterpolatedString"] = CreateExpressionGenerator(
                "int x", "$\"value={x}\".Length", "$\"sum={x + 1}\".Length"),
            ["ImpureAmbientDateTime"] = CreateExpressionGenerator("int x", "DateTime.Now.Day")
        };

    public static ImmutableArray<ShapeRegistryEntry> RegistryEntries { get; } =
        FuzzShapeRegistry.Load(RegistryGenerators);

    private static Func<int, Random, string, string> CreateExpressionGenerator(
        string parameters,
        params string[] expressions) =>
        (_, random, className) => {
            var expression = expressions.Length == 1
                ? expressions[0]
                : expressions[random.Next(expressions.Length)];
            return BuildClass(className, BuildIntMethodFromExpression(expression, random, parameters));
        };

    public FuzzCase Next(int index) {
        var shapeIds = OrderedGeneratorBackedShapeIds.Value;
        var shapeId = shapeIds[index % shapeIds.Length];
        var variant = index / shapeIds.Length;
        return GenerateForShapeCore(shapeId, variant, index);
    }

    private FuzzCase GenerateForShapeCore(string shapeId, int variant, int index) {
        if (!RegistryByPrimaryShape.Value.TryGetValue(shapeId, out var entries))
            throw new ArgumentException($"Unknown generator-backed shape '{shapeId}'.", nameof(shapeId));

        var entry = entries[variant % entries.Length];
        var entryVariant = variant / entries.Length;
        return GenerateForRegistryEntry(entry, index, entryVariant);
    }

    public FuzzCase GenerateForRegistryEntry(ShapeRegistryEntry registryEntry, int index, int variant = 0) {
        var random = CreateRandom(StableHash(index, variant, registryEntry.Id));
        var className = $"FuzzCase{index}_{registryEntry.Id}_V{variant}";
        var source = registryEntry.Build(index, random, className);
        return new FuzzCase(
            $"{index:000000}-{registryEntry.Id}",
            registryEntry.Id,
            source,
            registryEntry.AllowUnsafe ||
            source.Contains("unsafe", StringComparison.Ordinal) ||
            source.Contains("delegate*", StringComparison.Ordinal),
            registryEntry.Expectation,
            registryEntry.PrimaryShapeIds,
            registryEntry.ExpectedOperationKinds,
            registryEntry.ExpectedSyntaxKinds);
    }

    private static string BuildPureStringConcat(int index, Random random, string className) {
        const string expression = "(left + right).Length";
        return BuildClass(
            className,
            $$"""
                  [EnforcePure]
                  public int TestMethod(string left, string right)
                  {
              {{Indent(BuildReturnBody(expression, random), 8)}}
                  }
              """);
    }

    private Random CreateRandom(int index) =>
        new Random(StableHash(_seed, index, 0x51ED270B));

    private static int StableHash(int first, int second, int third) =>
        Mix(Mix(Mix(unchecked((int)2166136261), first), second), third);

    private static int StableHash(int first, int second, string third) {
        var hash = Mix(Mix(unchecked((int)2166136261), first), second);
        foreach (var character in third)
            hash = Mix(hash, character);

        return hash;
    }

    private static int Mix(int hash, int value) => unchecked((hash ^ value) * 16777619);

    private static string BuildIntMethodFromExpression(string expression, Random random, string parameterList = "int x") => $$"""
                             [EnforcePure]
                             public int TestMethod({{parameterList}})
                             {
                 {{Indent(BuildReturnBody(expression, random), 8)}}
                             }
                 """;

    private static string BuildReturnBody(string expression, Random random) => random.Next(5) switch {
        0 => $"return {expression};",
        1 => $"var value = {expression};\nreturn value;",
        2 => $"if (true)\n{{\n    return {expression};\n}}\nreturn 0;",
        3 => $"return true ? {expression} : 0;",
        _ => $"int Local() => {expression};\nreturn Local();"
    };

    private static string BuildClass(string className, string members) => $$"""
                 {{BuildUsings("System")}}

                 public class {{className}}
                 {
                 {{Indent(members, 4)}}
                 }
                 """;

    private static string BuildUsings(params string[] namespaces) =>
        string.Join("\n", namespaces
            .Append("SharpProof.Attributes")
            .Select(static value => $"using {value};"));

    private static string Indent(string text, int spaces, string? newline = null) {
        var padding = new string(' ', spaces);
        return string.Join(
            newline ?? Environment.NewLine,
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                .Select(line => line.Length == 0 ? line : padding + line));
    }
}
