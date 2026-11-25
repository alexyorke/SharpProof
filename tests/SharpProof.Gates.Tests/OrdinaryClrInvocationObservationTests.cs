using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryClrInvocationObservationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualSameEmissionObservesNormalReturnAndUnderlyingException(bool fail)
    {
        const string source = """
            public static class InvocationSourceProbe
            {
                public static int Counter;
                public static void Target(bool fail)
                {
                    Counter++;
                    if (fail) throw new System.InvalidOperationException("ordinary-sdk-observation");
                }
            }
            """;
        var compilation = AnalyzerGateHost.CreateCompilation(source, "SharpProofClrInvocationObservation");
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, tree);
        var symbol = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        Assert.That(symbol.IsStatic, Is.True);
        Assert.That(symbol.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly), Is.True);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join(Environment.NewLine, emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("SharpProof.ActualClrInvocation." + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var owner = assembly.GetType("InvocationSourceProbe", throwOnError: true)!;
            var method = owner.GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            var counter = owner.GetField("Counter", BindingFlags.Public | BindingFlags.Static)!;
            Assert.That(counter.GetValue(null), Is.EqualTo(0));
            var observed = NativeExceptionWitnessOracle.ObserveInvocationException(method, [fail]);
            Assert.That(counter.GetValue(null), Is.EqualTo(1));
            if (fail)
            {
                Assert.That(observed, Is.TypeOf<InvalidOperationException>());
                Assert.That(observed!.Message, Is.EqualTo("ordinary-sdk-observation"));
            }
            else
            {
                Assert.That(observed, Is.Null);
            }
        }
        finally
        {
            context.Unload();
        }
    }

    // An async method stores its exception in the task it returns: the oracle
    // observes the task's fault, or its cancellation exception.
    [TestCase("System.Threading.Tasks.Task", "throw new System.InvalidOperationException(\"async-fault\");", typeof(InvalidOperationException))]
    [TestCase("System.Threading.Tasks.ValueTask<int>", "throw new System.InvalidOperationException(\"async-fault\");", typeof(InvalidOperationException))]
    [TestCase("System.Threading.Tasks.Task<int>", "throw new System.OperationCanceledException(\"async-fault\");", typeof(OperationCanceledException))]
    [TestCase("System.Threading.Tasks.ValueTask", "return;", null)]
    public void AsyncInvocationObservesTheReturnedTasksFault(string returnType, string body, Type? expected)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(body);
        var source = "public static class AsyncProbe { public static async " + returnType + " Target(bool fail) { if (fail) { " + body +
            " } " + (returnType.EndsWith("<int>", StringComparison.Ordinal) ? "return 1; " : "") + "} }";
        var compilation = AnalyzerGateHost.CreateCompilation(source, "SharpProofAsyncInvocationObservation");
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join(Environment.NewLine, emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("SharpProof.AsyncInvocation." + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var method = context.LoadFromStream(image).GetType("AsyncProbe", throwOnError: true)!
                .GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            var observed = NativeExceptionWitnessOracle.ObserveInvocationException(method, [true]);
            Assert.That(observed?.GetType(), Is.EqualTo(expected));
            Assert.That(NativeExceptionWitnessOracle.ObserveInvocationException(method, [false]), Is.Null);
        }
        finally
        {
            context.Unload();
        }
    }
}
