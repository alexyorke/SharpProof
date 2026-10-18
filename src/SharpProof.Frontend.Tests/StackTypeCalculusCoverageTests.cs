using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class StackTypeCalculusCoverageTests
{
    private const string Source = """
        #nullable enable
        public static class StackShapeOracle
        {
            public static bool Bool(bool value) => value;
            public static int I4(int value) => value;
            public static long I8(long value) => value;
            public static object? Object(object? value) => value;
            public static string? Text(string? value) => value;
            public static object? NullObject() => null;
            public static string? NullText() => null;
            public static decimal Unsupported(decimal value) => value;
            public static int[] UnsupportedArray(int[] value) => value;
            public static int? NullableNull() => null;
            public static sbyte I1(sbyte value) => value;
            public static byte U1(byte value) => value;
            public static short I2(short value) => value;
            public static ushort U2(ushort value) => value;
            public static char Character(char value) => value;
            public static uint U4(uint value) => value;
            public static ulong U8(ulong value) => value;
            public static float Single(float value) => value;
            public static double Double(double value) => value;
            public static void NoValue() { }
            public static int Zero() => 0;
            public static int One() => 1;
            public static int Other() => 17;
        }
        """;

    private static readonly ImmutableArray<string> Categories = ["bool", "int", "long", "object", "string", "null"];
    private static readonly ImmutableArray<ImmutableArray<string?>> JoinTable =
    [
        ["bool", "int", null, null, null, null],
        ["int", "int", null, null, null, null],
        [null, null, "long", null, null, null],
        [null, null, null, "object", null, "object"],
        [null, null, null, null, "string", "string"],
        [null, null, null, "object", "string", "null"]
    ];
    private static readonly ImmutableArray<ImmutableArray<bool>> ReferenceTable =
    [
        [false, false, false, false, false, false],
        [false, false, false, false, false, false],
        [false, false, false, false, false, false],
        [false, false, false, true, false, true],
        [false, false, false, false, true, true],
        [false, false, false, true, true, true]
    ];
    // Target columns: Boolean, I4, I8, object, string, unsupported.
    private static readonly ImmutableArray<ImmutableArray<bool>> AcceptanceTable =
    [
        [true, true, false, false, false, false],
        [false, true, false, false, false, false],
        [false, false, true, false, false, false],
        [false, false, false, true, false, false],
        [false, false, false, false, true, false],
        [false, false, false, true, true, false]
    ];
    private static readonly ImmutableArray<string> Targets = ["bool", "int", "long", "object", "string", "byte", "char", "uint", "ulong", "decimal", "array", "nullable", "float", "double", "void"];
    private static readonly ImmutableArray<int> TargetColumns = [0, 1, 2, 3, 4, 1, 1, 1, 2, 5, 5, 5, 5, 5, 5];
    private static readonly ImmutableDictionary<string, string> Methods =
        ImmutableDictionary.CreateRange<string, string>(StringComparer.Ordinal,
        [
        new("bool", "Bool"),
        new("int", "I4"),
        new("long", "I8"),
        new("object", "Object"),
        new("string", "Text"),
        new("sbyte", "I1"),
        new("byte", "U1"),
        new("short", "I2"),
        new("ushort", "U2"),
        new("char", "Character"),
        new("uint", "U4"),
        new("ulong", "U8"),
        new("decimal", "Unsupported"),
        new("array", "UnsupportedArray"),
        new("nullable", "NullableNull"),
        new("float", "Single"),
        new("double", "Double"),
        new("void", "NoValue")
            ]);

    private CSharpCompilation _compilation = null!;
    private SemanticModel _model = null!;
    private AssemblyLoadContext _loadContext = null!;
    private Assembly _assembly = null!;

    [OneTimeSetUp]
    public void CompileAndEmitActualInputSource()
    {
        var tree = CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.CSharp12), "stack-shape.cs");
        _compilation = CSharpCompilation.Create("StackShapeOracle", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(_compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        _model = _compilation.GetSemanticModel(tree);
        using var image = new MemoryStream();
        var emitted = _compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        _loadContext = new AssemblyLoadContext("StackShapeOracle", isCollectible: true);
        _assembly = _loadContext.LoadFromStream(image);
    }

    [OneTimeTearDown]
    public void ReleaseEmittedAssembly()
    { _loadContext?.Unload(); }

    [Test]
    public void ExactInputSourceHasIndependentClrControls()
    {
        Assert.That(Invoke("Bool", true), Is.EqualTo(true));
        Assert.That(Invoke("Bool", false), Is.EqualTo(false));
        Assert.That(Invoke("I4", 17), Is.EqualTo(17));
        Assert.That(Invoke("I8", -19L), Is.EqualTo(-19L));
        var instance = new object();
        Assert.That(Invoke("Object", instance), Is.SameAs(instance));
        const string text = "owned";
        Assert.That(Invoke("Text", text), Is.SameAs(text));
        Assert.That(Invoke("Object", (object?)null), Is.Null);
        Assert.That(Invoke("Text", (object?)null), Is.Null);
        Assert.That(Invoke("NullObject"), Is.Null);
        Assert.That(Invoke("NullText"), Is.Null);
        Assert.That(Invoke("NullableNull"), Is.Null);
        Assert.That(Invoke("Unsupported", 1.25m), Is.EqualTo(1.25m));
        int[] array = [0, 17, -19];
        Assert.That(Invoke("UnsupportedArray", array), Is.SameAs(array));
        Assert.That(Invoke("I1", (sbyte)-17), Is.EqualTo((sbyte)-17));
        Assert.That(Invoke("U1", byte.MaxValue), Is.EqualTo(byte.MaxValue));
        Assert.That(Invoke("I2", short.MinValue), Is.EqualTo(short.MinValue));
        Assert.That(Invoke("U2", ushort.MaxValue), Is.EqualTo(ushort.MaxValue));
        Assert.That(Invoke("Character", 'x'), Is.EqualTo('x'));
        Assert.That(Invoke("U4", uint.MaxValue), Is.EqualTo(uint.MaxValue));
        Assert.That(Invoke("U8", ulong.MaxValue), Is.EqualTo(ulong.MaxValue));
        Assert.That(Invoke("Single", 1.25f), Is.EqualTo(1.25f));
        Assert.That(Invoke("Double", 1.25d), Is.EqualTo(1.25d));
        Assert.That(Invoke("NoValue"), Is.Null);
    }

    private static IEnumerable<TestCaseData> Joins()
    {
        for (var row = 0; row < Categories.Length; row++)
        {
            for (var column = 0; column < Categories.Length; column++)
            { yield return new(Categories[row], Categories[column], JoinTable[row][column]); }
        }
    }

    [TestCaseSource(nameof(Joins))]
    public void WholeJoinTable(string left, string right, string? expected)
    {
        System.ArgumentNullException.ThrowIfNull(left);
        System.ArgumentNullException.ThrowIfNull(right);
        var joined = Shape(left).TryJoin(Shape(right), out var value);
        Assert.That(joined, Is.EqualTo(expected != null));
        if (expected == null)
        { Assert.That(value.IsValid, Is.False); }
        else
        { Assert.That(value.Equals((object)Shape(expected)), Is.True); }
    }

    private static IEnumerable<TestCaseData> ReferencePairs()
    {
        for (var row = 0; row < Categories.Length; row++)
        {
            for (var column = 0; column < Categories.Length; column++)
            { yield return new(Categories[row], Categories[column], ReferenceTable[row][column]); }
        }
    }

    [TestCaseSource(nameof(ReferencePairs))]
    public void WholeReferenceCompatibilityTable(string left, string right, bool expected)
    { System.ArgumentNullException.ThrowIfNull(left, nameof(left)); System.ArgumentNullException.ThrowIfNull(right, nameof(right)); Assert.That(TotalIlStackType.CompatibleReferences(Shape(left), Shape(right)), Is.EqualTo(expected)); }

    private static IEnumerable<TestCaseData> Stores()
    {
        for (var row = 0; row < Categories.Length; row++)
        {
            for (var column = 0; column < Targets.Length; column++)
            { yield return new(Categories[row], Targets[column], AcceptanceTable[row][TargetColumns[column]]); }
        }
    }

    [TestCaseSource(nameof(Stores))]
    public void WholeStoreAcceptanceTable(string value, string target, bool expected)
    { System.ArgumentNullException.ThrowIfNull(target, nameof(target)); System.ArgumentNullException.ThrowIfNull(value, nameof(value)); Assert.That(Shape(value).Accepts(Type(target)), Is.EqualTo(expected)); }

    [TestCase("bool", 32, false, true)]
    [TestCase("int", 32, false, false)]
    [TestCase("long", 64, false, false)]
    [TestCase("sbyte", 32, false, false)]
    [TestCase("byte", 32, false, false)]
    [TestCase("short", 32, false, false)]
    [TestCase("ushort", 32, false, false)]
    [TestCase("char", 32, false, false)]
    [TestCase("uint", 32, false, false)]
    [TestCase("ulong", 64, false, false)]
    [TestCase("object", 0, true, false)]
    [TestCase("string", 0, true, false)]
    public void OwnedSymbolMappingAndStorage(string type, int width, bool reference, bool booleanCompatible)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        var symbol = Type(type);
        Assert.That(TotalIlStackType.TryFromTypeSymbol(symbol, out var shape), Is.True);
        Assert.That(shape.IsValid, Is.True);
        Assert.That(shape.NumericWidth, Is.EqualTo(width));
        Assert.That(TotalIlStackType.NumericWidthFor(symbol.SpecialType), Is.EqualTo(width));
        Assert.That(shape.IsReference, Is.EqualTo(reference));
        Assert.That(shape.IsBooleanCompatibleI4, Is.EqualTo(booleanCompatible));
        Assert.That(shape.IsNullReference, Is.False);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var storage = shape.StorageType(factory);
        if (type == "object")
        { Assert.That(storage, Is.EqualTo(factory.ObjectType)); }
        else if (type == "string")
        { Assert.That(storage, Is.EqualTo(factory.StringType)); }
        else
        {
            var info = factory.GetTypeInfo(storage);
            Assert.That(info.Kind, Is.EqualTo(IrTypeKind.Integer));
            Assert.That(info.Width, Is.EqualTo(width));
            Assert.That(info.Signed, Is.True);
        }
    }

    [TestCase("decimal")]
    [TestCase("array")]
    [TestCase("nullable")]
    [TestCase("float")]
    [TestCase("double")]
    [TestCase("void")]
    public void ActualUnsupportedTypeHasNoStackValue(string type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        Assert.That(TotalIlStackType.TryFromTypeSymbol(Type(type), out var shape), Is.False);
        Assert.That(shape.IsValid, Is.False);
        Assert.That(TotalIlStackType.NumericWidthFor(Type(type).SpecialType), Is.Zero);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(new Action(() => shape.StorageType(factory)));
        Assert.That(exception!.ParamName, Is.EqualTo("width"));
    }

    [Test]
    public void AbsentCompilerTypeLookupHasNoStackValue()
    {
        var missing = _compilation.GetTypeByMetadataName("StackShapeOracle.AbsentType");
        Assert.That(missing, Is.Null);
        Assert.That(TotalIlStackType.TryFromTypeSymbol(missing, out var shape), Is.False);
        Assert.That(shape.IsValid, Is.False);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(new Action(() => shape.StorageType(factory)));
        Assert.That(exception!.ParamName, Is.EqualTo("width"));
    }

    [TestCase("NullObject")]
    [TestCase("NullText")]
    public void ActualNullReferenceConstantsHaveObjectStorage(string method)
    {
        System.ArgumentNullException.ThrowIfNull(method);
        var shape = NullShape(Expression(method));
        Assert.That(shape.IsNullReference, Is.True);
        Assert.That(shape.IsReference, Is.True);
        Assert.That(shape.NumericWidth, Is.Zero);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        Assert.That(shape.StorageType(factory), Is.EqualTo(factory.ObjectType));
    }

    [TestCase("Zero", "bool", 0)]
    [TestCase("One", "bool", 1)]
    [TestCase("Other", "int", 17)]
    public void ActualI4ConstantsRetainTheirBooleanSubset(string method, string expected, int constant)
    {
        System.ArgumentNullException.ThrowIfNull(expected);
        System.ArgumentNullException.ThrowIfNull(method);
        var operation = Expression(method);
        Assert.That(operation.ConstantValue.HasValue, Is.True);
        Assert.That(operation.ConstantValue.Value, Is.EqualTo(constant));
        Assert.That(Invoke(method), Is.EqualTo(constant));
        var shape = TotalIlStackType.FromInt32Constant((int)operation.ConstantValue.Value!);
        Assert.That(shape.Equals(Shape(expected)), Is.True);
        Assert.That(shape.WidenBooleanCompatibleI4().Equals(Shape("int")), Is.True);
    }

    [Test]
    public void ActualTypeFactoriesHaveTheirDeclaredValueEquivalenceClasses()
    {
        var values = ImmutableHashSet.CreateBuilder<TotalIlStackType>();
        foreach (var type in Methods.Keys)
        {
            if (TotalIlStackType.TryFromTypeSymbol(Type(type), out var shape))
            { values.Add(shape); }
        }
        values.Add(Shape("null"));
        Assert.That(values.Count, Is.EqualTo(6));
        foreach (var category in Categories)
        { Assert.That(values.Contains(Shape(category)), Is.True); }
    }

    private ITypeSymbol Type(string key)
    {
        System.ArgumentNullException.ThrowIfNull(key);
        return _compilation.GetTypeByMetadataName("StackShapeOracle")!
            .GetMembers(Methods[key]).OfType<IMethodSymbol>().Single().ReturnType;
    }

    private IOperation Expression(string method)
    {
        System.ArgumentNullException.ThrowIfNull(method);
        var syntax = _model.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Identifier.ValueText == method)).ExpressionBody!.Expression;
        return _model.GetOperation(syntax)!;
    }

    private TotalIlStackType Shape(string key)
    {
        if (key == "null")
        { return NullShape(Expression("NullObject")); }
        var created = TotalIlStackType.TryFromTypeSymbol(Type(key), out var shape);
        Assert.That(created, Is.True);
        return shape;
    }

    private static TotalIlStackType NullShape(IOperation operation)
    {
        System.ArgumentNullException.ThrowIfNull(operation);
        Assert.That(operation.ConstantValue.HasValue, Is.True);
        Assert.That(operation.ConstantValue.Value, Is.Null);
        var type = operation.Type ?? operation.SemanticModel?.GetTypeInfo(operation.Syntax).ConvertedType;
        Assert.That(type?.SpecialType, Is.AnyOf(SpecialType.System_Object, SpecialType.System_String));
        return TotalIlStackType.NullFromIl;
    }

    private object? Invoke(string method, params object?[] arguments)
    { System.ArgumentNullException.ThrowIfNull(arguments, nameof(arguments)); System.ArgumentNullException.ThrowIfNull(method, nameof(method)); return _assembly.GetType("StackShapeOracle")!.GetMethod(method)!.Invoke(null, arguments); }
}
