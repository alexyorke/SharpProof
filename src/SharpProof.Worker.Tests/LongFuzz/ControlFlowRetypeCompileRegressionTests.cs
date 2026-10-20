using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class ControlFlowRetypeCompileRegressionTests
{
    [Test]
    public void Seed251224RetypingKeepsNestedIntFunctionValid()
    {
        const int seed = 251224;
        var random = new Random(seed);
        Assert.That(random.Next(9), Is.EqualTo(4), "seed must select dnt-bool");
        var generator = typeof(ControlFlowLongFuzzTests);
        var generated = Invoke(generator, "Body", [seed, random])!;
        var body = (string)generated.GetType().GetProperty("Body")!.GetValue(generated)!;
        Assert.That(body, Does.Contain("int L1()"));

        var original = CompileForType(body + " Out: return x;", "int");
        Assert.That(original, Is.Empty, $"seed {seed} must compile before retyping: {string.Join(" | ", original.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString())))}");

        var errors = CompileForType(body + " Out: return x;", "bool");
        Assert.That(errors, Is.Empty, $"seed {seed}: {string.Join(" | ", errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString())))}");
    }

    [TestCase(251270, "bool", 4)]
    [TestCase(251365, "void", 1)]
    [TestCase(251446, "bool", 4)]
    [TestCase(251557, "bool", 4)]
    public void AdditionalNestedLocalFunctionSeedsCompile(int seed, string type, int modeIndex)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        var random = new Random(seed);
        Assert.That(random.Next(9), Is.EqualTo(modeIndex), "seed must select its pinned mode");
        var generator = typeof(ControlFlowLongFuzzTests);
        var generated = Invoke(generator, "Body", [seed, random])!;
        var body = (string)generated.GetType().GetProperty("Body")!.GetValue(generated)!;
        Assert.That(body, Does.Contain("int L1()"));

        var original = CompileForType(body + " Out: return x;", "int");
        Assert.That(original, Is.Empty, $"seed {seed} must compile before retyping: {string.Join(" | ", original.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString())))}");

        var errors = CompileForType(body + " Out: return x;", type);
        Assert.That(errors, Is.Empty, $"seed {seed}: {string.Join(" | ", errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString())))}");
    }

    [Test]
    public void NearbyTopLevelBoolReturnStillCompiles()
    {
        var errors = CompileForType("x++; Out: return x;", "bool");
        Assert.That(errors, Is.Empty, string.Join(" | ", errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));
    }

    [Test]
    public void NearbyTopLevelVoidReturnStillCompiles()
    {
        var errors = CompileForType("if (x == 0) return x++; Out: return x;", "void");
        Assert.That(errors, Is.Empty, string.Join(" | ", errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));
    }

    [Test]
    public void NestedReturnAndTopLevelBoolReturnKeepRuntimeValues()
    {
        const string body = "int L1() { return x + 1; } x = L1(); Out: return x;";
        var original = Execute(body, "int", -1);
        var retyped = Execute(body, "bool", -1);
        Assert.That(original.Result, Is.EqualTo(0));
        Assert.That(retyped.Result, Is.EqualTo(false));

        original = Execute(body, "int", 0);
        retyped = Execute(body, "bool", 0);
        Assert.That(original.Result, Is.EqualTo(1));
        Assert.That(retyped.Result, Is.EqualTo(true));
    }

    [Test]
    public void NestedReturnAndTopLevelVoidReturnKeepRuntimeEffects()
    {
        const string body = "int L1() { return x + 1; } x = L1(); State = x; Out: return State++;";
        var original = Execute(body, "int", 0);
        var retyped = Execute(body, "void", 0);
        Assert.That(original.Result, Is.EqualTo(1));
        Assert.That(original.State, Is.EqualTo(2));
        Assert.That(retyped.Result, Is.Null);
        Assert.That(retyped.State, Is.EqualTo(original.State));
    }

    private static (object? Result, int State) Execute(string body, string type, int input)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(type);
        var generator = typeof(ControlFlowLongFuzzTests);
        var retyped = (string)Invoke(generator, "Retype", [body, type])!;
        var source = $"public static class C {{ public static int State; public static {type} Target(int x) {{ {retyped} }} }}";
        var compilation = (CSharpCompilation)Invoke(generator, "Compile", [source])!;
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        Assert.That(errors, Is.Empty, string.Join(" | ", errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));

        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.That(emission.Success, Is.True,
            string.Join(" | ", emission.Diagnostics.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));
        stream.Position = 0;
        var context = new AssemblyLoadContext("retype-regression", isCollectible: true);
        try
        {
            var subject = context.LoadFromStream(stream).GetType("C")!;
            var result = subject.GetMethod("Target")!.Invoke(null, [input]);
            return (result, (int)subject.GetField("State")!.GetValue(null)!);
        }
        finally
        {
            context.Unload();
        }
    }

    private static ImmutableArray<Diagnostic> CompileForType(string body, string type)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(type);
        var generator = typeof(ControlFlowLongFuzzTests);
        var retyped = (string)Invoke(generator, "Retype", [body, type])!;
        var runtime = (string)Invoke(generator, "Runtime", [retyped])!;
        var runtimeState = (string)generator.GetField("RuntimeState", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        var source = (string)Invoke(generator, "Source", [runtimeState,
            $"public static {type} Target(int x) {{ ", runtime])!;
        var compilation = (CSharpCompilation)Invoke(generator, "Compile", [source])!;
        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
    }

    private static object? Invoke(Type type, string name, ImmutableArray<object?> arguments)
    {
        if (arguments.IsDefault)
        { throw new ArgumentNullException(nameof(arguments)); }
        System.ArgumentNullException.ThrowIfNull(name);
        System.ArgumentNullException.ThrowIfNull(type);
        return type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments.ToArray());
    }
}
