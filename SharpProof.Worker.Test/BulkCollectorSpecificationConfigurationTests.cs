using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class BulkCollectorSpecificationConfigurationTests
{
    [Test]
    public void CurrentSdkPackIsAdmittedButChangedCatalogVersionIsRejected()
    {
        var compilation = TestCompilation.Create("PackConfigurationBoundary", """
            public static class Subject
            {
                public static int Read() => System.Math.Max(7, -3);
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("System.Math")!.GetMembers("Max")
            .OfType<IMethodSymbol>().Single(candidate => candidate.Parameters.Length == 2 &&
                candidate.Parameters.All(parameter => parameter.Type.SpecialType == SpecialType.System_Int32));
        var configuration = CompilerSpecificationPackProvider.ResolveConfiguration(["dotnet.scalar"]);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        Assert.That(CompilerSpecificationPackSelection.IsValid(configuration.SpecificationPackIds,
            configuration.SpecificationPackCatalogVersion, configuration.SpecificationPackCatalogSha256), Is.True);
        var provider = new CompilerSpecificationPackProvider(factory, configuration);
        Assert.That(provider.ResolveTotal(method), Is.Not.Null);

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("PackConfigurationBoundary", isCollectible: true);
        try
        {
            image.Position = 0;
            var read = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Read")!;
            Assert.That(read.Invoke(null, null), Is.EqualTo(7));
        }
        finally { runtime.Unload(); }

        // This configuration is rejected input, never accepted model or proof authority.
        // Preserve every resolved field except the caller-supplied catalog version.
        configuration.SpecificationPackCatalogVersion += 1;
        Assert.That(CompilerSpecificationPackSelection.IsValid(configuration.SpecificationPackIds,
            configuration.SpecificationPackCatalogVersion, configuration.SpecificationPackCatalogSha256), Is.False);
        var error = Assert.Throws<InvalidOperationException>((Action)(() =>
            new CompilerSpecificationPackProvider(factory, configuration)));
        Assert.That(error!.Message, Is.EqualTo("The SharpProof specification-pack authority is not current."));
    }
}
