using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalMetadataSymbolReaderTests
{
    private static readonly string[] ExpectedSymbols = ["existing", "FIRST", "SECOND", "FIRST"];

    [Test]
    public void CompiledConditionalAttributesAppendSymbolsInMetadataOrder()
    {
        var compilation = TestCompilation.Create("ConditionalMetadataSymbols", """
            using System.Diagnostics;
            public static class Subject
            {
                [Conditional("FIRST"), Conditional("SECOND"), Conditional("FIRST")]
                public static void Target() { }
            }
            """);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)),
            Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        using var metadata = ModuleMetadata.CreateFromImage(image.ToArray());
        var reader = metadata.GetMetadataReader();
        Assert.That(reader.MetadataKind, Is.EqualTo(MetadataKind.Ecma335));
        var handle = reader.MethodDefinitions.Single(candidate =>
            reader.GetString(reader.GetMethodDefinition(candidate).Name) == "Target");
        var symbols = ImmutableArray.CreateBuilder<string>();
        symbols.Add("existing");

        ConditionalMetadataSymbolReader.AppendSymbols(reader, handle, symbols);

        Assert.That(symbols.ToImmutable(), Is.EqualTo(ExpectedSymbols));
    }

    [Test]
    public void SdkMethodDefinitionsPreserveExistingSymbols()
    {
        using var metadata = LoadSdkMetadata();
        var reader = metadata.GetMetadataReader();
        AssertSdkReader(reader);
        foreach (var handle in reader.MethodDefinitions)
        {
            Assert.That(reader.GetString(reader.GetMethodDefinition(handle).Name), Is.Not.Empty);
            Assert.That(ConditionalMethodIdentityPolicy.GetMethodDefinitionHandle(reader, MetadataTokens.GetToken(handle)),
                Is.EqualTo(handle));
            var symbols = ImmutableArray.CreateBuilder<string>();
            symbols.Add("existing");

            ConditionalMetadataSymbolReader.AppendSymbols(reader, handle, symbols);

            Assert.That(symbols, Has.Count.EqualTo(1));
            Assert.That(symbols[0], Is.EqualTo("existing"));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void UnavailableSdkMethodPreservesPreviouslyDecodedSymbols(bool nilHandle)
    {
        using var metadata = LoadSdkMetadata();
        var reader = metadata.GetMetadataReader();
        AssertSdkReader(reader);
        var handle = nilHandle
            ? default
            : MetadataTokens.MethodDefinitionHandle(reader.MethodDefinitions.Count + 2);
        Assert.Throws<BadImageFormatException>(new Action(() =>
        {
            _ = reader.GetMethodDefinition(handle);
        }));
        var token = MetadataTokens.GetToken(handle);
        Assert.Throws<BadImageFormatException>(new Action(() =>
        {
            _ = ConditionalMethodIdentityPolicy.GetMethodDefinitionHandle(reader, token);
        }));
        var symbols = ImmutableArray.CreateBuilder<string>();
        symbols.Add("existing");

        ConditionalMetadataSymbolReader.AppendSymbols(reader, handle, symbols);

        Assert.That(symbols, Has.Count.EqualTo(1));
        Assert.That(symbols[0], Is.EqualTo("existing"));
    }

    private static ModuleMetadata LoadSdkMetadata()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures",
            "Windows.Media.Capture.CameraCaptureUIContract.winmd");
        var bytes = File.ReadAllBytes(path);
        Assert.That(bytes, Has.Length.EqualTo(3072));
        Assert.That(Convert.ToHexString(SHA256.HashData(bytes)),
            Is.EqualTo("5F9EE2BFF39D954EDFDB88E985B7F6F42CBB6DB98D6D2EFE05DB9035B66856FB"));
        return ModuleMetadata.CreateFromImage(bytes);
    }

    private static void AssertSdkReader(MetadataReader reader)
    {
        System.ArgumentNullException.ThrowIfNull(reader);
        Assert.That(reader.MetadataVersion, Is.EqualTo("WindowsRuntime 1.4"));
        Assert.That(reader.MetadataKind, Is.EqualTo(MetadataKind.WindowsMetadata));
        Assert.That(reader.MethodDefinitions, Has.Count.EqualTo(2));
        Assert.That(reader.GetTableRowCount(TableIndex.MethodPtr), Is.Zero);
    }
}
