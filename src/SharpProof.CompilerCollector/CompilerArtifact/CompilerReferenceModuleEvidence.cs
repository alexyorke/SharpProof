using System.Reflection.Metadata;

namespace SharpProof.CompilerCollector;

// The metadata and identity facts supplied by a loaded module or its backing image.
internal sealed record CompilerReferenceModuleEvidence(MetadataReader Reader, string Name, GuidHandle /* SPINT001: all values valid - nil is retained as raw evidence and rejected by ReadMvid */ Mvid)
{
    public MetadataReader Reader { get; init; } = Reader ?? throw new System.ArgumentNullException(nameof(Reader));
    public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));

    internal static CompilerReferenceModuleEvidence Capture(MetadataReader reader)
    {
        reader = RepositoryParameterGuard.NotNull(reader, nameof(reader));
        var name = reader.GetString(reader.GetModuleDefinition().Name);
        return new(reader, name, reader.GetModuleDefinition().Mvid);
    }

    internal static CompilerReferenceModuleIdentity Match(
        CompilerReferenceModuleEvidence backing, CompilerReferenceModuleEvidence file)
    {
        var fileMvid = file.ReadMvid();
        if (!string.Equals(backing.Name, file.Name, StringComparison.Ordinal) ||
            backing.ReadMvid() != fileMvid ||
            !CompilerCompilationCapture.MetadataEquals(backing.Reader, file.Reader))
        {
            throw new InvalidDataException(
                "A compiler reference path does not match its loaded metadata.");
        }
        return new(file.Name, fileMvid);
    }

    private Guid ReadMvid()
    {
        if (Mvid.IsNil)
        {
            throw new InvalidDataException("A compiler reference module has no MVID.");
        }
        return Reader.GetGuid(Mvid);
    }
}

internal sealed record CompilerReferenceModuleIdentity(string Name, Guid Mvid)
{
    public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));
}
