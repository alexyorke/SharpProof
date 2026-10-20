using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class ContractClaimCheckedConstantCompileRegressionTests
{
    [TestCase(360)]
    [TestCase(362)]
    public void GeneratedCheckedAdditionCompiles(int seed)
    {
        var (source, _, _) = new ContractClaimProgramGenerator(new Random(seed)).Program();
        if (seed == 362)
        {
            Assert.That(source, Does.Contain("int.MaxValue"));
            Assert.That(source, Does.Contain("checked("));
        }

        var errors = Compile(source).GetDiagnostics()
            .Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        Assert.That(errors, Is.Empty,
            $"seed {seed}: {string.Join(" | ", errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString())))}\n{source}");
    }

    [Test]
    public void GeneratedMaximumAdditionStillThrowsCheckedOverflow()
    {
        var (source, _, _) = new ContractClaimProgramGenerator(new Random(362)).Program();
        var target = LoadTarget(source);
        ImmutableArray<object?> arguments = [0, 0, new[] { 1 }, null];
        var exception = Assert.Throws<TargetInvocationException>((Action)(() => { target.Invoke(null, arguments.ToArray()); }));
        Assert.That(exception!.InnerException, Is.TypeOf<OverflowException>());
    }

    [Test]
    public void NeighboringNonOverflowAdditionReturnsNormally()
    {
        var (source, _, _) = new ContractClaimProgramGenerator(new Random(362)).Program();
        Assert.That(source.Split("int.MaxValue", StringSplitOptions.None).Length, Is.EqualTo(2));
        var nearbySource = source.Replace("int.MaxValue", "(int.MaxValue - 1)", StringComparison.Ordinal);
        var target = LoadTarget(nearbySource);
        ImmutableArray<object?> arguments = [0, 0, new[] { 1 }, null];
        Assert.That(target.Invoke(null, arguments.ToArray()), Is.EqualTo(0));
    }

    private static MethodInfo LoadTarget(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = Compile(source);
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        Assert.That(errors, Is.Empty, string.Join(" | ", errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.That(emit.Success, Is.True, string.Join(" | ", emit.Diagnostics));
        var assembly = Assembly.Load(image.ToArray());
        return assembly.GetType("C")!.GetMethod("Target")!;
    }

    private static CSharpCompilation Compile(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: []),
            "/project/Subject.cs", Encoding.UTF8);
        return CSharpCompilation.Create("CheckedConstantCompile" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
    }
}
