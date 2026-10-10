using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypedProgramLoweringTests
{
    public static IEnumerable<TestCaseData> ScalarCases()
    {
        foreach (var input in new[] { int.MinValue, int.MinValue + 1, -2, -1, 0, 1, int.MaxValue })
        {
            yield return Case("int Target(int x) => checked(x & int.MaxValue);", input);
            yield return Case("int Target(int x) => int.MaxValue & x;", input);
        }
        yield return Case("int Target(int x, int y) => x & y;", -1, int.MinValue);
        yield return Case("uint Target(uint x, uint y) => checked(x & y);", uint.MaxValue, 0x80000000U);
        yield return Case("long Target(long x, long y) => x & y;", -1L, long.MinValue);
        yield return Case("ulong Target(ulong x, ulong y) => checked(x & y);", ulong.MaxValue, 0x8000000000000000UL);
        yield return Case("int Target(sbyte x, byte y) => x & y;", (sbyte)-1, (byte)128);
        yield return Case("int Target(short x, ushort y) => x & y;", (short)-1, (ushort)32768);
        yield return Case("long Target(uint x, int y) => x & y;", uint.MaxValue, -1);
        yield return Case("int Target(int x) { int y = x++ & ++x; return y * 100 + x; }", 3);
        yield return Case("int Target(int x) { x &= x++; return x; }", 3);
        yield return Case("sbyte Target(sbyte x) { unchecked { x &= (sbyte)-128; return x; } }", (sbyte)-1);
        yield return Case("short Target(short x) { unchecked { x &= (short)-32768; return x; } }", (short)-1);
        yield return Case("uint Target(uint x, uint y) { x &= y; return x; }", uint.MaxValue, 0x80000000U);
        yield return Case("ulong Target(ulong x, ulong y) { x &= y; return x; }", ulong.MaxValue, 0x8000000000000000UL);
        foreach (var operation in new[] { "|", "^" })
        {
            yield return Case($"int Target(int x, int y) => x {operation} y;", -1, int.MinValue);
            yield return Case($"uint Target(uint x, uint y) => checked(x {operation} y);", uint.MaxValue, 0x80000000U);
            yield return Case($"long Target(long x, long y) => x {operation} y;", long.MinValue, 0x5555555555555555L);
            yield return Case($"ulong Target(ulong x, ulong y) => checked(x {operation} y);", ulong.MaxValue, 0x8000000000000000UL);
            yield return Case($"int Target(sbyte x, byte y) => x {operation} y;", (sbyte)-1, (byte)128);
            yield return Case($"long Target(uint x, int y) => x {operation} y;", uint.MaxValue, -1);
            yield return Case($"int Target(int x) {{ int y = x++ {operation} ++x; return y * 100 + x; }}", 3);
            yield return Case($"int Target(int x) {{ x {operation}= x++; return x; }}", 3);
            yield return Case($"sbyte Target(sbyte x) {{ checked {{ x {operation}= (sbyte)-128; return x; }} }}", (sbyte)1);
            yield return Case($"ulong Target(ulong x, ulong y) {{ x {operation}= y; return x; }}", 0x00FF00FF00FF00FFUL, 0x8000000000000001UL);
        }
        foreach (var operation in new[] { "&", "|", "^" })
        {
            foreach (var (x, y) in new[] { (false, false), (false, true), (true, false), (true, true) })
            {
                yield return Case($"bool Target(bool x, bool y) => x {operation} y;", x, y);
                yield return Case($"bool Target(bool x, bool y) {{ x {operation}= y; return x; }}", x, y);
            }
            yield return Case($"int Target(int x) {{ bool b = x++ > 3 {operation} x++ > 4; return b ? x : -x; }}", 3);
        }
        yield return Case("int Target(int x) => ~x;", int.MinValue);
        yield return Case("uint Target(uint x) => ~x;", 0x80000001U);
        yield return Case("long Target(long x) => ~x;", long.MaxValue);
        yield return Case("ulong Target(ulong x) => ~x;", 1UL);
        yield return Case("int Target(byte x) => ~x;", byte.MaxValue);
        yield return Case("int Target(sbyte x) => ~x;", sbyte.MinValue);
        yield return Case("byte Target(byte x) => (byte)~x;", (byte)0x0F);
        yield return Case("int Target(byte a, sbyte b) => a + b;", (byte)255, (sbyte)-128);
        yield return Case("int Target(ushort a) => -a;", ushort.MaxValue);
        yield return Case("long Target(uint a, int b) => a + b;", uint.MaxValue, -1);
        yield return Case("long Target(uint a) => -a;", uint.MaxValue);
        yield return Case("uint Target(uint a) => unchecked(a + 1);", uint.MaxValue);
        yield return Case("ulong Target(ulong a) => unchecked(a + 1);", ulong.MaxValue);
        yield return Case("ulong Target(sbyte a) => unchecked((ulong)a);", (sbyte)-1);
        yield return Case("sbyte Target(ulong a) => unchecked((sbyte)a);", ulong.MaxValue);
        yield return Case("bool Target(ulong a, ulong b) => a > b;", 1UL << 63, 1UL);
        yield return Case("int Target(sbyte a, sbyte b) => a / b;", sbyte.MinValue, (sbyte)-1);
        yield return Case("int Target(int x) { return x + x++; }", 3);
        yield return Case("int Target(int x) { x += x++; return x; }", 3);
        yield return Case("int Target(int x) { int y = ++x; return y + x; }", 3);
        yield return Case("int Target(int x) { int y = x--; return y + x; }", 3);
        yield return Case("bool Target(int d) => d != 0 && 10 / d > 0;", 0);
        yield return Case("bool Target(int d) => d == 0 || 10 / d > 0;", 0);
        yield return Case("int Target(int d) => d == 0 ? 1 : 10 / d;", 0);
        yield return Case("int Target(int x) { if (x > 0) { x += 2; } else { x -= 1; } return x; }", -3);
        yield return Case("long Target(ulong a) => checked((long)a);", (ulong)long.MaxValue + 1);
        yield return Case("ulong Target(long a) => checked((ulong)a);", -1L);
        foreach (var value in new[] { -1, 0, 255, 256 })
        {
            yield return Case("byte Target(int a) => checked((byte)a);", value);
        }
        foreach (var mode in new[] { "checked", "unchecked" })
        {
            foreach (var operation in new[] { "/", "%" })
            {
                yield return Case($"int Target(int a, int b) => {mode}(a {operation} b);", int.MinValue, -1);
                yield return Case($"long Target(long a, long b) => {mode}(a {operation} b);", long.MinValue, -1L);
                yield return Case($"int Target(int a, int b) => {mode}(a {operation} b);", 3, 0);
                yield return Case($"long Target(long a, long b) => {mode}(a {operation} b);", 3L, 0L);
            }
        }
    }

    [TestCase("int[] Target() => new int[Size()]; static int Size() => 1;")]
    [TestCase("object Target() => (object)Value(); static int Value() => 1;")]
    [TestCase("System.Action Target(Receiver receiver) => new System.Action(Pass(receiver).Sink); " +
        "static Receiver Pass(Receiver receiver) => receiver; public class Receiver { public void Sink() {} }")]
    public void UnsupportedAllocationOperandsAbstainWithValidResultTypes(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.False);
    }

    [TestCase("new string[] { \"\\uD800\" }")]
    [TestCase("new string[] { \"\\uDC00\" }")]
    [TestCase("[\"\\uD800\"]")]
    [TestCase("[\"\\uDC00\"]")]
    public void IllFormedArrayStringElementsAbstainWithoutThrowing(string expression)
    {
        using var subject = TypedProgramSubject.Create("string[] Target() => " + expression + ";");
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.False);
    }

    [TestCase("new string[] { \"\\uD83D\\uDE00\" }")]
    [TestCase("[\"\\uD83D\\uDE00\"]")]
    public void WellFormedArrayStringElementsRemainExact(string expression)
    {
        using var subject = TypedProgramSubject.Create("string[] Target() => " + expression + ";");
        Assert.That(subject.Lower().IsExact, Is.True);
    }

    [TestCaseSource(nameof(ScalarCases))]
    public void CandidateMatchesCompiledCSharp(string members, object[] arguments)
    {
        using var subject = TypedProgramSubject.Create(members);
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var actual = subject.Invoke(arguments);
        var execution = subject.Execute(lowered, arguments);
        if (actual is Exception exception)
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(execution.Exception!.Kind, Is.EqualTo(exception is DivideByZeroException ? IrExceptionKind.DivideByZero : IrExceptionKind.Overflow));
        }
        else
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            var expected = subject.Value(subject.Context.Result!.Value, actual!);
            Assert.That(execution.ReturnValue!.Type, Is.EqualTo(expected.Type));
            Assert.That(execution.ReturnValue.Kind == IrValueKind.Boolean ? (object)execution.ReturnValue.Boolean : execution.ReturnValue.IntegerNumericValue,
                Is.EqualTo(expected.Kind == IrValueKind.Boolean ? (object)expected.Boolean : expected.IntegerNumericValue));
        }
    }

    [TestCase("bool Target(int x) => x is 3;", 3)]
    [TestCase("bool Target(int x) => x is 3;", 4)]
    [TestCase("bool Target(int x) => x is >= 3;", 3)]
    [TestCase("bool Target(int x) => x is >= 3;", 2)]
    [TestCase("bool Target(int x) => x is > 2 and < 5;", 3)]
    [TestCase("bool Target(int x) => x is > 2 and < 5;", 5)]
    [TestCase("bool Target(int x) => x is < 0 or > 9;", -1)]
    [TestCase("bool Target(int x) => x is < 0 or > 9;", 5)]
    [TestCase("bool Target(int x) => x is not (3 or 4);", 4)]
    [TestCase("bool Target(int x) => x is not (3 or 4);", 5)]
    [TestCase("bool Target(bool x) => x is not false;", true)]
    [TestCase("bool Target(bool x) => x is not false;", false)]
    [TestCase("bool Target(uint x) => x is >= 0x80000000u;", 0x80000000u)]
    [TestCase("bool Target(char x) => x is >= 'a' and <= 'z';", 'a')]
    public void ScalarPatternBodyMatchesCompiledCSharp(string members, object input)
    {
        using var subject = TypedProgramSubject.Create(members);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(syntax.ExpressionBody!.Expression)!;
        var builder = new IrProgramBuilder(subject.Factory);
        var block = builder.CreateBlock("entry");
        builder.SetEntry(block);
        var lowered = new RoslynTotalExpressionLowerer(subject.Context, builder).LowerBodyValue(operation, block);
        Assert.That(lowered.Classification.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        builder.Return(lowered.Continuation, subject.Context.Site(operation), lowered.Value);
        var parameter = subject.Context.Parameters.Single().Current;
        var execution = new IrProgramInterpreter(subject.Factory).Execute(builder.Build(),
            new Dictionary<IrVarId, IrValue> { [parameter] = subject.Value(parameter, input) });
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.Boolean, Is.EqualTo((bool)subject.Invoke([input])!));
    }
    [TestCase("int Target(int x) => x is int k ? k : 0;")]
    [TestCase("int Target(int x) => x is var k ? k : 0;")]
    [TestCase("int Target(int? x) => x is 3 ? 1 : 0;")]
    public void TypeDeclarationAndNullablePatternsRemainOutsideExactLowering(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<IsPatternExpressionSyntax>().Single();
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        var builder = new IrProgramBuilder(subject.Factory);
        var lowered = new RoslynTotalExpressionLowerer(subject.Context, builder)
            .LowerBodyValue(operation, builder.CreateBlock("entry"));
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(subject.Lower().IsExact, Is.False);
    }

    [TestCase("int? Target(int? x, int? y) => x & y;")]
    [TestCase("int? Target(int? x, int? y) => x | y;")]
    [TestCase("bool? Target(bool? x, bool? y) => x ^ y;")]
    [TestCase("int? Target(int? x) => ~x;")]
    [TestCase("System.DayOfWeek Target(System.DayOfWeek x, System.DayOfWeek y) => x | y;")]
    [TestCase("System.DayOfWeek Target(System.DayOfWeek x) => ~x;")]
    [TestCase("int Target(Box x, Box y) => x & y; public sealed class Box { public static int operator &(Box x, Box y) => 1; }")]
    [TestCase("int Target(Box x, Box y) => x ^ y; public sealed class Box { public static int operator ^(Box x, Box y) => 1; }")]
    [TestCase("int Target(Box x) => ~x; public sealed class Box { public static int operator ~(Box x) => 1; }")]
    public void LiftedEnumAndUserDefinedBitwiseRemainClosed(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Lower().IsExact, Is.False);
    }

    [TestCase(0, 0, "a / b")]
    [TestCase(1, 0, "c / d")]
    public void FirstFaultPreventsLaterChildEvaluation(int firstDenominator, int secondDenominator, string source)
    {
        using var subject = TypedProgramSubject.Create("int Target(int a, int b, int c, int d) => a / b + c / d;");
        var execution = subject.Execute(subject.Lower(), [10, firstDenominator, 20, secondDenominator]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        var span = subject.Factory.GetOperationInfo(execution.Instruction!.Operation).SourceSpan!;
        Assert.That(subject.Source.Substring(span.Start, span.Length), Is.EqualTo(source));
        Assert.That(subject.Invoke([10, firstDenominator, 20, secondDenominator]), Is.TypeOf<DivideByZeroException>());
    }

    [TestCase("int Target(int x) { ref int r = ref x; r++; return x; }")]
    [TestCase("int Target(int x) { ref readonly int r = ref x; x++; return r; }")]
    public void ReferenceLocalAliasesAbstainInsteadOfCopyingStorage(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([3]), Is.EqualTo(4));
        Assert.That(subject.Lower().IsExact, Is.False);
    }

    private static TestCaseData Case(string members, params object[] arguments)
    {
        return new TestCaseData(members, arguments).SetName("Candidate: " + members + " [" + string.Join(",", arguments) + "]");
    }
}

internal sealed class TypedProgramSubject : IDisposable
{
    private readonly AssemblyLoadContext _assemblyContext;
    private readonly MethodInfo _method;
    private TypedProgramSubject(string source, Compilation compilation, ControlFlowGraph graph, TotalLoweringContext context,
        AssemblyLoadContext assemblyContext, MethodInfo method)
    {
        Source = source;
        Compilation = compilation;
        Graph = graph;
        Context = context;
        _assemblyContext = assemblyContext;
        _method = method;
    }
    internal string Source { get; }
    internal Compilation Compilation { get; }
    internal ControlFlowGraph Graph { get; }
    internal TotalLoweringContext Context { get; }
    internal IrFactory Factory => Context.Factory;

    internal static TypedProgramSubject Create(string members, string additionalSource = "", IEnumerable<string>? preprocessorSymbols = null)
    {
        var source = FrontendTestHelpers.WrapSubjectMembers("public static " + members) + additionalSource;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: preprocessorSymbols), "typed.cs");
        var compilation = CSharpCompilation.Create("TypedSubject", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Target");
        var model = compilation.GetSemanticModel(tree);
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), (IMethodSymbol)model.GetDeclaredSymbol(syntax)!);
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var assemblyContext = new AssemblyLoadContext("TypedSubject", isCollectible: true);
        stream.Position = 0;
        var assembly = assemblyContext.LoadFromStream(stream);
        return new(source, compilation, ControlFlowGraph.Create(syntax, model)!, context, assemblyContext, assembly.GetType("Subject")!.GetMethod("Target")!);
    }
    internal FrontendProgramLoweringResult Lower()
    {
        return new RoslynProgramLowerer(Factory).LowerCandidate(Graph, Context);
    }
    internal FrontendProgramLoweringResult LowerSourceCalls(CancellationToken cancellationToken = default)
    { return LowerSourceCalls(false, cancellationToken: cancellationToken); }

    internal FrontendProgramLoweringResult LowerShadowSourceCalls(CancellationToken cancellationToken = default)
    {
        return new RoslynProgramLowerer(Factory).LowerShadowSourceBody(Graph, Context, static _ => true, cancellationToken);
    }

    internal FrontendProgramLoweringResult LowerSourceCalls(bool arrayModels, bool opaqueCalls = false, CancellationToken cancellationToken = default)
    {
        return new RoslynProgramLowerer(Factory).LowerCandidate(Graph, Context, static _ => true, null, cancellationToken,
            arrayModels ? ResolveArrayEmpty : null, opaqueCalls: opaqueCalls);

        TotalScalarCallModel? ResolveArrayEmpty(IMethodSymbol method)
        {
            return method.IsStatic && method.ContainingType.SpecialType == SpecialType.System_Array &&
                method.Name == "Empty" && method.Arity == 1 && method.Parameters.IsEmpty &&
                CSharpOperationSemantics.IsReferenceDomain(method.ReturnType)
                ? new(0, _ => CSharpOperationSemantics.ArrayEmpty(Factory,
                    new RoslynTypeMapper(Factory).GetTypeId(method.ReturnType))) : null;
        }
    }
    internal IrProgramExecutionResult Execute(FrontendProgramLoweringResult lowering, object[] arguments)
    {
        return new IrProgramInterpreter(Factory).Execute(lowering.Program,
            Context.Parameters.ToDictionary(binding => binding.Entry, binding => Value(binding.Entry, arguments[binding.Parameter.Ordinal])));
    }
    internal IrValue Value(IrVarId variable, object value)
    {
        var type = Factory.GetVariableInfo(variable).Type;
        return value is bool boolean ? Factory.CreateBooleanValue(boolean) : value is ulong unsigned
            ? Factory.CreateIntegerValue(type, unsigned) : Factory.CreateIntegerValue(type, Convert.ToInt64(value, CultureInfo.InvariantCulture));
    }
    internal object? Invoke(object[] arguments)
    {
        try
        { return _method.Invoke(null, arguments); }
        catch (TargetInvocationException exception) { return exception.InnerException; }
    }
    public void Dispose()
    {
        _assemblyContext.Unload();
    }
}
