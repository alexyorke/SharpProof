using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ContractIntrinsicInterceptionContextTests
{
    private const string Source = """
        using SharpProof.Attributes;
        public static class Subject
        {
            public static long Identity(long value)
            {
                Contract.Requires(string.Equals("ready", "ready"));
                Contract.Ensures(Contract.Result<long>() == Contract.Old(value));
                return value;
            }
        }
        """;

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task EnablingInterceptorsDoesNotAbstainOnUninterceptedContractIntrinsics(bool featureEnabled, bool companion)
    {
        var compilation = CreateCompilation(featureEnabled, companion);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Identity").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var binder = new ContractBinder(compilation, factory);
        var binding = binder.BindTotal(new TotalLoweringContext(factory, method));
        Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
        Assert.That(binding.Clauses, Has.Length.EqualTo(2));
        var requires = binder.BindTotalRequires(new TotalLoweringContext(factory, method));
        Assert.That(requires.IsSuccess, Is.True, requires.Failure.ToString());
        Assert.That(requires.Clauses, Has.Length.EqualTo(1));
        var response = await VerifyAsync(compilation);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
    }

    [TestCase("Result")]
    [TestCase("Old")]
    public async Task ActualIntrinsicInterceptionRemainsUnknown(string intrinsic)
    {
        var compilation = CreateCompilation(featureEnabled: true);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync(TestContext.CurrentContext.CancellationToken);
        var syntax = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString().StartsWith("Contract." + intrinsic + "<", StringComparison.Ordinal) ||
                invocation.Expression.ToString() == "Contract." + intrinsic);
        var location = compilation.GetSemanticModel(tree).GetInterceptableLocation(syntax)!;
        Assert.That(location, Is.Not.Null);
        var options = (CSharpParseOptions)tree.Options;
        var attribute = CSharpSyntaxTree.ParseText("""
            namespace System.Runtime.CompilerServices
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
                public sealed class InterceptsLocationAttribute : global::System.Attribute
                {
                    public InterceptsLocationAttribute(int version, string data) { }
                }
            }
            """, options, path: "/project/InterceptsLocationAttribute.cs");
        var signature = intrinsic == "Result" ? "T Replacement<T>() => default!;" : "T Replacement<T>(T value) => value;";
        var interceptor = CSharpSyntaxTree.ParseText(
            "namespace Interception { public static class IntrinsicInterceptors { " +
            "[global::System.Runtime.CompilerServices.InterceptsLocation(" +
            location.Version.ToString(CultureInfo.InvariantCulture) + ", " +
            Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(location.Data!, quote: true) + ")] " +
            "public static " + signature + " } }", options, path: "/project/Interceptors.cs");
        compilation = compilation.AddSyntaxTrees(attribute, interceptor);
        TestCompilation.AssertNoErrors(compilation);
        Assert.That(compilation.GetSemanticModel(tree).GetInterceptorMethod(syntax)?.Name, Is.EqualTo("Replacement"));
        var response = await VerifyAsync(compilation);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(response.ClaimResults.Single().Reason, Is.EqualTo(WorkerClaimReason.UnsupportedExpression));
        Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Incomplete));
    }

    private static CSharpCompilation CreateCompilation(bool featureEnabled, bool companion = false)
    {
        var options = new CSharpParseOptions(LanguageVersion.CSharp13).WithFeatures(featureEnabled
            ? [new KeyValuePair<string, string>("InterceptorsNamespaces", "Interception")]
            : []);
        var source = companion ? """
            using SharpProof.Attributes;
            public static class Subject { public static long Identity(long value) => value; }
            [ContractFor(typeof(Subject))]
            public static class Specification
            {
                public static long Identity(long value)
                {
                    Contract.Requires(string.Equals("ready", "ready"));
                    Contract.Ensures(Contract.Result<long>() == Contract.Old(value));
                    return default;
                }
            }
            """ : Source;
        var tree = CSharpSyntaxTree.ParseText(source, options, path: "/project/Subject.cs");
        var compilation = CSharpCompilation.Create("ContractInterceptionContext", [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }

    private static async Task<WorkerVerifyResponse> VerifyAsync(CSharpCompilation compilation)
    {
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net10.0",
            WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        return await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
    }
}
