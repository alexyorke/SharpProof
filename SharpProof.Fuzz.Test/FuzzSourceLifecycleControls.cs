using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzSourceLifecycleControls
{
    private const string Finite = "public static bool Target(bool value) => value;";
    private const string Partial = Finite + " public static bool NormalCompletion(bool value) => true;";
    private const string LayoutPrefix = "public static BrokenLayout State; " + Finite +
        " [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)] " +
        "public struct BrokenLayout { [System.Runtime.InteropServices.FieldOffset(0)] public ";
    private const string LayoutSuffix = " First; [System.Runtime.InteropServices.FieldOffset(0)] public long Second; }";

    [Test]
    public void ActualInt64ReturnRejectsTheBooleanSourceContract()
    {
        const string finite = "public static long Target(bool value) => 7L;";
        var actual = CompileActualSource(finite);
        using var image = actual.Image;
        Assert.That(actual.ReturnType, Is.EqualTo(SpecialType.System_Int64));
        AssertLoadedTarget(image, 7L);
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        Func<Task> replay = async () => await FuzzRunner.ReplaySourceCasesAsync(
            [new(positive, finite, [true], Partial, [[true]])]);
        var exception = Assert.ThrowsAsync<InvalidOperationException>(replay);
        Assert.That(exception!.Message, Is.EqualTo("Source campaign methods require static Boolean return values."));
    }

    [Test]
    public void ActualOverlappingManagedLayoutRejectsClrTypeLoad()
    {
        const string finite = LayoutPrefix + "object?" + LayoutSuffix;
        var actual = CompileActualSource(finite);
        using var image = actual.Image;
        Assert.That(actual.ReturnType, Is.EqualTo(SpecialType.System_Boolean));
        var context = new AssemblyLoadContext("SharpProofSourceCampaignBoolean", isCollectible: true);
        try
        {
            image.Position = 0;
            Action load = () =>
            {
                context.LoadFromStream(image).GetType("SharpProofSourceCampaignBoolean");
            };
            Assert.Throws<TypeLoadException>(load);
        }
        finally
        {
            context.Unload();
        }
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        Func<Task> replay = async () => await FuzzRunner.ReplaySourceCasesAsync(
            [new(positive, finite, [true], Partial, [[true]])]);
        Assert.ThrowsAsync<TypeLoadException>(replay);
    }

    [Test]
    public async Task ActualOverlappingIntegerLayoutLoadsAndReplays()
    {
        const string finite = LayoutPrefix + "long" + LayoutSuffix;
        var actual = CompileActualSource(finite);
        using var image = actual.Image;
        Assert.That(actual.ReturnType, Is.EqualTo(SpecialType.System_Boolean));
        AssertLoadedTarget(image, true);
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        var result = await FuzzRunner.ReplaySourceCasesAsync(
            [new(positive, finite, [true], Partial, [[true]])]);
        Assert.That(result.Agreements, Is.EqualTo(1));
        Assert.That(result.Failures, Is.Empty);
    }

    private static (MemoryStream Image, SpecialType ReturnType) CompileActualSource(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var tree = CSharpSyntaxTree.ParseText(
            "#nullable enable\npublic static class SharpProofSourceCampaignBoolean {\n" + members + "\n}\n",
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("SharpProofSourceCampaignBoolean", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, checkOverflow: true,
                nullableContextOptions: NullableContextOptions.Enable));
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var symbol = compilation.GetSemanticModel(tree).GetDeclaredSymbol(method);
        Assert.That(symbol, Is.Not.Null);
        Assert.That(symbol!.IsStatic, Is.True);
        Assert.That(symbol.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        var emission = GeneratedCompilationEmitter.Emit(compilation, CancellationToken.None);
        try
        {
            Assert.That(emission.Emit.Success, Is.True, string.Join(Environment.NewLine, emission.Emit.Diagnostics));
            return (emission.Image, symbol.ReturnType.SpecialType);
        }
        catch
        {
            emission.Image.Dispose();
            throw;
        }
    }

    private static void AssertLoadedTarget(MemoryStream image, object expected)
    {
        System.ArgumentNullException.ThrowIfNull(expected);
        System.ArgumentNullException.ThrowIfNull(image);
        var context = new AssemblyLoadContext("SharpProofSourceCampaignBoolean", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("SharpProofSourceCampaignBoolean");
            Assert.That(runtime, Is.Not.Null);
            var target = runtime!.GetMethod("Target", BindingFlags.Public | BindingFlags.Static);
            Assert.That(target, Is.Not.Null);
            Assert.That(target!.Invoke(null, [true]), Is.EqualTo(expected));
        }
        finally
        {
            context.Unload();
        }
    }
}
