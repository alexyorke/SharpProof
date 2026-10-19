using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class TotalClosedAttributeBoundaryMatrixTests
{
    private static readonly ImmutableArray<string> Types = ["sbyte", "byte", "short", "ushort", "char", "int", "uint", "long", "ulong"];

    private static IEnumerable<TestCaseData> NumericCases()
    {
        var ranges = ImmutableArray.Create<(long Minimum, long Maximum)>((long.MinValue, long.MaxValue), (-129, 128), (-1, 1), (0, 0), (0, 1), (127, 129),
            (255, 257), (65535, 65537), (long.MinValue, long.MinValue), (long.MaxValue, long.MaxValue));
        foreach (var type in Types)
        {
            yield return new TestCaseData(type, true, 0L, 0L);
            foreach (var range in ranges)
            {
                yield return new TestCaseData(type, false, range.Minimum, range.Maximum);
            }
        }
    }

    [TestCaseSource(nameof(NumericCases))]
    public void PublicTypedAttributeBindingMatchesCompilerAndClrBoundaries(string typeName, bool positive, long minimum, long maximum)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        var attribute = positive ? "Positive" : "InRange(" + minimum.ToString(CultureInfo.InvariantCulture) + "L, " + maximum.ToString(CultureInfo.InvariantCulture) + "L)";
        var compilation = CreateCompilation(typeName, attribute);
        var target = GetTarget(compilation);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var bound = new ContractBinder(compilation, factory).BindTotal(context);
        Assert.That(bound.IsSuccess, Is.True, bound.Failure.ToString());
        Assert.That(bound.Clauses, Has.Length.EqualTo(2));
        Assert.That(bound.Clauses.All(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Evidence == BoundContractEvidence.ClosedAttribute)), Is.True);
        var entry = context.Parameters[0].Entry;
        var result = context.Result!.Value;
        var type = factory.GetVariableInfo(entry).Type;
        var info = factory.GetTypeInfo(type);
        var (width, signed) = typeName switch
        {
            "sbyte" => (8, true),
            "byte" => (8, false),
            "short" => (16, true),
            "ushort" or "char" => (16, false),
            "int" => (32, true),
            "uint" => (32, false),
            "long" => (64, true),
            _ => (64, false)
        };
        Assert.That(info.Kind, Is.EqualTo(IrTypeKind.Integer));
        Assert.That(info.Width, Is.EqualTo(width));
        Assert.That(info.Signed, Is.EqualTo(signed));
        Assert.That(factory.GetVariableInfo(result).Type, Is.EqualTo(type));
        var domainMinimum = signed ? -(BigInteger.One << (width - 1)) : BigInteger.Zero;
        var domainMaximum = (BigInteger.One << (signed ? width - 1 : width)) - 1;
        var values = ImmutableArray.Create<BigInteger>(domainMinimum, domainMaximum, -1, 0, 1, minimum, maximum,
            (BigInteger)minimum - 1, (BigInteger)minimum + 1, (BigInteger)maximum - 1, (BigInteger)maximum + 1)
            .Where(value => value >= domainMinimum && value <= domainMaximum).Distinct().ToImmutableArray();
        var legacyFactory = new IrFactory();
        var legacy = new ContractBinder(compilation, legacyFactory).Bind(target);
        Assert.That(legacy.IsSuccess, Is.EqualTo(typeName != "ulong"));
        Assert.That(legacy.Failure, Is.EqualTo(typeName == "ulong"
            ? ContractBindingFailure.InvalidClosedAttribute : ContractBindingFailure.None));
        if (typeName == "ulong")
        {
            Assert.That(legacy.Contracts, Is.Null);
        }
        var loadContext = new AssemblyLoadContext("ClosedAttributeBoundary", isCollectible: true);
        try
        {
            using var image = Emit(compilation);
            var runtime = loadContext.LoadFromStream(image).GetType("Target")!.GetMethod("Read")!;
            foreach (var value in values)
            {
                var argument = RuntimeValue(typeName, value);
                var actual = runtime.Invoke(null, [argument]);
                var returned = actual is char character ? new BigInteger(character) :
                    BigInteger.Parse(Convert.ToString(actual, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
                Assert.That(returned, Is.EqualTo(value));
                var expected = positive ? value > 0 : value >= minimum && value <= maximum;
                var supplied = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(entry, signed ? factory.CreateIntegerValue(type, (long)value) : factory.CreateIntegerValue(type, (ulong)value)).SetItem(result, signed ? factory.CreateIntegerValue(type, (long)value) : factory.CreateIntegerValue(type, (ulong)value));
                foreach (var clause in bound.Clauses)
                {
                    var evaluation = new IrInterpreter(factory).Evaluate(clause.Value, supplied);
                    Assert.That(evaluation.Value!.Boolean, Is.EqualTo(expected));
                    Assert.That(new IrInterpreter(factory).Evaluate(clause.SafeCondition, supplied).Value!.Boolean, Is.True);
                }
                if (legacy.IsSuccess)
                {
                    var legacyValues = ImmutableDictionaryCapture.Create(
                        legacy.Contracts!.Variables.Where(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role is BoundContractVariableRole.Parameter or BoundContractVariableRole.Result)),
                        variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Variable),
                        variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (legacyFactory.CreateIntegerValue(
                            legacyFactory.GetVariableInfo(variable.Variable).Type, (long)value)));
                    Assert.That(legacy.Contracts.Clauses, Has.Length.EqualTo(2));
                    foreach (var clause in legacy.Contracts.Clauses)
                    {
                        Assert.That(new IrInterpreter(legacyFactory).Evaluate(clause.Condition, legacyValues).Value!.Boolean, Is.EqualTo(expected));
                    }
                }
            }
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [TestCase("string?")]
    [TestCase("int[]?")]
    [TestCase("object?")]
    public void PublicNotNullBindingRetainsNaturalNullAndNonNullReferenceValues(string typeName)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        var compilation = CreateCompilation(typeName, "NotNull");
        var target = GetTarget(compilation);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var bound = new ContractBinder(compilation, factory).BindTotal(context);
        Assert.That(bound.IsSuccess, Is.True, bound.Failure.ToString());
        Assert.That(bound.Clauses, Has.Length.EqualTo(2));
        var type = factory.GetVariableInfo(context.Parameters[0].Entry).Type;
        object nonNull = typeName switch { "string?" => "value", "int[]?" => new[] { 1 }, _ => new object() };
        var loadContext = new AssemblyLoadContext("ClosedNotNullBoundary", isCollectible: true);
        try
        {
            using var image = Emit(compilation);
            var runtime = loadContext.LoadFromStream(image).GetType("Target")!.GetMethod("Read")!;
            foreach (var input in ImmutableArray.Create<object?>(null, nonNull))
            {
                Assert.That(runtime.Invoke(null, [input]), Is.SameAs(input));
                var value = input == null ? factory.CreateNullValue(type) : typeName switch
                {
                    "string?" => factory.CreateStringValue((string)input),
                    "int[]?" => factory.CreateSequenceValue(type, [factory.CreateIntegerValue(factory.GetTypeInfo(type).ElementType!.Value, 1L)]),
                    _ => factory.CreateReferenceValue(type, input)
                };
                var supplied = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(context.Parameters[0].Entry, value).SetItem(context.Result!.Value, value);
                foreach (var clause in bound.Clauses)
                {
                    Assert.That(clause.Evidence, Is.EqualTo(BoundContractEvidence.ClosedAttribute));
                    Assert.That(new IrInterpreter(factory).Evaluate(clause.Value, supplied).Value!.Boolean, Is.EqualTo(input != null));
                    Assert.That(new IrInterpreter(factory).Evaluate(clause.SafeCondition, supplied).Value!.Boolean, Is.True);
                }
            }
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static CSharpCompilation CreateCompilation(string typeName, string attribute)
    {
        System.ArgumentNullException.ThrowIfNull(attribute);
        System.ArgumentNullException.ThrowIfNull(typeName);
        var compilation = TestCompilation.Create("ClosedAttributeBoundaryMatrix", $$"""
            #nullable enable
            using SharpProof.Attributes;
            public static class Target {
                [return: {{attribute}}]
                public static {{typeName}} Read([{{attribute}}] {{typeName}} value) => value;
            }
            public static class Caller {
                public static void Call() { _ = Target.Read(default); }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }

    private static IMethodSymbol GetTarget(Compilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single());
        Assert.That(operation, Is.InstanceOf<IInvocationOperation>());
        var target = ((IInvocationOperation)operation!).TargetMethod;
        Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(target.Parameters[0].Type, target.ReturnType), Is.True);
        Assert.That(target.Parameters[0].GetAttributes(), Has.Length.EqualTo(1));
        Assert.That(target.GetReturnTypeAttributes(), Has.Length.EqualTo(1));
        return target;
    }

    private static MemoryStream Emit(Compilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        image.Position = 0;
        return image;
    }

    private static object RuntimeValue(string typeName, BigInteger value)
    {
        System.ArgumentNullException.ThrowIfNull(typeName);
        return typeName switch
        {
            "sbyte" => (sbyte)value,
            "byte" => (byte)value,
            "short" => (short)value,
            "ushort" => (ushort)value,
            "char" => (char)(ushort)value,
            "int" => (int)value,
            "uint" => (uint)value,
            "long" => (long)value,
            _ => (ulong)value
        };
    }
}
