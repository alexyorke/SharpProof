using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.IO.Compression;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class SymbolPackageValidatorCoverageTests
{
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string PackageId = "SharpProof.SymbolFixture";
    private const string PackageVersion = "1.0.0";
    private const string AssemblyEntry = "lib/net10.0/SharpProof.SymbolFixture.dll";
    private const string PdbEntry = "lib/net10.0/SharpProof.SymbolFixture.pdb";
    private const string SourceUrl = "https://raw.githubusercontent.com/alexyorke/SharpProof/" + Commit + "/*";

    [TestCase(false)]
    [TestCase(true)]
    public void RealPortableSourceLinkedPackagePairIsAccepted(bool multipleAssemblies)
    {
        var image = EmitImage();
        var directory = Directory.CreateTempSubdirectory("sharpproof-symbol-authority-");
        try
        {
            var package = Path.Combine(directory.FullName, PackageId + "." + PackageVersion + ".nupkg");
            var symbols = Path.Combine(directory.FullName, PackageId + "." + PackageVersion + ".snupkg");
            ImmutableArray<(string Name, ImmutableArray<byte> Bytes)> assemblies = multipleAssemblies
                ? [(AssemblyEntry, image.Pe), ("lib/net10.0/SharpProof.Second.dll", image.Pe)]
                : [(AssemblyEntry, image.Pe)];
            ImmutableArray<(string Name, ImmutableArray<byte> Bytes)> pdbs = multipleAssemblies
                ? [(PdbEntry, image.Pdb), ("lib/net10.0/SharpProof.Second.pdb", image.Pdb)]
                : [(PdbEntry, image.Pdb)];
            WriteArchive(package, assemblies);
            WriteArchive(symbols, pdbs);
            SharpProofSymbolPackageValidator.Validate(
                package, symbols, PackageId, PackageVersion, Commit);
        }
        finally
        { directory.Delete(recursive: true); }
    }

    [TestCase("main-name")]
    [TestCase("symbol-name")]
    [TestCase("missing-nuspec")]
    [TestCase("duplicate-nuspec")]
    [TestCase("wrong-id")]
    [TestCase("wrong-version")]
    [TestCase("wrong-commit")]
    [TestCase("duplicate-entry")]
    [TestCase("duplicate-symbol-entry")]
    [TestCase("main-pdb")]
    [TestCase("missing-symbol-pdb")]
    [TestCase("no-assembly")]
    [TestCase("wrong-pdb-name")]
    [TestCase("malformed-pdb")]
    [TestCase("different-pdb-id")]
    [TestCase("missing-codeview")]
    [TestCase("multiple-codeview")]
    [TestCase("nonportable-codeview")]
    [TestCase("codeview-age")]
    public void CorruptedArchiveAndPeBoundariesAreRejected(string corruption)
    {
        ArgumentNullException.ThrowIfNull(corruption);
        var image = EmitImage();
        var directory = Directory.CreateTempSubdirectory("sharpproof-symbol-corruption-");
        try
        {
            var package = Path.Combine(directory.FullName,
                corruption == "main-name" ? "wrong.nupkg" : PackageId + "." + PackageVersion + ".nupkg");
            var symbols = Path.Combine(directory.FullName,
                corruption == "symbol-name" ? "wrong.snupkg" : PackageId + "." + PackageVersion + ".snupkg");
            var mainBytes = corruption.EndsWith("codeview", StringComparison.Ordinal) || corruption == "codeview-age"
                ? CorruptDebugDirectory(image.Pe, corruption) : image.Pe;
            var pdbBytes = corruption switch
            {
                "malformed-pdb" => ImmutableArray.Create<byte>(1, 2, 3),
                "different-pdb-id" => EmitImage(sourceCode:
                    "public static class SymbolFixture { public static int Value => 19; }").Pdb,
                _ => image.Pdb
            };
            ImmutableArray<(string Name, ImmutableArray<byte> Bytes)> mainEntries = corruption switch
            {
                "main-pdb" => [(AssemblyEntry, mainBytes), (PdbEntry, pdbBytes)],
                "no-assembly" => [("unrelated.txt", ImmutableArray<byte>.Empty)],
                "duplicate-entry" => [(AssemblyEntry, mainBytes), (AssemblyEntry, mainBytes)],
                _ => [(AssemblyEntry, mainBytes)]
            };
            ImmutableArray<(string Name, ImmutableArray<byte> Bytes)> symbolEntries = corruption switch
            {
                "missing-symbol-pdb" => [],
                "wrong-pdb-name" => [("wrong.pdb", pdbBytes)],
                "duplicate-symbol-entry" => [(PdbEntry, pdbBytes), (PdbEntry, pdbBytes)],
                _ => [(PdbEntry, pdbBytes)]
            };
            var identity = corruption switch
            {
                "wrong-id" => "<package>\n<id>wrong</id>\n<version>1.0.0</version>\n<repository commit=\"" + Commit + "\" />\n</package>",
                "wrong-version" => "<package>\n<id>" + PackageId + "</id>\n<version>2.0.0</version>\n<repository commit=\"" + Commit + "\" />\n</package>",
                "wrong-commit" => "<package>\n<id>" + PackageId + "</id>\n<version>1.0.0</version>\n<repository commit=\"wrong\" />\n</package>",
                _ => null
            };
            WriteArchive(package, mainEntries, identity,
                corruption == "missing-nuspec" ? 0 : corruption == "duplicate-nuspec" ? 2 : 1);
            WriteArchive(symbols, symbolEntries);
            Assert.Throws<InvalidDataException>((Action)(() => SharpProofSymbolPackageValidator.Validate(
                package, symbols, PackageId, PackageVersion, Commit)));
        }
        finally
        { directory.Delete(recursive: true); }
    }

    [TestCase("")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [TestCase("gggggggggggggggggggggggggggggggggggggggg")]
    public void InvalidExpectedCommitRejectsBeforeOpeningArchives(string commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        Assert.Throws<InvalidDataException>((Action)(() => SharpProofSymbolPackageValidator.Validate(
            "absent.nupkg", "absent.snupkg", PackageId, PackageVersion, commit)));
    }

    [TestCase(null)]
    [TestCase("{}")]
    [TestCase("[]")]
    [TestCase("{\"documents\":[]}")]
    [TestCase("{\"documents\":{}}")]
    [TestCase("{\"documents\":{\"bad\":\"url\"}}")]
    [TestCase("{\"documents\":{\"/_/*\":17}}")]
    [TestCase("{\"documents\":{\"/_/*\":\"wrong\"}}")]
    public void MissingOrNoncanonicalSourceLinkIsRejected(string? sourceLinkJson)
    {
        var image = EmitImage(sourceLinkJson);
        var directory = Directory.CreateTempSubdirectory("sharpproof-symbol-source-link-");
        try
        {
            var package = Path.Combine(directory.FullName, PackageId + "." + PackageVersion + ".nupkg");
            var symbols = Path.Combine(directory.FullName, PackageId + "." + PackageVersion + ".snupkg");
            WriteArchive(package, [(AssemblyEntry, image.Pe)]);
            WriteArchive(symbols, [(PdbEntry, image.Pdb)]);
            Assert.Throws<InvalidDataException>((Action)(() => SharpProofSymbolPackageValidator.Validate(
                package, symbols, PackageId, PackageVersion, Commit)));
        }
        finally
        { directory.Delete(recursive: true); }
    }

    [Test]
    public void OrdinaryPeMetadataCannotBeUsedAsAPortablePdbHeader()
    {
        var image = EmitImage();
        using var pe = new PEReader(image.Pe);
        var directoryEntry = pe.ReadDebugDirectory().Single(entry => entry.Type == DebugDirectoryEntryType.CodeView);
        var identity = pe.ReadCodeViewDebugDirectoryData(directoryEntry);
        using var stream = new MemoryStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var entry = archive.CreateEntry("entry");
        var validate = typeof(SharpProofSymbolPackageValidator).GetMethod("ValidatePortablePdb",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var failure = Assert.Throws<TargetInvocationException>((Action)(() => validate.Invoke(null,
            [pe.GetMetadataReader(), entry, entry, identity, directoryEntry, SourceUrl])));
        Assert.That(failure!.InnerException, Is.TypeOf<InvalidDataException>());
        Assert.That(failure.InnerException!.Message, Does.Contain("no debug metadata header"));
    }

    private static ImmutableArray<byte> CorruptDebugDirectory(ImmutableArray<byte> pe, string corruption)
    {
        if (pe.IsDefault)
        { throw new ArgumentNullException(nameof(pe)); }
        ArgumentNullException.ThrowIfNull(corruption);
        using var reader = new PEReader(pe);
        var entries = reader.ReadDebugDirectory();
        var codeViewIndex = Enumerable.Range(0, entries.Length)
            .Single(index => entries[index].Type == DebugDirectoryEntryType.CodeView);
        var directoryRva = reader.PEHeaders.PEHeader!.DebugTableDirectory.RelativeVirtualAddress;
        var section = reader.PEHeaders.SectionHeaders.Single(header =>
            directoryRva >= header.VirtualAddress && directoryRva < header.VirtualAddress + header.VirtualSize);
        var directoryOffset = section.PointerToRawData + directoryRva - section.VirtualAddress;
        // The mutable image is the actual corrupted PE subject.
        var mutated = pe.ToArray();
        if (corruption == "missing-codeview")
        { BinaryPrimitives.WriteInt32LittleEndian(mutated.AsSpan(directoryOffset + codeViewIndex * 28 + 12, 4), 0); }
        else if (corruption == "multiple-codeview")
        {
            var extra = Enumerable.Range(0, entries.Length).First(index => index != codeViewIndex);
            BinaryPrimitives.WriteInt32LittleEndian(mutated.AsSpan(directoryOffset + extra * 28 + 12, 4),
                (int)DebugDirectoryEntryType.CodeView);
        }
        else if (corruption == "nonportable-codeview")
        { BinaryPrimitives.WriteUInt16LittleEndian(mutated.AsSpan(directoryOffset + codeViewIndex * 28 + 10, 2), 0); }
        else
        { BinaryPrimitives.WriteInt32LittleEndian(mutated.AsSpan(entries[codeViewIndex].DataPointer + 20, 4), 2); }
        return ImmutableArray.Create<byte>(mutated);
    }

    private static (ImmutableArray<byte> Pe, ImmutableArray<byte> Pdb) EmitImage(
        string? sourceLinkJson = "{\"documents\":{\"/_/*\":\"" + SourceUrl + "\"}}",
        string sourceCode = "public static class SymbolFixture { public static int Value => 17; }")
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        var source = SourceText.From(sourceCode, Encoding.UTF8);
        var tree = CSharpSyntaxTree.ParseText(source, path: "/_/SymbolFixture.cs");
        var platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException("The native compiler fixture requires platform references."))
            .Split(Path.PathSeparator).ToImmutableArray();
        var references = platform.Select(static path =>
        {
            ArgumentNullException.ThrowIfNull(path);
            return MetadataReference.CreateFromFile(path);
        }).ToImmutableArray();
        var compilation = CSharpCompilation.Create(PackageId, [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));
        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        using var sourceLink = sourceLinkJson == null ? null :
            new MemoryStream(Encoding.UTF8.GetBytes(sourceLinkJson), writable: false);
        var emitted = compilation.Emit(pe, pdb,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
            sourceLinkStream: sourceLink);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        return (ImmutableArray.Create<byte>(pe.GetBuffer().AsSpan(0, checked((int)pe.Length))),
            ImmutableArray.Create<byte>(pdb.GetBuffer().AsSpan(0, checked((int)pdb.Length))));
    }

    private static void WriteArchive(string path, ImmutableArray<(string Name, ImmutableArray<byte> Bytes)> entries,
        string? identityXml = null, int /* SPINT001: all values valid - nonpositive counts deliberately omit nuspec entries */ nuspecCount = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (entries.IsDefault)
        { throw new ArgumentNullException(nameof(entries)); }
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        for (var index = 0; index < nuspecCount; index++)
        {
            using var stream = archive.CreateEntry(index == 0 ? PackageId + ".nuspec" : "extra.nuspec").Open();
            var text = identityXml ?? "<package>\n<metadata>\n<id>" + PackageId + "</id>\n<version>" + PackageVersion +
                "</version>\n<repository commit=\"" + Commit + "\" />\n</metadata>\n</package>";
            stream.Write(Encoding.UTF8.GetBytes(text));
        }
        foreach (var entry in entries)
        {
            using var stream = archive.CreateEntry(entry.Name).Open();
            stream.Write(entry.Bytes.AsSpan());
        }
    }
}
