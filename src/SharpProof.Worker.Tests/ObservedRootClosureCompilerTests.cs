using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Frontend.Host;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ObservedRootClosureCompilerTests
{
    [TestCase(false, "return")]
    [TestCase(true, "return")]
    [TestCase(false, "zero-loop")]
    [TestCase(true, "zero-loop")]
    [TestCase(false, "early-throw")]
    [TestCase(true, "early-throw")]
    [TestCase(false, "unused-body")]
    [TestCase(true, "unused-body")]
    [TestCase(false, "dead-body")]
    [TestCase(true, "dead-body")]
    [TestCase(false, "nameof")]
    [TestCase(true, "nameof")]
    public void RootCaptureFramesPrecedeUserControlFlow(bool release, string shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var body = shape switch
        {
            "return" => "return () => value;",
            "zero-loop" => "System.Func<int> last=null; for(int i=0;i<value;i++) { last=() => value; } return last;",
            "early-throw" => "if(value<0) { throw Failure; } return () => value;",
            "nameof" => "return () => nameof(value).Length;",
            "unused-body" => "return () => { int copied=value; };",
            "dead-body" => "return () => { if(false) { System.GC.KeepAlive(value); } };",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var resultType = shape is "unused-body" or "dead-body" ? "System.Action" : "System.Func<int>";
        var source = "public static class Subject { static readonly System.Exception Failure=new System.Exception(); " +
            "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] " +
            "public static " + resultType + " Target(int value) { " + body + " } }";
        var compilation = TestCompilation.Create("RootClosureCompiler", ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(release ? OptimizationLevel.Release : OptimizationLevel.Debug));
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var methodSymbol = (IMethodSymbol)CompilationModelProvider.GetSemanticModel(compilation, tree).GetDeclaredSymbol(declaration)!;
        var plan = RootParameterClosurePlan.TryCreate(methodSymbol, compilation, TestContext.CurrentContext.CancellationToken);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new AssemblyLoadContext("RootClosureCompiler", isCollectible: true);
        try
        {
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
            var first = 0;
            while (bytes[first] == 0)
            { first++; }
            var entryFrame = bytes[first] == 0x73;
            if (shape is "zero-loop" or "early-throw")
            {
                Assert.That(plan, Is.Null);
                Assert.That(entryFrame, Is.True);
            }
            else
            { Assert.That(plan is not null, Is.EqualTo(entryFrame)); }
            if (shape is "return" or "nameof")
            { Assert.That(entryFrame, Is.EqualTo(shape == "return")); }
            if (entryFrame)
            {
                var token = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(first + 1, 4));
                var constructor = method.Module.ResolveMethod(token)!;
                Assert.That(constructor.IsConstructor, Is.True);
                Assert.That(constructor.DeclaringType!.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false), Is.True);
                Assert.That(constructor.DeclaringType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(static field => field is null ? throw new ArgumentNullException(nameof(field)) : field.FieldType), Is.EquivalentTo(shape == "zero-loop" ? new[] { typeof(int), typeof(Func<int>) } : new[] { typeof(int) }));
            }
            var target = method.CreateDelegate<Func<int, object?>>();
            for (var warm = 0; warm < 16; warm++)
            { GC.KeepAlive(target(0)); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            object? result = null;
            for (var repeat = 0; repeat < 32; repeat++)
            { result = target(0); GC.KeepAlive(result); }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(!entryFrame ? 0 : shape == "zero-loop" ? 32 * 32 : 32 * 88));
        }
        finally { runtime.Unload(); }
    }
}
