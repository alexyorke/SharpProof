using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;
using static SharpProof.Test.TestReflectionFacts;

namespace SharpProof.Test;

[TestFixture]
public class CachingTests
{
    [Test]
    public void CompilationPurityService_DoesNotBuildCallGraphInConstructor()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("public class C { public int M() => 1; }");
        var compilation = CSharpCompilation.Create(
            "LazyCallGraphTest",
            new[] { syntaxTree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var serviceType =
            typeof(SharpProofAnalyzer).Assembly.GetType("SharpProof.Analyzer.Engine.CompilationPurityService", true)!;
        var service = Activator.CreateInstance(
            serviceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { compilation },
            null);

        var callGraphField = serviceType.GetField("_callGraph", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.That(callGraphField.GetValue(service), Is.Null);
    }

    [Test]
    public void CompilationPurityService_CanceledPurityRequest_DoesNotBuildCallGraph()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Caller() => Shared();

    private int Shared() => 42;
}");
        var compilation = CSharpCompilation.Create(
            "CanceledPurityRequestTest",
            new[] { syntaxTree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var enforcePureAttributeSymbol = compilation.GetTypeByMetadataName(typeof(EnforcePureAttribute).FullName!)!;
        var testClass = compilation.GetTypeByMetadataName("TestClass")!;
        var caller = testClass.GetMembers("Caller").OfType<IMethodSymbol>().Single();

        var serviceType =
            typeof(SharpProofAnalyzer).Assembly.GetType("SharpProof.Analyzer.Engine.CompilationPurityService", true)!;
        var service = Activator.CreateInstance(
            serviceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { compilation },
            null);
        var callGraphField = serviceType.GetField("_callGraph", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var getPurityMethod = serviceType.GetMethod("GetPurity", BindingFlags.Instance | BindingFlags.Public)!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = Assert.Throws<TargetInvocationException>(() => getPurityMethod.Invoke(service,
            new object?[] { caller, semanticModel, enforcePureAttributeSymbol, null, cancellation.Token }));

        Assert.That(exception!.InnerException, Is.TypeOf<OperationCanceledException>());
        Assert.That(callGraphField.GetValue(service), Is.Null);
    }

    [Test]
    public void CompilationPurityService_ReusesLazyCallGraphAcrossRepeatedPurityRequests()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Caller1() => Shared();

    [EnforcePure]
    public int Caller2() => Shared();

    private int Shared() => 42;
}");
        var compilation = CSharpCompilation.Create(
            "RepeatedPurityRequestCachingTest",
            new[] { syntaxTree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var enforcePureAttributeSymbol = compilation.GetTypeByMetadataName(typeof(EnforcePureAttribute).FullName!)!;
        var testClass = compilation.GetTypeByMetadataName("TestClass")!;
        var caller1 = testClass.GetMembers("Caller1").OfType<IMethodSymbol>().Single();
        var caller2 = testClass.GetMembers("Caller2").OfType<IMethodSymbol>().Single();

        var serviceType =
            typeof(SharpProofAnalyzer).Assembly.GetType("SharpProof.Analyzer.Engine.CompilationPurityService", true)!;
        var service = Activator.CreateInstance(
            serviceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { compilation },
            null);
        var callGraphField = serviceType.GetField("_callGraph", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var getPurityMethod = serviceType.GetMethod("GetPurity", BindingFlags.Instance | BindingFlags.Public)!;

        Assert.That(callGraphField.GetValue(service), Is.Null);

        var firstResult = getPurityMethod.Invoke(service,
            new object?[] { caller1, semanticModel, enforcePureAttributeSymbol, null, CancellationToken.None });
        var firstCallGraph = callGraphField.GetValue(service);

        Assert.That(firstResult, Is.Not.Null);
        Assert.That(firstCallGraph, Is.Not.Null);

        var secondResult = getPurityMethod.Invoke(service,
            new object?[] { caller2, semanticModel, enforcePureAttributeSymbol, null, CancellationToken.None });

        Assert.That(secondResult, Is.Not.Null);
        Assert.That(callGraphField.GetValue(service), Is.SameAs(firstCallGraph));
    }

    [Test]
    public void CompilationPurityService_RepeatedSameMethodRequest_DoesNotGrowPurityCache()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Caller() => Shared();

    private int Shared() => 42;
}");
        var compilation = CSharpCompilation.Create(
            "RepeatedSameMethodPurityCacheTest",
            new[] { syntaxTree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var enforcePureAttributeSymbol = compilation.GetTypeByMetadataName(typeof(EnforcePureAttribute).FullName!)!;
        var testClass = compilation.GetTypeByMetadataName("TestClass")!;
        var caller = testClass.GetMembers("Caller").OfType<IMethodSymbol>().Single();

        var serviceType =
            typeof(SharpProofAnalyzer).Assembly.GetType("SharpProof.Analyzer.Engine.CompilationPurityService", true)!;
        var service = Activator.CreateInstance(
            serviceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { compilation },
            null);
        var callGraphField = serviceType.GetField("_callGraph", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var purityCacheField = serviceType.GetField("_purityCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var getPurityMethod = serviceType.GetMethod("GetPurity", BindingFlags.Instance | BindingFlags.Public)!;

        Assert.That(GetCount(purityCacheField.GetValue(service)!), Is.EqualTo(0));
        Assert.That(callGraphField.GetValue(service), Is.Null);

        var firstResult = getPurityMethod.Invoke(service,
            new object?[] { caller, semanticModel, enforcePureAttributeSymbol, null, CancellationToken.None });
        var builtCallGraph = callGraphField.GetValue(service);

        Assert.That(firstResult, Is.Not.Null);
        Assert.That(builtCallGraph, Is.Not.Null);
        Assert.That(GetCount(purityCacheField.GetValue(service)!), Is.EqualTo(1));

        var secondResult = getPurityMethod.Invoke(service,
            new object?[] { caller, semanticModel, enforcePureAttributeSymbol, null, CancellationToken.None });

        Assert.That(secondResult, Is.Not.Null);
        Assert.That(callGraphField.GetValue(service), Is.SameAs(builtCallGraph));
        Assert.That(GetCount(purityCacheField.GetValue(service)!), Is.EqualTo(1));
    }

    [Test]
    public void CompilationPurityService_ReusesCallGraphAcrossDeepCallChainRequests()
    {
        var methodBodies = string.Join(
            Environment.NewLine + Environment.NewLine,
            Enumerable.Range(0, 25).Select(index =>
                index == 24
                    ? "    private int M24() => 24;"
                    : $"    [EnforcePure] public int M{index}() => M{index + 1}();"));

        var syntaxTree = CSharpSyntaxTree.ParseText($@"
using SharpProof.Attributes;

public class TestClass
{{
{methodBodies}
}}");
        var compilation = CSharpCompilation.Create(
            "DeepCallChainCachingTest",
            new[] { syntaxTree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var enforcePureAttributeSymbol = compilation.GetTypeByMetadataName(typeof(EnforcePureAttribute).FullName!)!;
        var testClass = compilation.GetTypeByMetadataName("TestClass")!;
        var m0 = testClass.GetMembers("M0").OfType<IMethodSymbol>().Single();
        var m12 = testClass.GetMembers("M12").OfType<IMethodSymbol>().Single();

        var serviceType =
            typeof(SharpProofAnalyzer).Assembly.GetType("SharpProof.Analyzer.Engine.CompilationPurityService", true)!;
        var service = Activator.CreateInstance(
            serviceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { compilation },
            null);
        var callGraphField = serviceType.GetField("_callGraph", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var purityCacheField = serviceType.GetField("_purityCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var getPurityMethod = serviceType.GetMethod("GetPurity", BindingFlags.Instance | BindingFlags.Public)!;

        Assert.That(GetCount(purityCacheField.GetValue(service)!), Is.EqualTo(0));
        Assert.That(callGraphField.GetValue(service), Is.Null);

        var firstResult = getPurityMethod.Invoke(service,
            new object?[] { m0, semanticModel, enforcePureAttributeSymbol, null, CancellationToken.None });
        var builtCallGraph = callGraphField.GetValue(service);

        Assert.That(firstResult, Is.Not.Null);
        Assert.That(builtCallGraph, Is.Not.Null);
        Assert.That(GetCount(purityCacheField.GetValue(service)!), Is.EqualTo(1));

        var secondResult = getPurityMethod.Invoke(service,
            new object?[] { m12, semanticModel, enforcePureAttributeSymbol, null, CancellationToken.None });

        Assert.That(secondResult, Is.Not.Null);
        Assert.That(callGraphField.GetValue(service), Is.SameAs(builtCallGraph));
        Assert.That(GetCount(purityCacheField.GetValue(service)!), Is.EqualTo(2));

        var thirdResult = getPurityMethod.Invoke(service,
            new object?[] { m0, semanticModel, enforcePureAttributeSymbol, null, CancellationToken.None });

        Assert.That(thirdResult, Is.Not.Null);
        Assert.That(callGraphField.GetValue(service), Is.SameAs(builtCallGraph));
        Assert.That(GetCount(purityCacheField.GetValue(service)!), Is.EqualTo(2));
    }

    [Test]
    public void CompilationPurityService_ReusesFixedPointAcrossDispatchHeavyQueries()
    {
        var callerBodies = string.Join(
            Environment.NewLine + Environment.NewLine,
            Enumerable.Range(0, 20).Select(index =>
                $"    [EnforcePure] public int Caller{index}() => _provider.Get();"));

        var syntaxTree = CSharpSyntaxTree.ParseText($@"
using SharpProof.Attributes;

public interface IProvider
{{
    int Get();
}}

public sealed class PureProvider : IProvider
{{
    public int Get() => 42;
}}

public class TestClass
{{
    private readonly IProvider _provider = new PureProvider();

{callerBodies}
}}");
        var compilation = CSharpCompilation.Create(
            "DispatchHeavyCachingTest",
            new[] { syntaxTree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var enforcePureAttributeSymbol = compilation.GetTypeByMetadataName(typeof(EnforcePureAttribute).FullName!)!;
        var testClass = compilation.GetTypeByMetadataName("TestClass")!;
        var caller0 = testClass.GetMembers("Caller0").OfType<IMethodSymbol>().Single();
        var caller10 = testClass.GetMembers("Caller10").OfType<IMethodSymbol>().Single();
        var caller19 = testClass.GetMembers("Caller19").OfType<IMethodSymbol>().Single();

        var serviceType =
            typeof(SharpProofAnalyzer).Assembly.GetType("SharpProof.Analyzer.Engine.CompilationPurityService", true)!;
        var service = Activator.CreateInstance(
            serviceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { compilation },
            null);
        var callGraphField = serviceType.GetField("_callGraph", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fixedPointField = serviceType.GetField("_fixedPoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var purityCacheField = serviceType.GetField("_purityCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var getPurityMethod = serviceType.GetMethod("GetPurity", BindingFlags.Instance | BindingFlags.Public)!;

        Assert.That(callGraphField.GetValue(service), Is.Null);
        Assert.That(fixedPointField.GetValue(service), Is.Null);
        Assert.That(GetCount(purityCacheField.GetValue(service)!), Is.EqualTo(0));

        var firstResult = getPurityMethod.Invoke(service,
            new object?[] { caller0, semanticModel, enforcePureAttributeSymbol, null, CancellationToken.None });
        var builtCallGraph = callGraphField.GetValue(service);
        var builtFixedPoint = fixedPointField.GetValue(service);

        Assert.That(firstResult, Is.Not.Null);
        Assert.That(builtCallGraph, Is.Not.Null);
        Assert.That(builtFixedPoint, Is.Not.Null);
        Assert.That(GetCount(purityCacheField.GetValue(service)!), Is.EqualTo(1));

        var secondResult = getPurityMethod.Invoke(service,
            new object?[] { caller10, semanticModel, enforcePureAttributeSymbol, null, CancellationToken.None });

        Assert.That(secondResult, Is.Not.Null);
        Assert.That(callGraphField.GetValue(service), Is.SameAs(builtCallGraph));
        Assert.That(fixedPointField.GetValue(service), Is.SameAs(builtFixedPoint));
        Assert.That(GetCount(purityCacheField.GetValue(service)!), Is.EqualTo(2));

        var thirdResult = getPurityMethod.Invoke(service,
            new object?[] { caller19, semanticModel, enforcePureAttributeSymbol, null, CancellationToken.None });

        Assert.That(thirdResult, Is.Not.Null);
        Assert.That(callGraphField.GetValue(service), Is.SameAs(builtCallGraph));
        Assert.That(fixedPointField.GetValue(service), Is.SameAs(builtFixedPoint));
        Assert.That(GetCount(purityCacheField.GetValue(service)!), Is.EqualTo(3));
    }

    [Test]
    public void CompilationPurityService_CachesSemanticModelsDuringFixedPointBuild()
    {
        var callerTree = CSharpSyntaxTree.ParseText(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Caller() => Helper.Shared();
}");
        var helperTree = CSharpSyntaxTree.ParseText(@"
public static class Helper
{
    public static int Shared() => 42;
}");
        var compilation = CSharpCompilation.Create(
            "SemanticModelCacheTest",
            new[] { callerTree, helperTree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(callerTree);
        var enforcePureAttributeSymbol = compilation.GetTypeByMetadataName(typeof(EnforcePureAttribute).FullName!)!;
        var testClass = compilation.GetTypeByMetadataName("TestClass")!;
        var caller = testClass.GetMembers("Caller").OfType<IMethodSymbol>().Single();

        var serviceType =
            typeof(SharpProofAnalyzer).Assembly.GetType("SharpProof.Analyzer.Engine.CompilationPurityService", true)!;
        var service = Activator.CreateInstance(
            serviceType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { compilation },
            null);
        var semanticModelCacheField =
            serviceType.GetField("_semanticModelCache", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var getPurityMethod = serviceType.GetMethod("GetPurity", BindingFlags.Instance | BindingFlags.Public)!;

        Assert.That(GetCount(semanticModelCacheField.GetValue(service)!), Is.EqualTo(0));

        var result = getPurityMethod.Invoke(service,
            new object?[] { caller, semanticModel, enforcePureAttributeSymbol, null, CancellationToken.None });

        Assert.That(result, Is.Not.Null);
        Assert.That(GetCount(semanticModelCacheField.GetValue(service)!), Is.EqualTo(2));
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
    public void {|SP0002:ImpureLeaf|}()
    {
        Console.WriteLine(""side effect"");
    }

    [EnforcePure]
    public void {|SP0002:Caller1|}() => ImpureLeaf();

    [EnforcePure]
    public void {|SP0002:Caller2|}() => ImpureLeaf();

    [EnforcePure]
    public void {|SP0002:Caller3|}() => ImpureLeaf();

    [EnforcePure]
    public void {|SP0002:Caller4|}() => ImpureLeaf();

    [EnforcePure]
    public void {|SP0002:Caller5|}() => ImpureLeaf();
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

}
