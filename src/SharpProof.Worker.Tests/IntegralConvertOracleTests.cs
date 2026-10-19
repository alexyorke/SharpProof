using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

public sealed class IntegralConvertOracleTests
{
    [Test]
    public void IntegralToInt64SymbolGuardRejectsSourceLookalikesAndOtherDomains()
    {
        var references = CSharpCompilation.Create("SymbolGuard", references: TestMetadataReferences.Platform);
        var overloads = references.GetTypeByMetadataName("System.Convert")!.GetMembers("ToInt64").OfType<IMethodSymbol>().ToArray();
        Assert.That(overloads.Where(CSharpOperationSemantics.IsIntegralConvertToInt64).ToArray(), Has.Length.EqualTo(10));
        foreach (var method in overloads)
        {
            var type = method.Parameters[0].Type.SpecialType;
            var expected = method.Parameters.Length == 1 && type is SpecialType.System_Boolean or SpecialType.System_SByte or
                SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char or
                SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64;
            Assert.That(CSharpOperationSemantics.IsIntegralConvertToInt64(method), Is.EqualTo(expected), method.ToString());
        }
        var tree = CSharpSyntaxTree.ParseText("namespace System { public static class Convert { public static long ToInt64(int value) => 42; } }");
        var impostor = references.AddSyntaxTrees(tree).GetTypeByMetadataName("System.Convert")!.GetMembers("ToInt64").OfType<IMethodSymbol>().Single();
        Assert.That(CSharpOperationSemantics.IsIntegralConvertToInt64(impostor), Is.False);
    }

    [Test]
    public void IntegralToInt64DoesNotHideAnUnsupportedArgument()
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static long Target(int value) => System.Convert.ToInt64(value.GetHashCode()); }");
        var compilation = CSharpCompilation.Create("UnsupportedArgument", [tree], TestMetadataReferences.Platform);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var builder = new IrProgramBuilder(factory);
        var lowerer = new RoslynTotalExpressionLowerer(context, builder);
        var invocation = (Microsoft.CodeAnalysis.Operations.IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
        Assert.That(lowerer.LowerClause(invocation).Classification.IsExact, Is.False);
        Assert.That(lowerer.LowerBodyValue(invocation, builder.CreateBlock()).Classification.IsExact, Is.False);
    }

    [Test]
    public void SourceConvertLookalikeCannotCreateAFrameworkReadinessSite()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace System { public static class Convert { public static long ToInt64(int value) => 42; } } public static class Probe { public static long Target(int value) { return System.Convert.ToInt64(value); } }");
        var compilation = CSharpCompilation.Create("SourceConvert", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        Assert.That(lowered.Program.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions).OfType<IrHavocInstruction>()
            .Any(havoc => havoc is null ? throw new ArgumentNullException(nameof(havoc)) : IrOpaqueCallSite.IntegralConvertInitializationPrimitive(factory, havoc.Operation) != null), Is.False);
    }

    [TestCase("sbyte", "-128")]
    [TestCase("sbyte", "127")]
    [TestCase("byte", "255")]
    [TestCase("short", "-32768")]
    [TestCase("short", "32767")]
    [TestCase("ushort", "65535")]
    [TestCase("char", "0")]
    [TestCase("char", "55296")]
    [TestCase("char", "65535")]
    [TestCase("int", "-2147483648")]
    [TestCase("int", "2147483647")]
    [TestCase("uint", "4294967295")]
    [TestCase("long", "-9223372036854775808")]
    [TestCase("long", "9223372036854775807")]
    [TestCase("ulong", "0")]
    [TestCase("ulong", "9223372036854775807")]
    [TestCase("ulong", "9223372036854775808")]
    [TestCase("ulong", "18446744073709551615")]
    [TestCase("bool", "false")]
    [TestCase("bool", "true")]
    public void IntegralToInt64MatchesClrAtDomainBoundaries(string type, string input)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(input);
        Compare(type, input, "return unchecked(System.Convert.ToInt64(value: value));");
    }

    [TestCase("return System.Convert.ToInt64(value) + ++value;")]
    [TestCase("var values = new int[1]; values[0] = value; return System.Convert.ToInt64(values[0]) + (values[0] = 17);")]
    [TestCase("return System.Convert.ToInt64(++value) + value;")]
    [TestCase("return System.Convert.ToInt64(value++) + value;")]
    public void IntegralToInt64KeepsEvaluationOrder(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Compare("int", "16", body);
    }

    private static void Compare(string type, string input, string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(input);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static long Target(" + type + " value) { " + body + " } }");
        var compilation = CSharpCompilation.Create("IntegralConvertOracle", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(";", emitted.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        Assert.That(lowered.IsExact, Is.True);
        var runtime = new AssemblyLoadContext("IntegralConvertOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var method = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!;
            var runtimeType = method.GetParameters().Single().ParameterType;
            var argument = runtimeType == typeof(char) ? (object)(char)int.Parse(input, CultureInfo.InvariantCulture)
                : System.Convert.ChangeType(input, runtimeType, CultureInfo.InvariantCulture);
            object? actual = null;
            Exception? fault = null;
            try
            { actual = method.Invoke(null, [argument]); }
            catch (TargetInvocationException exception) { fault = exception.InnerException; }
            var parameter = context.Parameters.Single().Entry;
            var irType = factory.GetVariableInfo(parameter).Type;
            var irValue = argument is bool boolean ? factory.CreateBooleanValue(boolean)
                : argument is ulong unsigned ? factory.CreateIntegerValue(irType, unsigned)
                : factory.CreateIntegerValue(irType, long.Parse(input, CultureInfo.InvariantCulture));
            var options = new IrProgramReplayOptions(static request =>
            {
                ArgumentNullException.ThrowIfNull(request);
                return null;
            })
            {
                ConcreteHavocValueProvider = request =>
                {
                    ArgumentNullException.ThrowIfNull(request);
                    return PassiveCallableVcPlan.ObserveIntegralConvertInitialization(factory, request, PassiveCallableVcPlan.WarmIntegralConvert);
                }
            };
            var execution = new IrProgramInterpreter(factory).Execute(lowered.Program,
                new Dictionary<IrVarId, IrValue> { [parameter] = irValue }, 5000, options);
            Assert.That(execution.ConsumedApproximation, Is.False);
            if (fault is null)
            {
                Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), execution.Unsupported?.Detail);
                Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger((long)actual!)));
                Assert.That(factory.GetTypeInfo(execution.ReturnValue.Type).Width, Is.EqualTo(64));
                Assert.That(factory.GetTypeInfo(execution.ReturnValue.Type).Signed, Is.True);
            }
            else
            {
                Assert.That(fault, Is.TypeOf<OverflowException>());
                Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
            }
        }
        finally { runtime.Unload(); }
    }
}
