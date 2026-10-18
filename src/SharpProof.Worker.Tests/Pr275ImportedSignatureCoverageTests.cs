using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Contracts;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275ImportedSignatureCoverageTests
{
    [TestCase("in")]
    [TestCase("ref readonly")]
    public void ImportedReadonlyInputMatchesItsStaticCompanion(string modifier)
    {
        System.ArgumentNullException.ThrowIfNull(modifier);
        var compilation = ImportCompilerOutput($$"""
            public interface ITarget {
                void Read({{modifier}} int value);
            }
            public static class Companion {
                public static void Read(ITarget receiver, {{modifier}} int value) { }
            }
            """);
        var target = compilation.GetTypeByMetadataName("ITarget")!.GetMembers("Read")
            .OfType<IMethodSymbol>().Single();
        var companion = compilation.GetTypeByMetadataName("Companion")!.GetMembers("Read")
            .OfType<IMethodSymbol>().Single();

        Assert.That(target.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(target.Parameters.Single().RefCustomModifiers, Is.Not.Empty);
        Assert.That(ContractForSymbolMatcher.MemberSignaturesMatch(target, companion), Is.True);
    }

    [TestCase("Cdecl, SuppressGCTransition", "SuppressGCTransition, Cdecl", true)]
    [TestCase("Cdecl", "Cdecl", true)]
    [TestCase("Cdecl", "Stdcall", false)]
    public void ImportedFunctionPointerSignaturesPreserveTheirCallingConventions(
        string targetConventions, string companionConventions, bool matches)
    {
        System.ArgumentNullException.ThrowIfNull(companionConventions);
        System.ArgumentNullException.ThrowIfNull(targetConventions);
        var compilation = ImportCompilerOutput($$"""
            public unsafe interface ITarget {
                delegate* unmanaged[{{targetConventions}}]<int, int> Map(
                    delegate* unmanaged[{{targetConventions}}]<int, int> value);
            }
            public static unsafe class Companion {
                public static delegate* unmanaged[{{companionConventions}}]<int, int> Map(
                    ITarget receiver,
                    delegate* unmanaged[{{companionConventions}}]<int, int> value) => null;
            }
            """);
        var target = compilation.GetTypeByMetadataName("ITarget")!.GetMembers("Map")
            .OfType<IMethodSymbol>().Single();
        var companion = compilation.GetTypeByMetadataName("Companion")!.GetMembers("Map")
            .OfType<IMethodSymbol>().Single();

        Assert.That(target.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(target.ReturnType, Is.InstanceOf<IFunctionPointerTypeSymbol>());
        Assert.That(compilation.ClassifyConversion(target.ReturnType, companion.ReturnType).IsImplicit,
            Is.EqualTo(matches));
        Assert.That(ContractForSymbolMatcher.MemberSignaturesMatch(target, companion), Is.EqualTo(matches));
    }

    private static Microsoft.CodeAnalysis.CSharp.CSharpCompilation ImportCompilerOutput(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var producer = TestCompilation.Create("Pr275ImportedProducer", source, allowUnsafe: true);
        using var image = new MemoryStream();
        var emission = producer.Emit(image);
        Assert.That(emission.Success, Is.True,
            string.Join(Environment.NewLine, emission.Diagnostics));
        var reference = MetadataReference.CreateFromImage(image.ToArray());

        return TestCompilation.Create("Pr275ImportedConsumer", "public sealed class Consumer { }")
            .AddReferences(reference);
    }
}
