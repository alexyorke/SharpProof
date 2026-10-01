using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker;

namespace SharpProof.Fuzz;

public sealed record TotalProgramFuzzCoverage(int Cases, int Agreements, int NativeProofs, int NativeRefutations,
    int WrappedBodies, int CheckedBodies, int FinallyBodies, int SourceCalls, int BooleanBodies,
    int LoopBodies, int ReferenceBodies, int ExceptionalExits, int TypeMask)
{
    [JsonIgnore]
    public bool HasValidCounts => Cases > 0 && Agreements >= 0 && Agreements <= Cases &&
        NativeProofs >= Agreements && NativeProofs <= Cases && NativeRefutations >= 0 && ExceptionalExits >= 0 &&
        (long)NativeRefutations + ExceptionalExits >= Agreements && (long)NativeRefutations + ExceptionalExits <= Cases &&
        WrappedBodies >= 0 && CheckedBodies >= 0 && FinallyBodies >= 0 && SourceCalls >= 0 && BooleanBodies >= 0 &&
        LoopBodies >= 0 && ReferenceBodies >= ExceptionalExits &&
        (long)WrappedBodies + CheckedBodies + FinallyBodies + SourceCalls + BooleanBodies + LoopBodies + ReferenceBodies == Cases &&
        TypeMask is > 0 and <= 8191;
    [JsonIgnore]
    public bool HasExpandedCategories => TypeMask == 8191 && WrappedBodies > 0 && CheckedBodies > 0 &&
        FinallyBodies > 0 && SourceCalls > 0 && BooleanBodies > 0 && LoopBodies > 0 && ReferenceBodies > 0 && ExceptionalExits > 0;
}

internal sealed record TotalProgramFuzzResult(TotalProgramFuzzCoverage Coverage, ImmutableArray<FuzzFailure> Failures)
{
    internal bool Passed => Coverage.HasValidCounts && Coverage.Agreements == Coverage.Cases && Failures.IsEmpty;
}

// The compiled runtime, original Total program and native callable session
// observe the same generated body. This trusted source adapter is tooling-only.
internal static class TotalProgramDifferentialOracle
{
    private static readonly string[] Types = ["sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "char", "bool"];

    internal static async Task<TotalProgramFuzzResult> RunAsync(int cases, int seed, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cases, 1);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var random = new Random(seed);
        var failures = ImmutableArray.CreateBuilder<FuzzFailure>();
        var agreements = 0;
        var nativeProofs = 0;
        var nativeRefutations = 0;
        var exceptionalExits = 0;
        var typeMask = 0;
        var bodies = new int[7];
        for (var offset = 0; offset < cases; offset += 128)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(128, cases - offset);
            var inputs = new object?[count][];
            var members = new string[count];
            var loops = new bool[count];
            for (var index = 0; index < count; index++)
            {
                var ordinal = offset + index;
                if (ordinal / Types.Length % 5 == 3)
                {
                    var reference = ReferenceCase(ordinal);
                    members[index] = reference.Source;
                    inputs[index] = reference.Inputs;
                    typeMask |= reference.Mask;
                    bodies[6]++;
                    continue;
                }
                var type = Types[ordinal % Types.Length];
                typeMask |= 1 << (ordinal % Types.Length);
                loops[index] = type != "bool" && ordinal / Types.Length % 7 == 6;
                bodies[type == "bool" ? 4 : loops[index] ? 5 : ordinal / Types.Length % 4]++;
                var number = random.NextInt64();
                inputs[index] = [Input(type, number, ordinal)];
                var body = type == "bool" ? "return !x;" : loops[index]
                    ? $"for (int i = 0; i < 2; i++) x = unchecked(({type})(x + 1)); return x;"
                    : ((ordinal / Types.Length) % 4) switch
                    {
                        0 => $"return unchecked(({type})(x + 1));",
                        1 => $"try {{ return checked(({type})(x + 1)); }} catch (System.OverflowException) {{ return x; }}",
                        2 => $"try {{ return unchecked(({type})(x + 1)); }} finally {{ x = unchecked(({type})(x + 2)); }}",
                        _ => "return Copy" + ordinal.ToString(CultureInfo.InvariantCulture) + "(x);"
                    };
                members[index] = $"public static {type} Target{ordinal}( {type} x) {{ {body} }} private static {type} Copy{ordinal}({type} value) {{ return value; }}";
            }
            var tree = CSharpSyntaxTree.ParseText("public static class TotalGenerated { " + string.Join("\n", members) + " }",
                new CSharpParseOptions(LanguageVersion.CSharp12), "total-fuzz.cs", cancellationToken: cancellationToken);
            var compilation = CSharpCompilation.Create("TotalProgramFuzz", [tree], TestMetadataReferences.SortedDistinctPlatform,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image, cancellationToken: cancellationToken);
            if (!emit.Success)
            { throw new InvalidOperationException(string.Join("\n", emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))); }
            var assemblyContext = new AssemblyLoadContext("TotalProgramFuzz", isCollectible: true);
            try
            {
                image.Position = 0;
                var runtime = assemblyContext.LoadFromStream(image).GetType("TotalGenerated")!;
                var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, tree);
                var methods = tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Where(method => method.Identifier.ValueText.StartsWith("Target", StringComparison.Ordinal)).ToArray();
                if (methods.Length != count)
                { throw new InvalidOperationException("Generated Total method count does not match its case universe."); }
                for (var index = 0; index < methods.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var method = methods[index];
                    void Fail(string detail)
                    {
                        if (failures.Count < FuzzRunner.MaximumRetainedFailures)
                        {
                            var reproducer = members[index] + "\n// inputs: " + JsonSerializer.Serialize(inputs[index]);
                            failures.Add(new(offset + index, seed, "total-program", reproducer, reproducer, detail));
                        }
                    }
                    object? expected = null;
                    Exception? expectedException = null;
                    try
                    { expected = runtime.GetMethod(method.Identifier.ValueText)!.Invoke(null, inputs[index]); }
                    catch (TargetInvocationException exception) when (exception.InnerException != null)
                    { expectedException = exception.InnerException; }
                    var factory = new IrFactory(IrExecutionSemantics.Total);
                    var context = new TotalLoweringContext(factory, (IMethodSymbol)model.GetDeclaredSymbol(method, cancellationToken)!);
                    var lower = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(method, model, cancellationToken)!, context, static _ => true, cancellationToken);
                    if (!lower.IsExact)
                    { Fail(method.Identifier.ValueText + ": incomplete " + lower.Classification.Abstention); continue; }
                    var aliases = new Dictionary<IrTypeId, Dictionary<object, IrValue>>();
                    var entries = context.Parameters.ToDictionary(parameter => parameter.Entry,
                        parameter => Value(factory, factory.GetVariableInfo(parameter.Entry).Type, inputs[index][parameter.Parameter.Ordinal], aliases));
                    var observed = expectedException == null ? Value(factory, factory.GetVariableInfo(context.Result!.Value).Type, expected, aliases) : null;
                    var original = new IrProgramInterpreter(factory).Execute(lower.Program,
                        entries, cancellationToken: cancellationToken);
                    var matches = expectedException == null
                        ? original.Status == IrProgramExecutionStatus.Returned && Equal(original.ReturnValue!, observed!)
                        : expectedException is NullReferenceException && original.Status == IrProgramExecutionStatus.Exception && original.Exception!.Kind == IrExceptionKind.NullReference;
                    if (!matches || original.ConsumedApproximation)
                    { Fail(method.Identifier.ValueText + ": compiled/original disagreement"); continue; }
                    var site = factory.CreateOperation("total-fuzz-postcondition");
                    IrTerm trueGoal = observed == null ? factory.Boolean(false) : factory.Binary(IrBinaryOperator.Equal,
                        factory.Variable(context.Result!.Value), loops[index] ? factory.Variable(context.Parameters[0].Current) : Term(factory, observed));
                    var requirements = entries.Select(entry => new PassiveContractClause(Observation(factory, entry.Key, entry.Value), factory.Boolean(true), site)).ToList();
                    if (context.Parameters.Length == 2)
                    {
                        requirements.Add(new(factory.Binary(ReferenceEquals(inputs[index][0], inputs[index][1]) ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual,
                            factory.Variable(context.Parameters[0].Entry), factory.Variable(context.Parameters[1].Entry)), factory.Boolean(true), site));
                    }
                    var candidate = new PassiveCallableCandidate(method.Identifier.ValueText, lower.Program,
                        [.. context.Parameters.Select(parameter => new PassiveParameterBinding(parameter.Entry, parameter.Current, parameter.PreState))], context.Result,
                        [.. requirements],
                        [new(trueGoal, factory.Boolean(true), site), new(factory.Unary(IrUnaryOperator.Not, trueGoal), factory.Boolean(true), site)]);
                    if (!PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason, cancellationToken))
                    { Fail(method.Identifier.ValueText + ": native admission " + reason); continue; }
                    using var solver = new PassiveCallableSolver(plan!);
                    var proven = await solver.VerifyEnsuresAsync(0, cancellationToken);
                    var refuted = await solver.VerifyEnsuresAsync(1, cancellationToken);
                    if (proven.Outcome is ProvenOutcome)
                    { nativeProofs++; }
                    if (refuted.Outcome is RefutedOutcome)
                    { nativeRefutations++; }
                    var nativeMatches = expectedException == null
                        ? refuted.Outcome is RefutedOutcome && refuted.EntryModel.Count == context.Parameters.Length && context.Parameters.All(parameter => refuted.EntryModel.ContainsKey(parameter.Entry))
                        : refuted.Outcome is ProvenOutcome && (await solver.VerifyFeasibilityAsync(cancellationToken)).Kind == PassiveCallableFeasibilityKind.NoModeledNormalReturn;
                    if (proven.Outcome is not ProvenOutcome || !nativeMatches)
                    { Fail(method.Identifier.ValueText + ": native agreement " + proven.Reason + "/" + refuted.Reason); continue; }
                    agreements++;
                    if (expectedException != null)
                    { exceptionalExits++; }
                }
            }
            finally
            { assemblyContext.Unload(); }
        }
        return new(new(cases, agreements, nativeProofs, nativeRefutations, bodies[0], bodies[1], bodies[2], bodies[3], bodies[4], bodies[5], bodies[6], exceptionalExits, typeMask), failures.ToImmutable());
    }

    private static (string Source, object?[] Inputs, int Mask) ReferenceCase(int ordinal)
    {
        var name = ordinal.ToString(CultureInfo.InvariantCulture);
        return (ordinal % 5) switch
        {
            0 => ($"public static int Target{name}(string x) {{ return x.Length; }}", [null], 1 << 10),
            1 => ($"public static int Target{name}(string x) {{ return Length{name}(x); }} private static int Length{name}(string x) {{ return x.Length; }}", ["a\ud800b"], 1 << 10),
            2 => ($"public static int Target{name}(int[] x) {{ return x.Length; }}", [new[] { 1, 2, 3 }], 1 << 11),
            3 => ($"public static int Target{name}(int[] x) {{ int seen = 0; try {{ return Length{name}(x); }} catch (System.NullReferenceException) when (++seen == 1) {{ return seen; }} finally {{ seen += 10; }} }} private static int Length{name}(int[] x) {{ return x.Length; }}", [null], 1 << 11),
            _ => Objects(name, ordinal / 5 % 2 == 0)
        };
    }

    private static (string Source, object?[] Inputs, int Mask) Objects(string name, bool same)
    {
        var shared = new object();
        return ($"public static bool Target{name}(object x, object y) {{ return x == y; }}", [shared, same ? shared : new object()], 1 << 12);
    }

    private static object Input(string type, long number, int ordinal)
    {
        var edge = ordinal / Types.Length % 3 == 0;
        var minimum = (ordinal / Types.Length % 12) is 3 or 6;
        var signedNumber = ordinal / Types.Length % 2 == 0 ? -number : number;
        return type switch
        {
            "sbyte" => edge ? minimum ? sbyte.MinValue : sbyte.MaxValue : unchecked((sbyte)signedNumber),
            "byte" => edge ? byte.MaxValue : unchecked((byte)number),
            "short" => edge ? minimum ? short.MinValue : short.MaxValue : unchecked((short)signedNumber),
            "ushort" => edge ? ushort.MaxValue : unchecked((ushort)number),
            "int" => edge ? minimum ? int.MinValue : int.MaxValue : unchecked((int)signedNumber),
            "uint" => edge ? uint.MaxValue : unchecked((uint)number),
            "long" => edge ? minimum ? long.MinValue : long.MaxValue : signedNumber,
            "ulong" => edge ? ulong.MaxValue : unchecked((ulong)number),
            "char" => edge ? char.MaxValue : unchecked((char)number),
            _ => ordinal / Types.Length % 2 == 0
        };
    }

    private static IrValue Value(IrFactory factory, IrTypeId type, object? value, Dictionary<IrTypeId, Dictionary<object, IrValue>> aliases)
    {
        if (value == null)
        { return factory.CreateNullValue(type); }
        var info = factory.GetTypeInfo(type);
        if (info.Kind is IrTypeKind.Boolean or IrTypeKind.Integer)
        {
            return value is bool flag ? factory.CreateBooleanValue(flag)
            : value is ulong wide ? factory.CreateIntegerValue(type, wide)
            : value is char character ? factory.CreateIntegerValue(type, (long)character)
            : factory.CreateIntegerValue(type, Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }
        if (!aliases.TryGetValue(type, out var typed))
        { aliases.Add(type, typed = new(ReferenceEqualityComparer.Instance)); }
        if (typed.TryGetValue(value, out var existing))
        { return existing; }
        // Null/length cases use a valid same-length representative, including
        // legal CLR inputs containing unpaired UTF-16 code units.
        var result = value is string text ? factory.CreateStringValue(new string('\0', text.Length))
            : value is Array array ? factory.CreateSequenceValue(type, array.Cast<object>().Select(element => Value(factory, info.ElementType!.Value, element, aliases)))
            : factory.CreateReferenceValue(type, value);
        typed.Add(value, result);
        return result;
    }

    private static IrTerm Observation(IrFactory factory, ScopedIrId<IrVariableTag> variable, IrValue value)
    {
        var term = factory.Variable(variable);
        if (value.Kind is IrValueKind.Boolean or IrValueKind.Integer)
        { return factory.Binary(IrBinaryOperator.Equal, term, Term(factory, value)); }
        if (value.Kind == IrValueKind.Null)
        { return factory.Binary(IrBinaryOperator.Equal, term, factory.Null(value.Type)); }
        var nonnull = factory.Binary(IrBinaryOperator.NotEqual, term, factory.Null(value.Type));
        return value.Kind == IrValueKind.Reference ? nonnull : factory.Binary(IrBinaryOperator.AndAlso, nonnull,
            factory.Binary(IrBinaryOperator.Equal, factory.Length(term), factory.Integer(value.Kind == IrValueKind.String ? value.String.Length : value.Elements.Length)));
    }

    private static IrTerm Term(IrFactory factory, IrValue value)
    {
        return value.Kind == IrValueKind.Boolean ? factory.Boolean(value.Boolean) : factory.IntegerBits(value.Type, value.IntegerBits);
    }

    private static bool Equal(IrValue left, IrValue right)
    {
        return left.Kind == right.Kind && left.Type == right.Type &&
            (left.Kind == IrValueKind.Boolean ? left.Boolean == right.Boolean : left.IntegerBits == right.IntegerBits);
    }
}
