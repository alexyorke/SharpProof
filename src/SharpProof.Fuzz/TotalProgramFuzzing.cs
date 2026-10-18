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
    int LoopBodies, int ReferenceBodies, int ExceptionalExits, int TypeMask, int ArrayReadBodies)
{
    [JsonIgnore]
    public bool HasValidCounts => Cases > 0 && Agreements >= 0 && Agreements <= Cases &&
        NativeProofs >= Agreements && NativeProofs <= Cases && NativeRefutations >= 0 && ExceptionalExits >= 0 &&
        (long)NativeRefutations + ExceptionalExits >= Agreements && (long)NativeRefutations + ExceptionalExits <= Cases &&
        WrappedBodies >= 0 && CheckedBodies >= 0 && FinallyBodies >= 0 && SourceCalls >= 0 && BooleanBodies >= 0 &&
        LoopBodies >= 0 && ReferenceBodies >= ExceptionalExits && ArrayReadBodies >= 0 && ArrayReadBodies <= ReferenceBodies &&
        (long)WrappedBodies + CheckedBodies + FinallyBodies + SourceCalls + BooleanBodies + LoopBodies + ReferenceBodies == Cases &&
        TypeMask is > 0 and <= 8191;
    [JsonIgnore]
    public bool HasExpandedCategories => TypeMask == 8191 && WrappedBodies > 0 && CheckedBodies > 0 &&
        FinallyBodies > 0 && SourceCalls > 0 && BooleanBodies > 0 && LoopBodies > 0 && ReferenceBodies > 0 && ExceptionalExits > 0 && ArrayReadBodies > 0;
}

internal sealed record TotalProgramFuzzResult(TotalProgramFuzzCoverage Coverage, ImmutableArray<FuzzFailure> Failures)
{
    internal bool Passed => Coverage.HasValidCounts && Coverage.Agreements == Coverage.Cases && Failures.IsEmpty;
    public TotalProgramFuzzCoverage Coverage { get; init; } = Coverage ?? throw new System.ArgumentNullException(nameof(Coverage));
    public ImmutableArray<FuzzFailure> Failures { get; init; } = Failures.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Failures)) : Failures;
}

// The compiled runtime, original Total program and native callable session
// observe the same generated body. This trusted source adapter is tooling-only.
internal static class TotalProgramDifferentialOracle
{
    internal static readonly string[] Types = ["sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "char", "bool"];

    internal static async Task<TotalProgramFuzzResult> RunAsync(int cases, int seed, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cases, 1);
        var replay = new TotalProgramSourceReplay(seed);
        var random = new Random(seed);
        var arrayReads = 0;
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
                    var reference = ReferenceCase(ordinal, random);
                    members[index] = reference.Source;
                    inputs[index] = reference.Inputs;
                    typeMask |= reference.Mask;
                    bodies[6]++;
                    if (ordinal % 5 == 2)
                    { arrayReads++; }
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
            var batch = members.Select((member, index) =>
                ((index < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(index)) : (member is null ? throw new System.ArgumentNullException(nameof(member)) : (new TotalProgramSourceCase(member, inputs[index], loops[index]))))).ToArray();
            await replay.CompareBatchAsync(batch, offset, cancellationToken);
        }
        var result = replay.Result;
        return new(new(cases, result.Agreements, result.NativeProofs, result.NativeRefutations, bodies[0], bodies[1], bodies[2], bodies[3], bodies[4], bodies[5], bodies[6], result.ExceptionalExits, typeMask, arrayReads), result.Failures);
    }

    private static (string Source, object?[] Inputs, int Mask) ReferenceCase(int ordinal, Random random)
    {
        RepositoryParameterGuard.RequireInitialized(ordinal < 0, nameof(ordinal), "Value must be initialized.");

        random = RepositoryParameterGuard.NotNull(random, nameof(random));
        var name = ordinal.ToString(CultureInfo.InvariantCulture);
        return (ordinal % 5) switch
        {
            0 => ($"public static int Target{name}(string x) {{ return x.Length; }}", [null], 1 << 10),
            1 => ($"public static int Target{name}(string x) {{ return Length{name}(x); }} private static int Length{name}(string x) {{ return x.Length; }}", ["a\ud800b"], 1 << 10),
            2 => ($"public static int Target{name}(int[] x, uint index) {{ return x[index]; }}",
                [new[] { unchecked((int)random.NextInt64()), unchecked((int)random.NextInt64()), unchecked((int)random.NextInt64()) }, (uint)random.Next(3)], (1 << 11) | (1 << 5)),
            3 => ($"public static int Target{name}(int[] x) {{ int seen = 0; try {{ return Length{name}(x); }} catch (System.NullReferenceException) when (++seen == 1) {{ return seen; }} finally {{ seen += 10; }} }} private static int Length{name}(int[] x) {{ return x.Length; }}", [null], 1 << 11),
            _ => Objects(name, ordinal / 5 % 2 == 0)
        };
    }

    private static (string Source, object?[] Inputs, int Mask) Objects(string name, bool same)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        var shared = new object();
        return ($"public static bool Target{name}(object x, object y) {{ return x == y; }}", [shared, same ? shared : new object()], 1 << 12);
    }

    internal static object Input(string type, long number, int ordinal)
    {
        RepositoryParameterGuard.RequireInitialized(ordinal < 0, nameof(ordinal), "Value must be initialized.");

        type = RepositoryParameterGuard.NotNull(type, nameof(type));
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
            "ulong" => edge ? ulong.MaxValue : unchecked((ulong)signedNumber),
            "char" => edge ? char.MaxValue : unchecked((char)number),
            _ => ordinal / Types.Length % 2 == 0
        };
    }

    internal static IrValue Value(IrFactory factory, IrTypeId type, object? value, Dictionary<IrTypeId, Dictionary<object, IrValue>> aliases)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        aliases = RepositoryParameterGuard.NotNull(aliases, nameof(aliases));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
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
            : value is Array array ? factory.CreateSequenceValue(type, array.Cast<object>().Select(element => element is null ? throw new System.ArgumentNullException(nameof(element)) : (Value(factory, info.ElementType!.Value, element, aliases))))
            : factory.CreateReferenceValue(type, value);
        typed.Add(value, result);
        return result;
    }

    internal static IrTerm Observation(IrFactory factory, ScopedIrId<IrVariableTag> variable, IrValue value)
    {
        RepositoryParameterGuard.ThrowIfInvalid(variable.IsDefault, nameof(variable));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        var term = factory.Variable(variable);
        if (value.Kind is IrValueKind.Boolean or IrValueKind.Integer)
        { return factory.Binary(IrBinaryOperator.Equal, term, Term(factory, value)); }
        if (value.Kind == IrValueKind.Null)
        { return factory.Binary(IrBinaryOperator.Equal, term, factory.Null(value.Type)); }
        var nonnull = factory.Binary(IrBinaryOperator.NotEqual, term, factory.Null(value.Type));
        if (value.Kind == IrValueKind.Reference)
        { return nonnull; }
        var observed = factory.Binary(IrBinaryOperator.AndAlso, nonnull,
            factory.Binary(IrBinaryOperator.Equal, factory.Length(term), factory.Integer(value.Kind == IrValueKind.String ? value.String.Length : value.Elements.Length)));
        if (value.Kind == IrValueKind.Sequence && value.Elements.All(element => element is null ? throw new System.ArgumentNullException(nameof(element)) : (element.Kind is IrValueKind.Boolean or IrValueKind.Integer)))
        {
            for (var index = 0; index < value.Elements.Length; index++)
            {
                observed = factory.Binary(IrBinaryOperator.AndAlso, observed,
                    factory.Binary(IrBinaryOperator.Equal, factory.SequenceAccess(term, factory.Integer(index)), Term(factory, value.Elements[index])));
            }
        }
        return observed;
    }

    internal static IrTerm Term(IrFactory factory, IrValue value)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return value.Kind == IrValueKind.Boolean ? factory.Boolean(value.Boolean) : factory.IntegerBits(value.Type, value.IntegerBits);
    }

    internal static bool Equal(IrValue left, IrValue right)
    {
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        return left.Kind == right.Kind && left.Type == right.Type &&
            (left.Kind == IrValueKind.Boolean ? left.Boolean == right.Boolean : left.IntegerBits == right.IntegerBits);
    }
}
