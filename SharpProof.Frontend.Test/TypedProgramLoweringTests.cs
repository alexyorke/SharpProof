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

    [TestCase("int Target(int x) { while (x > 0) { x--; } return x; }")]
    [TestCase("int Target(int x) { try { try { return x; } finally { x++; } } finally { x++; } }")]
    [TestCase("int Target(int x) => System.Math.Abs(x);")]
    [TestCase("int Target(int[] x) => x[0];")]
    [TestCase("int Target(int x) { ref int r = ref x; r++; return x; }")]
    public void UnsupportedCandidateDoesNotChangeLegacyClassification(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Lower().IsExact, Is.False);
        var legacy = new IrFactory();
        var first = new RoslynProgramLowerer(legacy).Lower(subject.Graph);
        var otherLegacy = new IrFactory();
        var second = new RoslynProgramLowerer(otherLegacy).Lower(subject.Graph);
        Assert.That(first.Classification.Decision, Is.EqualTo(second.Classification.Decision));
        Assert.That(first.Classification.Abstention, Is.EqualTo(second.Classification.Abstention));
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

    internal static TypedProgramSubject Create(string members, string additionalSource = "")
    {
        var source = FrontendTestHelpers.WrapSubjectMembers("public static " + members) + additionalSource;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "typed.cs");
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
