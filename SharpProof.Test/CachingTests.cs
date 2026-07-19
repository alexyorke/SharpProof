using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Analyzer.Engine;
using SharpProof.Attributes;

namespace SharpProof.Test;

[TestFixture]
public class CachingTests
{
    [Test]
    public void CompilationPurityService_StartsWithEmptyCaches()
    {
        using var fixture = CreateFixture(
            "EmptyPurityCacheTest",
            "public class C { public int M() => 1; }");

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Service.CachedPurityCount, Is.Zero);
            Assert.That(fixture.Service.CachedSemanticModelCount, Is.Zero);
        });
    }

    [Test]
    public void CompilationPurityService_CanceledPurityRequest_DoesNotPopulateCaches()
    {
        using var fixture = CreateFixture(
            "CanceledPurityRequestTest",
            """
            using SharpProof.Attributes;
            public class TestClass
            {
                [EnforcePure] public int Caller() => Shared();
                private int Shared() => 42;
            }
            """);
        var caller = fixture.Method("TestClass", "Caller");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => fixture.GetPurity(caller, cancellation.Token));
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Service.CachedPurityCount, Is.Zero);
            Assert.That(fixture.Service.CachedSemanticModelCount, Is.Zero);
        });
    }

    [Test]
    public void CompilationPurityService_CachesDistinctPurityRequests()
    {
        using var fixture = CreateFixture(
            "DistinctPurityRequestCachingTest",
            """
            using SharpProof.Attributes;
            public class TestClass
            {
                [EnforcePure] public int Caller1() => Shared();
                [EnforcePure] public int Caller2() => Shared();
                private int Shared() => 42;
            }
            """);

        Assert.That(fixture.GetPurity(fixture.Method("TestClass", "Caller1")).IsPure, Is.True);
        Assert.That(fixture.Service.CachedPurityCount, Is.EqualTo(1));
        Assert.That(fixture.GetPurity(fixture.Method("TestClass", "Caller2")).IsPure, Is.True);
        Assert.That(fixture.Service.CachedPurityCount, Is.EqualTo(2));
    }

    [Test]
    public void CompilationPurityService_RepeatedSameMethodRequest_DoesNotGrowPurityCache()
    {
        using var fixture = CreateFixture(
            "RepeatedSameMethodPurityCacheTest",
            """
            using SharpProof.Attributes;
            public class TestClass
            {
                [EnforcePure] public int Caller() => Shared();
                private int Shared() => 42;
            }
            """);
        var caller = fixture.Method("TestClass", "Caller");

        Assert.That(fixture.GetPurity(caller).IsPure, Is.True);
        Assert.That(fixture.GetPurity(caller).IsPure, Is.True);
        Assert.That(fixture.Service.CachedPurityCount, Is.EqualTo(1));
    }

    [Test]
    public void CompilationPurityService_DeepCallChainUsesMemoizedRootResults()
    {
        var methodBodies = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 25).Select(index => index == 24
                ? "private int M24() => 24;"
                : $"[EnforcePure] public int M{index}() => M{index + 1}();"));
        using var fixture = CreateFixture(
            "DeepCallChainCachingTest",
            $"using SharpProof.Attributes; public class TestClass {{ {methodBodies} }}");
        var root = fixture.Method("TestClass", "M0");
        var middle = fixture.Method("TestClass", "M12");

        Assert.That(fixture.GetPurity(root).IsPure, Is.True);
        Assert.That(fixture.Service.CachedPurityCount, Is.EqualTo(1));
        Assert.That(fixture.GetPurity(middle).IsPure, Is.True);
        Assert.That(fixture.Service.CachedPurityCount, Is.EqualTo(2));
        Assert.That(fixture.GetPurity(root).IsPure, Is.True);
        Assert.That(fixture.Service.CachedPurityCount, Is.EqualTo(2));
    }

    [Test]
    public void CompilationPurityService_DispatchHeavyQueriesUseMemoizedRootResults()
    {
        var callers = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 20).Select(index =>
                $"[EnforcePure] public int Caller{index}() => _provider.Get();"));
        using var fixture = CreateFixture(
            "DispatchHeavyCachingTest",
            $$"""
              using SharpProof.Attributes;
              public interface IProvider { int Get(); }
              public sealed class PureProvider : IProvider { public int Get() => 42; }
              public class TestClass
              {
                  private readonly IProvider _provider = new PureProvider();
                  {{callers}}
              }
              """);

        foreach (var name in new[] { "Caller0", "Caller10", "Caller19" })
            Assert.That(fixture.GetPurity(fixture.Method("TestClass", name)).IsPure, Is.True, name);
        Assert.That(fixture.Service.CachedPurityCount, Is.EqualTo(3));
    }

    [Test]
    public void CompilationPurityService_CachesSemanticModelsForRecursiveCrossTreeAnalysis()
    {
        using var fixture = CreateFixture(
            "SemanticModelCacheTest",
            """
            using SharpProof.Attributes;
            public class TestClass { [EnforcePure] public int Caller() => Helper.Shared(); }
            """,
            "public static class Helper { public static int Shared() => 42; }");

        Assert.That(fixture.GetPurity(fixture.Method("TestClass", "Caller")).IsPure, Is.True);
        Assert.That(fixture.Service.CachedSemanticModelCount, Is.EqualTo(2));
    }

    [Test]
    public async Task SharedImpureCallee_ReusedAcrossManyCallers_ReportsAllCallers()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:ImpureLeaf|}() => Console.WriteLine(""side effect"");
    [EnforcePure] public void {|SP0002:Caller1|}() => ImpureLeaf();
    [EnforcePure] public void {|SP0002:Caller2|}() => ImpureLeaf();
    [EnforcePure] public void {|SP0002:Caller3|}() => ImpureLeaf();
    [EnforcePure] public void {|SP0002:Caller4|}() => ImpureLeaf();
    [EnforcePure] public void {|SP0002:Caller5|}() => ImpureLeaf();
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    private static PurityFixture CreateFixture(string assemblyName, params string[] sources)
    {
        var syntaxTrees = sources
            .Select((source, index) => CSharpSyntaxTree.ParseText(source, path: $"Source{index}.cs"))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return new PurityFixture(compilation, new CompilationPurityService(compilation));
    }

    private sealed record PurityFixture(
        CSharpCompilation Compilation,
        CompilationPurityService Service) : IDisposable
    {
        internal IMethodSymbol Method(string typeName, string methodName) =>
            Compilation.GetTypeByMetadataName(typeName)!.GetMembers(methodName).OfType<IMethodSymbol>().Single();

        internal PurityAnalysisEngine.PurityAnalysisResult GetPurity(
            IMethodSymbol method,
            CancellationToken cancellationToken = default) => Service.GetPurity(
            method,
            Compilation.GetSemanticModel(method.DeclaringSyntaxReferences[0].SyntaxTree),
            Compilation.GetTypeByMetadataName(typeof(EnforcePureAttribute).FullName!)!,
            null,
            cancellationToken);

        public void Dispose() => Service.Dispose();
    }
}
