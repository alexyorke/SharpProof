using System;
using System.Collections.Generic;
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
        string packageVersion,
        string repositoryCommit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolPackagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        if (repositoryCommit.Length != 40 ||
            repositoryCommit.Any(static value =>
                !Uri.IsHexDigit(value) || value is >= 'A' and <= 'F'))
        {
            throw new InvalidDataException(
                "The expected repository commit is not a 40-character hash.");
        }

        using var package = ZipFile.OpenRead(packagePath);
        using var symbols = ZipFile.OpenRead(symbolPackagePath);
        RejectDuplicateEntries(package, packagePath);
        RejectDuplicateEntries(symbols, symbolPackagePath);
        ValidateArchiveIdentity(
            package,
            packagePath,
            packageId,
            packageVersion,
            repositoryCommit,
            isSymbols: false);
        ValidateArchiveIdentity(
            symbols,
            symbolPackagePath,
            packageId,
            packageVersion,
            repositoryCommit,
            isSymbols: true);

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
            repositoryCommit + "/*";
        for (var index = 0; index < assemblies.Length; index++)
        {
            var pdb = symbols.GetEntry(expectedPdbNames[index]) ??
                throw new InvalidDataException(
                    "Symbol package entry disappeared during validation: " +
                    expectedPdbNames[index]);
            ValidatePair(assemblies[index], pdb, expectedSourceUrl);
        }
    }

    private static void ValidateArchiveIdentity(
        ZipArchive archive,
        string path,
        string packageId,
        string packageVersion,
        string repositoryCommit,
        bool isSymbols)
    {
        var expectedName = packageId + "." + packageVersion +
            (isSymbols ? ".snupkg" : ".nupkg");
        if (!string.Equals(
                Path.GetFileName(path),
                expectedName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The {(isSymbols ? "symbol" : "main")} package for " +
                $"'{packageId}' must be named exactly '{expectedName}'.");
        }

        var nuspecEntries = archive.Entries
            .Where(static entry => entry.FullName.EndsWith(
                ".nuspec",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nuspecEntries.Length != 1)
        {
            throw new InvalidDataException(
                $"Package '{path}' must contain exactly one nuspec.");
        }

        using var stream = nuspecEntries[0].Open();
        using var document = System.Xml.XmlReader.Create(
            stream,
            new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit });
        var id = default(string);
        var version = default(string);
        var commit = default(string);
        while (document.Read())
        {
            if (document.NodeType != System.Xml.XmlNodeType.Element)
            {
                continue;
            }
            if (document.LocalName == "id")
            {
                id = document.ReadElementContentAsString();
            }
            else if (document.LocalName == "version")
            {
                version = document.ReadElementContentAsString();
            }
            else if (document.LocalName == "repository")
            {
                commit = document.GetAttribute("commit");
            }
        }
        if (!string.Equals(id, packageId, StringComparison.Ordinal) ||
            !string.Equals(version, packageVersion, StringComparison.Ordinal) ||
            !string.Equals(commit, repositoryCommit, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Package '{path}' nuspec identity or repository commit " +
                "does not match its authenticated release role.");
        }

        var pdbCount = archive.Entries.Count(static entry =>
            entry.FullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
        if ((!isSymbols && pdbCount != 0) || (isSymbols && pdbCount == 0))
        {
            throw new InvalidDataException(
                $"Package '{path}' has an invalid " +
                $"{(isSymbols ? "symbol" : "main")} package layout.");
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
        var debugMetadataHeader = reader.DebugMetadataHeader ??
            throw new InvalidDataException(
                $"Portable PDB '{pdbEntry.FullName}' has no debug metadata header.");
        var pdbId = debugMetadataHeader.Id;
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

        var sourceLinkMappings = mappings
            .Select(mapping => (Pattern: mapping.Name, Url: mapping.Value.GetString()!))
            .ToArray();
        var documentNames = reader.Documents
            .Select(handle => reader.GetString(reader.GetDocument(handle).Name))
            .ToArray();
        ValidateSourceLinkCoverage(
            pdbEntry.FullName,
            documentNames,
            sourceLinkMappings,
            expectedSourceUrl);
    }

    internal static void ValidateSourceLinkCoverage(
        string pdbName,
        IReadOnlyList<string> documentNames,
        IReadOnlyList<(string Pattern, string Url)> mappings,
        string expectedSourceUrl)
    {
        var normalizedDocuments = documentNames
            .Select(static name => name.Replace('\\', '/'))
            .ToArray();
        var used = new bool[mappings.Count];
        var normalizedPatterns = new string[mappings.Count];
        var seenPatterns = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < mappings.Count; index++)
        {
            var mapping = mappings[index];
            var pattern = mapping.Pattern.Replace('\\', '/');
            if (!pattern.EndsWith("/*", StringComparison.Ordinal) ||
                pattern.Length < 2 ||
                !seenPatterns.Add(pattern) ||
                !string.Equals(mapping.Url, expectedSourceUrl, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Portable PDB '{pdbName}' Source Link mapping is invalid.");
            }

            normalizedPatterns[index] = pattern[..^1];
        }

        foreach (var document in normalizedDocuments)
        {
            var matched = false;
            for (var index = 0; index < normalizedPatterns.Length; index++)
            {
                if (!document.StartsWith(
                        normalizedPatterns[index],
                        StringComparison.Ordinal))
                {
                    continue;
                }

                used[index] = true;
                matched = true;
            }

            if (!matched)
            {
                throw new InvalidDataException(
                    $"Portable PDB '{pdbName}' Source Link does not cover " +
                    $"document '{document}'.");
            }
        }

        if (used.Any(static value => !value))
        {
            throw new InvalidDataException(
                $"Portable PDB '{pdbName}' Source Link contains an unused mapping.");
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
