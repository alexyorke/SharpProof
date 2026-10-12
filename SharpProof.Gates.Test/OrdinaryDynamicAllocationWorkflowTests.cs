using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryDynamicAllocationWorkflowTests
{
    private const string Source = """
        public static class OrdinaryParameters {
            public static int Target(dynamic value) => 1;
            public static int ObjectControl(object value) => 1;
        }
        """;

    [Test]
    public async Task ActualDynamicSourceParameterRetainsItsConservativeRuntimeBindingGap()
    {
        var root = CSharpSyntaxTree.ParseText(Source).GetCompilationUnitRoot();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-dynamic-allocation.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(Source), Source);
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Select(declaration =>
        {
            var span = declaration.GetLocation().GetLineSpan();
            return new OpenSourceCorpusMethod("ordinary-" + declaration.Identifier.ValueText, "fixture", file.Path,
                span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
                OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
                declaration.Identifier.ValueText, "effects", CorpusVerdict.Proven, CorpusSupport.Supported);
        }).ToImmutableArray();
        var document = new OpenSourceCorpusDocument(2, [], [file], methods);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None, allocations: true);
        Assert.That(compilation.GetDiagnostics().Where(static item => item.Severity == DiagnosticSeverity.Error), Is.Empty);
        var owner = compilation.GetTypeByMetadataName("OrdinaryParameters")!;
        var dynamicSymbol = (IMethodSymbol)owner.GetMembers("Target").Single();
        var objectSymbol = (IMethodSymbol)owner.GetMembers("ObjectControl").Single();
        Assert.That(dynamicSymbol.Parameters.Single().Type, Is.InstanceOf<IDynamicTypeSymbol>());
        Assert.That(dynamicSymbol.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.None));
        Assert.That(objectSymbol.Parameters.Single().Type, Is.InstanceOf<INamedTypeSymbol>());
        Assert.That(objectSymbol.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Object));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("OrdinaryDynamicAllocation", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("OrdinaryParameters", throwOnError: true)!;
            var target = type.GetMethod("Target")!;
            var control = type.GetMethod("ObjectControl")!;
            Assert.That(target.GetParameters().Single().ParameterType, Is.EqualTo(typeof(object)));
            Assert.That(target.GetParameters().Single().IsDefined(typeof(DynamicAttribute), inherit: false), Is.True);
            Assert.That(control.GetParameters().Single().ParameterType, Is.EqualTo(typeof(object)));
            Assert.That(control.GetParameters().Single().IsDefined(typeof(DynamicAttribute), inherit: false), Is.False);
            Assert.That(target.CreateDelegate<Func<object?, int>>()(null), Is.EqualTo(1));
            Assert.That(control.CreateDelegate<Func<object?, int>>()(null), Is.EqualTo(1));
        }
        finally
        {
            context.Unload();
        }

        var universe = OpenSourceCorpusCatalog.ComputeNormalizedSha256(JsonSerializer.Serialize(document));
        var report = await NativeEffectOracleGate.ObserveAsync(compilation,
            methods.Select(static method => method.Id).ToImmutableArray(), RepositoryLayout.FindRoot(), universe,
            methods.Length, allocations: true);
        Assert.That(report.UniverseMethodCount, Is.EqualTo(2));
        Assert.That(report.CheckedMethodCount, Is.EqualTo(2));
        Assert.That(report.Exhaustive, Is.True);
        Assert.That(report.NativeProven, Is.EqualTo(2));
        Assert.That(report.NativeRefuted, Is.Zero);
        Assert.That(report.NativeUnknownReasons, Is.Empty);
        Assert.That(report.Rows.Select(static row => row.HasTotalBody), Is.All.True);
        Assert.That(report.Rows.Select(static row => row.HasBodyAbstraction), Is.All.False);
        var dynamicRow = report.Rows.Single(static row => row.MethodId == "ordinary-Target");
        Assert.That(dynamicRow.NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(dynamicRow.NativeReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(dynamicRow.RuntimeOracle, Is.EqualTo("UnboundGenericConstraint"));
        Assert.That(dynamicRow.AllocationIlOracle, Is.EqualTo("NotRun"));
        Assert.That(dynamicRow.RuntimeChecks, Is.Zero);
        Assert.That(dynamicRow.AllocatedBytes, Is.Null);
        var controlRow = report.Rows.Single(static row => row.MethodId == "ordinary-ObjectControl");
        Assert.That(controlRow.NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(controlRow.RuntimeOracle, Is.EqualTo("Confirmed"));
        Assert.That(controlRow.AllocationIlOracle, Is.EqualTo("NoReachableAllocationOpcode"));
        Assert.That(controlRow.RuntimeChecks, Is.EqualTo(1));
        Assert.That(controlRow.AllocatedBytes, Is.Zero);
        Assert.That(report.RuntimeWitnesses, Is.EqualTo(1));
        Assert.That(report.RuntimeContradictions, Is.Zero);
        Assert.That(report.Passed, Is.True);
    }
}
