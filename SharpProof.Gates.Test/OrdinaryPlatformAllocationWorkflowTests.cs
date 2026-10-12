using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
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
[NUnit.Framework.Platform("Linux")]
public sealed class OrdinaryPlatformAllocationWorkflowTests
{
    private const string Source = """
        public static class ScalarPlatform {
            public static int Target(int value) => value;
        }
        """;

    [Test]
    public async Task ActualRequires32BitSourceImageCannotLoadInTheCanonicalAmd64Runtime()
    {
        Assert.That(RuntimeInformation.ProcessArchitecture, Is.EqualTo(Architecture.X64));
        var declaration = CSharpSyntaxTree.ParseText(Source).GetCompilationUnitRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single();
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-platform-allocation.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(Source), Source);
        var method = new OpenSourceCorpusMethod("ordinary-Target", "fixture", file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Proven, CorpusSupport.Supported);
        var document = new OpenSourceCorpusDocument(2, [], [file], [method]);
        var original = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None, allocations: true);
        var x64 = original.WithOptions(original.Options.WithPlatform(Platform.X64));
        var x86 = original.WithOptions(original.Options.WithPlatform(Platform.X86));
        Assert.That(x64.GetDiagnostics().Where(static item => item.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(x86.GetDiagnostics().Where(static item => item.Severity == DiagnosticSeverity.Error), Is.Empty);
        var universe = OpenSourceCorpusCatalog.ComputeNormalizedSha256(JsonSerializer.Serialize(document));
        var identifiers = ImmutableArray.Create(method.Id);

        using var x64Image = NativeSourceEmission.EmitImage(x64, "Ordinary X64 emission failed: ", CancellationToken.None);
        using (var reader = new PEReader(x64Image, PEStreamOptions.LeaveOpen))
        {
            Assert.That(reader.PEHeaders.CoffHeader.Machine, Is.EqualTo(Machine.Amd64));
            Assert.That(reader.PEHeaders.CorHeader!.Flags & CorFlags.Requires32Bit, Is.EqualTo((CorFlags)0));
        }
        x64Image.Position = 0;
        var x64Context = new AssemblyLoadContext("OrdinaryPlatformX64", isCollectible: true);
        try
        {
            var actual = x64Context.LoadFromStream(x64Image).GetType("ScalarPlatform", throwOnError: true)!
                .GetMethod("Target")!.CreateDelegate<Func<int, int>>();
            Assert.That(actual(-7), Is.EqualTo(-7));
            Assert.That(actual(int.MaxValue), Is.EqualTo(int.MaxValue));
        }
        finally
        {
            x64Context.Unload();
        }
        var control = await NativeEffectOracleGate.ObserveAsync(x64, identifiers, RepositoryLayout.FindRoot(), universe,
            1, allocations: true);
        var row = control.Rows.Single();
        Assert.That(control.NativeProven, Is.EqualTo(1));
        Assert.That(row.NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(row.NativeReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(row.HasTotalBody, Is.True);
        Assert.That(row.HasBodyAbstraction, Is.False);
        Assert.That(row.RuntimeOracle, Is.EqualTo("Confirmed"));
        Assert.That(row.AllocationIlOracle, Is.EqualTo("NoReachableAllocationOpcode"));
        Assert.That(row.RuntimeChecks, Is.EqualTo(1));
        Assert.That(row.AllocatedBytes, Is.Zero);
        Assert.That(control.RuntimeContradictions, Is.Zero);
        Assert.That(control.Passed, Is.True);

        using var x86Image = NativeSourceEmission.EmitImage(x86, "Ordinary X86 emission failed: ", CancellationToken.None);
        using (var reader = new PEReader(x86Image, PEStreamOptions.LeaveOpen))
        {
            Assert.That(reader.PEHeaders.CoffHeader.Machine, Is.EqualTo(Machine.I386));
            Assert.That(reader.PEHeaders.CorHeader!.Flags & CorFlags.Requires32Bit, Is.EqualTo(CorFlags.Requires32Bit));
        }
        x86Image.Position = 0;
        var x86Context = new AssemblyLoadContext("OrdinaryPlatformX86", isCollectible: true);
        try
        {
            Assert.Throws<BadImageFormatException>(() => x86Context.LoadFromStream(x86Image));
        }
        finally
        {
            x86Context.Unload();
        }
        var failure = Assert.ThrowsAsync<BadImageFormatException>(() => NativeEffectOracleGate.ObserveAsync(x86,
            identifiers, RepositoryLayout.FindRoot(), universe, 1, allocations: true));
        Assert.That(failure!.StackTrace, Does.Contain("NativeAllocationWitnessOracle.EnsureAssembly"));
    }
}
