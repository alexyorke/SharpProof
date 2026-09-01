using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
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
        if (!codeViewEntries[0].IsPortableCodeView)
        {
            throw new InvalidDataException(
                $"Assembly '{assemblyEntry.FullName}' must contain a portable CodeView identifier.");
        }
        var codeView = peReader.ReadCodeViewDebugDirectoryData(
            codeViewEntries[0]);
        if (codeView.Age != 1)
        {
            throw new InvalidDataException(
                $"Assembly '{assemblyEntry.FullName}' has an invalid portable CodeView age.");
        }

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
        ValidatePdbChecksum(peReader, assemblyEntry, pdbImage);
    }

    private static void ValidatePdbChecksum(
        PEReader peReader,
        ZipArchiveEntry assemblyEntry,
        MemoryStream pdbImage)
    {
        var checksumEntries = peReader.ReadDebugDirectory()
            .Where(static entry => entry.Type == DebugDirectoryEntryType.PdbChecksum)
            .ToArray();
        if (checksumEntries.Length != 1)
        {
            throw new InvalidDataException(
                $"Assembly '{assemblyEntry.FullName}' must contain exactly one " +
                "PDB checksum debug directory entry.");
        }

        PdbChecksumDebugDirectoryData checksum;
        try
        {
            checksum = peReader.ReadPdbChecksumDebugDirectoryData(checksumEntries[0]);
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException(
                $"Assembly '{assemblyEntry.FullName}' has a malformed PDB checksum entry.",
                exception);
        }
        if (!string.Equals(checksum.AlgorithmName, "SHA256", StringComparison.Ordinal) ||
            checksum.Checksum.Length != SHA256.HashSizeInBytes)
        {
            throw new InvalidDataException(
                $"Assembly '{assemblyEntry.FullName}' must use a 32-byte SHA256 " +
                "PDB checksum.");
        }

        var content = pdbImage.ToArray();
        var idOffset = PortablePdbIdOffset(content);
        Array.Clear(content, idOffset, 20);
        var actual = SHA256.HashData(content);
        if (!checksum.Checksum.AsSpan().SequenceEqual(actual))
        {
            throw new InvalidDataException(
                $"Assembly '{assemblyEntry.FullName}' PDB checksum does not " +
                "match the packaged portable PDB.");
        }
        pdbImage.Position = 0;
    }

    private static int PortablePdbIdOffset(byte[] content)
    {
        const uint metadataSignature = 0x424A5342;
        const string pdbStreamName = "#Pdb";
        try
        {
            var position = 0;
            uint ReadUInt32()
            {
                var value = BinaryPrimitives.ReadUInt32LittleEndian(
                    content.AsSpan(position, sizeof(uint)));
                position += sizeof(uint);
                return value;
            }
            ushort ReadUInt16()
            {
                var value = BinaryPrimitives.ReadUInt16LittleEndian(
                    content.AsSpan(position, sizeof(ushort)));
                position += sizeof(ushort);
                return value;
            }

            if (ReadUInt32() != metadataSignature)
            {
                throw new InvalidDataException("Portable PDB metadata signature is invalid.");
            }
            position += sizeof(ushort) * 2 + sizeof(uint);
            var versionLength = checked((int)ReadUInt32());
            position = checked((position + versionLength + 3) & ~3);
            _ = ReadUInt16();
            var streamCount = ReadUInt16();
            for (var index = 0; index < streamCount; index++)
            {
                var offset = checked((int)ReadUInt32());
                var size = checked((int)ReadUInt32());
                var nameStart = position;
                while (content[position] != 0)
                {
                    position++;
                }
                var name = Encoding.ASCII.GetString(
                    content,
                    nameStart,
                    position - nameStart);
                position = checked((position + 1 + 3) & ~3);
                if (name == pdbStreamName)
                {
                    if (size < 20 || offset < 0 || offset > content.Length - 20)
                    {
                        break;
                    }
                    return offset;
                }
            }
        }
        catch (Exception exception) when (exception is
            ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("Portable PDB metadata is malformed.", exception);
        }
        throw new InvalidDataException("Portable PDB metadata has no valid #Pdb stream.");
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
