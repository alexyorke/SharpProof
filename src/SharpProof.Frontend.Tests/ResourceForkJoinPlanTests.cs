using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ResourceForkJoinPlanTests
{
    [Test]
    public void ConfiguredAsyncRootRejectsACustomMethodBuilder()
    {
        const string builder = """
            public struct B {
              private System.Runtime.CompilerServices.AsyncTaskMethodBuilder inner;
              public static B Create() => new B { inner = System.Runtime.CompilerServices.AsyncTaskMethodBuilder.Create() };
              public Task Task => inner.Task;
              public void SetResult() => inner.SetResult();
              public void SetException(System.Exception exception) => inner.SetException(exception);
              public void SetStateMachine(System.Runtime.CompilerServices.IAsyncStateMachine state) => inner.SetStateMachine(state);
              public void Start<T>(ref T state) where T : System.Runtime.CompilerServices.IAsyncStateMachine => inner.Start(ref state);
              public void AwaitOnCompleted<A,T>(ref A awaiter, ref T state)
                where A : System.Runtime.CompilerServices.INotifyCompletion
                where T : System.Runtime.CompilerServices.IAsyncStateMachine => inner.AwaitOnCompleted(ref awaiter, ref state);
              public void AwaitUnsafeOnCompleted<A,T>(ref A awaiter, ref T state)
                where A : System.Runtime.CompilerServices.ICriticalNotifyCompletion
                where T : System.Runtime.CompilerServices.IAsyncStateMachine => inner.AwaitUnsafeOnCompleted(ref awaiter, ref state);
            }
            """;
        var (compilation, method) = Create("=> await Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(3))).ConfigureAwait(false);",
            "[System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(B))] public static async Task Target(int n)", builder);
        Assert.That(ResourceForkJoinPlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [TestCase("=> await Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(3))).ConfigureAwait(false);", true)]
    [TestCase("{ await Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(3))).ConfigureAwait(false); }", true)]
    [TestCase("=> await Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(3)));", false)]
    [TestCase("=> await Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(3))).ConfigureAwait(true);", false)]
    [TestCase("=> await Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(3))).ConfigureAwait(n > 0);", false)]
    [TestCase("{ Child(n); await Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(3))).ConfigureAwait(false); }", false)]
    [TestCase("{ await Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(3))).ConfigureAwait(false); Child(n); }", false)]
    [TestCase("{ await Task.CompletedTask.ConfigureAwait(false); await Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(3))).ConfigureAwait(false); }", false)]
    public void ConfiguredAsyncRootHasOneClosedFrameworkAwait(string body, bool expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (compilation, method) = Create(body, "public static async Task Target(int n)");
        var plan = ResourceForkJoinPlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(plan != null, Is.EqualTo(expected));
        if (plan != null)
        {
            Assert.That(plan.ConfiguredAwait, Is.True);
            Assert.That(plan.Children, Has.Length.EqualTo(2));
        }
    }

    [TestCase("=> Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(3)));")]
    [TestCase("{ return Task.WhenAll(Task.Run(() => { Child(n); }), Task.Run(() => Child(-1))); }")]
    [TestCase("=> Task.WhenAll(new Task[] { Task.Run(() => Child(n)), Task.Run(() => Child(0)) });")]
    public void RecognizesClosedJoinsAndPreservesActualChildOperations(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (compilation, method) = Create(body);
        var plan = ResourceForkJoinPlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(plan, Is.Not.Null);
        Assert.That(plan!.Join.TargetMethod.Name, Is.EqualTo("WhenAll"));
        Assert.That(plan.Children.Length, Is.EqualTo(2));
        Assert.That(plan.Children[0].TargetMethod.Name, Is.EqualTo("Child"));
        Assert.That(plan.Children[0].Arguments[0].Value.Syntax.ToString(), Is.EqualTo("n"));
        Assert.That(plan.Children[0].SemanticModel!.Compilation, Is.SameAs(compilation));
    }

    [TestCase("=> Task.WhenAll(Task.Run(() => Child(n)));")]
    [TestCase("=> Task.WhenAll(Task.Run(() => Child(n)), Task.Run(async () => { await Task.Yield(); Child(n); }));")]
    [TestCase("=> Task.WhenAll(Task.Run(() => Child(n), default(System.Threading.CancellationToken)), Task.Run(() => Child(n)));")]
    [TestCase("=> Task.WhenAll(Task.Run(() => { Child(n); Child(n); }), Task.Run(() => Child(n)));")]
    [TestCase("=> Task.WhenAll(Task.Run(() => Child(n + 1)), Task.Run(() => Child(n)));")]
    [TestCase("{ int local = n; return Task.WhenAll(Task.Run(() => Child(local)), Task.Run(() => Child(n))); }")]
    [TestCase("{ Child(n); return Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(n))); }")]
    [TestCase("=> Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => System.Threading.Thread.Sleep(n)));")]
    [TestCase("=> Task.WhenAll(Task.Run(() => { }), Task.Run(() => Child(n)));")]
    [TestCase("=> Task.WhenAll(Task.Run(() => { return; }), Task.Run(() => Child(n)));")]
    [TestCase("=> Task.WhenAll(Task.Run(MethodGroup), Task.Run(() => Child(n)));")]
    [TestCase("=> Task.WhenAll(Task.Run(() => Value(n)), Task.Run(() => Child(n)));")]
    public void UnsupportedShapesHaveNoPlan(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (compilation, method) = Create(body);
        Assert.That(ResourceForkJoinPlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [TestCase("static void Child<T>(int n) { }", "Child<int>(n)")]
    [TestCase("static async void Child(int n) { await Task.Yield(); }", "Child(n)")]
    [TestCase("[System.Diagnostics.Conditional(\"NEVER\")] static void Child(int n) { }", "Child(n)")]
    [TestCase("static void Child(ref int n) { }", "Child(ref n)")]
    [TestCase("static void Child(int n, int other = 0) { }", "Child(n)")]
    [TestCase("static void Child(long n) { }", "Child(n)")]
    public void SourceCallbacksStillRequireTheClosedSignature(string child, string call)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(call);
        var tree = CSharpSyntaxTree.ParseText("using System.Threading.Tasks; public static class C { public static Task Target(int n) => Task.WhenAll(Task.Run(() => " + call + "),Task.Run(() => " + call + ")); " + child + " }");
        var compilation = CSharpCompilation.Create("UnsupportedCallback", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(ResourceForkJoinPlan.TryCreate(Method(compilation, tree), compilation, TestContext.CurrentContext.CancellationToken), Is.Null);
    }
    [TestCase(2, true)]
    [TestCase(16, true)]
    [TestCase(17, false)]
    public void ClosedJoinHasAnExplicitChildBudget(int count, bool expected)
    {
        var calls = string.Join(",", Enumerable.Repeat("Task.Run(() => Child(n))", count));
        var (compilation, method) = Create("=> Task.WhenAll(" + calls + ");");
        Assert.That(ResourceForkJoinPlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken) != null, Is.EqualTo(expected));
    }

    [TestCase("public", "static", "", true)]
    [TestCase("public", "", "", false)]
    [TestCase("public", "static", "<T>", false)]
    [TestCase("public", "static", "", false, "static C() { }")]
    public void RootAdmissionRequiresAStaticNonGenericInitializerFreeOwner(string visibility, string modifier, string generic, bool expected, string initializer = "")
    {
        ArgumentNullException.ThrowIfNull(visibility);
        ArgumentNullException.ThrowIfNull(modifier);
        ArgumentNullException.ThrowIfNull(generic);
        ArgumentNullException.ThrowIfNull(initializer);
        var (compilation, method) = Create("=> Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(n)));",
            visibility + " " + modifier + " Task Target" + generic + "(int n)", initializer);
        Assert.That(ResourceForkJoinPlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken) != null, Is.EqualTo(expected));
    }

    [Test]
    public void MissingAndForeignCompilationsHaveNoPlan()
    {
        var (compilation, method) = Create("=> Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(n)));");
        Assert.That(ResourceForkJoinPlan.TryCreate(method, null, TestContext.CurrentContext.CancellationToken), Is.Null);
        var foreign = compilation.RemoveAllSyntaxTrees();
        Assert.That(ResourceForkJoinPlan.TryCreate(method, foreign, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [Test]
    public void CancellationIsNotConvertedIntoAnUnsupportedShape()
    {
        var (compilation, method) = Create("=> Task.WhenAll(Task.Run(() => Child(n)), Task.Run(() => Child(n)));");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action recognize = () => { _ = ResourceForkJoinPlan.TryCreate(method, compilation, cancellation.Token); };
        Assert.Throws<OperationCanceledException>(recognize);
    }

    [Test]
    public void SourceDefinedFrameworkNamesAreNotAuthenticated()
    {
        const string source = "namespace System.Threading.Tasks { public class Task { public static Task Run(System.Action a) => new Task(); public static Task WhenAll(params Task[] tasks) => new Task(); } } " +
            "public static class C { public static System.Threading.Tasks.Task Target(int n) => System.Threading.Tasks.Task.WhenAll(System.Threading.Tasks.Task.Run(() => Child(n)),System.Threading.Tasks.Task.Run(() => Child(n))); static void Child(int n) { } }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("SpoofedTask", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = Method(compilation, tree);
        Assert.That(ResourceForkJoinPlan.TryCreate(method, compilation, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Method) Create(string body,
        string signature = "public static Task Target(int n)", string initializer = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(initializer);
        var tree = CSharpSyntaxTree.ParseText("using System.Threading.Tasks; public class C { " + initializer + " " + signature + body +
            " static void Child(int n) { } static void MethodGroup() { } static int Value(int n) => n; }");
        var compilation = CSharpCompilation.Create("ClosedJoin", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        return (compilation, Method(compilation, tree));
    }

    private static IMethodSymbol Method(CSharpCompilation compilation, SyntaxTree tree)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        return (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
    }
}
