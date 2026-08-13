using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

internal static class SharpProofSymbolPackageValidator
{
    private static readonly Guid SourceLinkKind = new(
        "CC110556-A091-4D38-9FEC-25AB9A351A6A");

    public static void Validate(
        string packagePath,
        string symbolPackagePath,
        string packageId,
        string repositoryCommit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolPackagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (repositoryCommit.Length != 40 ||
            repositoryCommit.Any(static value => !Uri.IsHexDigit(value)))
        {
            throw new InvalidDataException(
                "The expected repository commit is not a 40-character hash.");
        }

        using var package = ZipFile.OpenRead(packagePath);
        using var symbols = ZipFile.OpenRead(symbolPackagePath);
        RejectDuplicateEntries(package, packagePath);
        RejectDuplicateEntries(symbols, symbolPackagePath);

        var assemblies = package.Entries
            .Where(static entry =>
                entry.FullName.EndsWith(".dll", StringComparison.Ordinal) &&
                Path.GetFileName(entry.FullName).StartsWith(
                    "SharpProof.",
                    StringComparison.Ordinal))
            .OrderBy(static entry => entry.FullName, StringComparer.Ordinal)
            .ToArray();
        if (assemblies.Length == 0)
        {
            throw new InvalidDataException(
                $"Main package '{packageId}' contains no SharpProof assemblies.");
        }

        var expectedPdbNames = assemblies
            .Select(static entry => entry.FullName[..^4] + ".pdb")
            .ToArray();
        var actualPdbNames = symbols.Entries
            .Where(static entry => entry.FullName.EndsWith(
                ".pdb",
                StringComparison.OrdinalIgnoreCase))
            .Select(static entry => entry.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!actualPdbNames.SequenceEqual(expectedPdbNames, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Symbol package '{packageId}' does not contain the exact " +
                "PDB entry set for its main package. Expected: " +
                $"'{string.Join(", ", expectedPdbNames)}'; found: " +
                $"'{string.Join(", ", actualPdbNames)}'.");
        }

        var expectedSourceUrl =
            "https://raw.githubusercontent.com/alexyorke/SharpProof/" +
            repositoryCommit.ToLowerInvariant() + "/*";
        for (var index = 0; index < assemblies.Length; index++)
        {
            var pdb = symbols.GetEntry(expectedPdbNames[index]) ??
                throw new InvalidDataException(
                    "Symbol package entry disappeared during validation: " +
                    expectedPdbNames[index]);
            ValidatePair(assemblies[index], pdb, expectedSourceUrl);
        }
    }

    private static void RejectDuplicateEntries(ZipArchive archive, string path)
    {
        var duplicate = archive.Entries
            .GroupBy(static entry => entry.FullName, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() != 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Package '{path}' contains duplicate entry " +
                $"'{duplicate.Key}'.");
        }
    }

    private static void ValidatePair(
        ZipArchiveEntry assemblyEntry,
        ZipArchiveEntry pdbEntry,
        string expectedSourceUrl)
    {
        using var assemblyImage = CopyToMemory(assemblyEntry);
        using var peReader = new PEReader(assemblyImage);
        var codeViewEntries = peReader.ReadDebugDirectory()
            .Where(static entry => entry.Type == DebugDirectoryEntryType.CodeView)
            .ToArray();
        if (codeViewEntries.Length != 1)
        {
            throw new InvalidDataException(
                $"Assembly '{assemblyEntry.FullName}' must contain exactly " +
                "one CodeView debug identifier.");
        }
        var codeView = peReader.ReadCodeViewDebugDirectoryData(
            codeViewEntries[0]);

        using var pdbImage = CopyToMemory(pdbEntry);
        MetadataReaderProvider provider;
        MetadataReader reader;
        try
        {
            provider = MetadataReaderProvider.FromPortablePdbStream(
                pdbImage,
                MetadataStreamOptions.LeaveOpen);
            reader = provider.GetMetadataReader();
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException(
                $"Portable PDB '{pdbEntry.FullName}' is malformed.",
                exception);
        }
        using (provider)
        {
            ValidatePortablePdb(
                reader,
                pdbEntry,
                assemblyEntry,
                codeView,
                codeViewEntries[0],
                expectedSourceUrl);
        }
    }

    private static void ValidatePortablePdb(
        MetadataReader reader,
        ZipArchiveEntry pdbEntry,
        ZipArchiveEntry assemblyEntry,
        CodeViewDebugDirectoryData codeView,
        DebugDirectoryEntry codeViewEntry,
        string expectedSourceUrl)
    {
        var pdbId = reader.DebugMetadataHeader.Id;
        var expectedId = new byte[20];
        codeView.Guid.ToByteArray().CopyTo(expectedId, 0);
        BitConverter.GetBytes(codeViewEntry.Stamp).CopyTo(expectedId, 16);
        if (pdbId.Length != expectedId.Length ||
            !pdbId.SequenceEqual(expectedId))
        {
            throw new InvalidDataException(
                $"Portable PDB '{pdbEntry.FullName}' debug identifier does " +
                $"not match assembly '{assemblyEntry.FullName}'.");
        }

        var sourceLinks = reader.CustomDebugInformation
            .Select(reader.GetCustomDebugInformation)
            .Where(information => reader.GetGuid(information.Kind) == SourceLinkKind)
            .ToArray();
        if (sourceLinks.Length != 1)
        {
            throw new InvalidDataException(
                $"Portable PDB '{pdbEntry.FullName}' must contain exactly " +
                "one Source Link record.");
        }

        var json = Encoding.UTF8.GetString(
            reader.GetBlobBytes(sourceLinks[0].Value));
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("documents", out var documents) ||
            documents.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Portable PDB '{pdbEntry.FullName}' has malformed Source Link.");
        }
        var mappings = documents.EnumerateObject().ToArray();
        if (mappings.Length == 0 || mappings.Any(mapping =>
                !mapping.Name.Replace('\\', '/').EndsWith(
                    "/*",
                    StringComparison.Ordinal) ||
                mapping.Value.ValueKind != JsonValueKind.String ||
                !string.Equals(
                    mapping.Value.GetString(),
                    expectedSourceUrl,
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"Portable PDB '{pdbEntry.FullName}' Source Link does not " +
                "name the canonical repository commit.");
        }
    }

    private static MemoryStream CopyToMemory(ZipArchiveEntry entry)
    {
        var image = new MemoryStream();
        using var stream = entry.Open();
        stream.CopyTo(image);
        image.Position = 0;
        return image;
    }
}
