using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

// Source locations are compiler evidence, not display-only hints.  Keep the
// physical-tree binding and mapped geometry predicate in the shared artifact
// assembly so the collector and worker cannot gradually diverge.
internal static class CompilerSourceLocationAuthority
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorkerSourceLocation, TreeBinding> TreeBindings = new();
    private sealed class TreeBinding(int ordinal) { internal int Ordinal { get; } = ordinal; }

    internal static void RememberTree(WorkerSourceLocation location, int ordinal)
    {
        TreeBindings.Remove(location);
        TreeBindings.Add(location, new TreeBinding(ordinal));
    }

    private static int RememberedTree(WorkerSourceLocation location)
    {
        return TreeBindings.TryGetValue(location, out var binding) ? binding.Ordinal : -1;
    }
    internal static bool IsNone(WorkerSourceLocation? value)
    {
        return WorkerProtocolJson.IsNoneLocation(value);
    }

    internal static bool HasValidLineMap(
        CompilerSyntaxTreeSnapshot? tree,
        CancellationToken cancellationToken = default)
    {
        if (tree == null ||
            !WorkerProtocolJson.IsSha256(tree.LineMapSha256) ||
            tree.LineMap is not { Length: > 0 } entries ||
            tree.LineMapSha256 != CompilationFingerprint.ComputeLineMapSha256(entries))
        {
            return false;
        }

        var previousStart = -1;
        for (var entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[entryIndex];
            if (entry == null ||
                entry.SourceStart < 0 ||
                entry.SourceLength < 0 ||
                entry.SourceStart <= previousStart ||
                entry.SourceStart > tree.TextLength ||
                entry.SourceLength > tree.TextLength - entry.SourceStart ||
                entry.MappedLine < 0 ||
                entry.MappedColumn < 0 ||
                entry.CharacterOffset < 0 ||
                entry.CharacterOffset > entry.SourceLength ||
                string.IsNullOrWhiteSpace(entry.MappedPath))
            {
                return false;
            }

            var nextStart = entryIndex + 1 < entries.Length
                ? entries[entryIndex + 1]?.SourceStart ?? -1
                : tree.TextLength;
            var gap = nextStart - entry.SourceStart;
            var terminatorLength = entryIndex + 1 < entries.Length
                ? gap - entry.SourceLength
                : 0;
            if (gap < 0 ||
                (entryIndex + 1 < entries.Length
                    ? terminatorLength is < 1 or > 2
                    : entry.SourceLength != gap))
            {
                return false;
            }

            previousStart = entry.SourceStart;
        }

        return entries[0].SourceStart == 0;
    }

    internal static bool HasValidLocationGeometry(
        WorkerSourceLocation? location,
        CompilerSyntaxTreeSnapshot? tree,
        bool locationAlreadyValidated = false,
        CancellationToken cancellationToken = default)
    {
        if (location == null || tree == null ||
            !locationAlreadyValidated && !WorkerProtocolJson.HasValidLocation(location) ||
            !HasValidLineMap(tree, cancellationToken) ||
            location.Start < 0 ||
            location.Length < 0 ||
            location.Start > tree.TextLength ||
            location.Length > tree.TextLength - location.Start ||
            !TryMap(
                tree.LineMap,
                location.Start,
                out var mappedPath,
                out var mappedLine,
                out var mappedColumn))
        {
            return false;
        }

        return string.Equals(location.Path, mappedPath, StringComparison.Ordinal) &&
            location.Line == mappedLine + 1L &&
            location.Column == mappedColumn + 1L;
    }

    // A mapped location can be produced by more than one physical tree (for
    // example two trees using the same #line path).  Never guess in that case.
    internal static int FindUniqueTree(
        WorkerSourceLocation? location,
        CompilerCompilationSnapshot? compilation,
        CancellationToken cancellationToken = default)
    {
        return FindUniqueTree(
            location,
            compilation?.SyntaxTrees,
            cancellationToken);
    }

    internal static int FindUniqueTree(
        WorkerSourceLocation? location,
        CompilerSyntaxTreeSnapshot[]? syntaxTrees,
        CancellationToken cancellationToken = default)
    {
        if (location == null || syntaxTrees == null)
        {
            return -1;
        }

        var ordinal = -1;
        var remembered = RememberedTree(location);
        if (remembered >= 0 && remembered < syntaxTrees.Length &&
            HasValidLocationGeometry(
                location,
                syntaxTrees[remembered],
                cancellationToken: cancellationToken))
        {
            return remembered;
        }
        for (var index = 0; index < syntaxTrees.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tree = syntaxTrees[index];
            if (tree == null || !HasValidLocationGeometry(
                    location,
                    tree,
                    cancellationToken: cancellationToken))
            {
                continue;
            }

            if (ordinal >= 0)
            {
                return -1;
            }

            ordinal = index;
        }

        return ordinal;
    }

    internal static bool IsBound(
        WorkerSourceLocation? location,
        int sourceTreeOrdinal,
        string? sourceTreePath,
        string? sourceTreeSha256,
        string? sourceLineMapSha256,
        CompilerCompilationSnapshot? compilation,
        bool allowNone = false,
        CancellationToken cancellationToken = default)
    {
        if (location == null || compilation is not { SyntaxTrees: not null } ||
            sourceTreePath == null || sourceTreeSha256 == null ||
            sourceLineMapSha256 == null)
        {
            return false;
        }

        if (allowNone && IsNone(location))
        {
            return sourceTreeOrdinal == -1 &&
                sourceTreePath.Length == 0 &&
                sourceTreeSha256.Length == 0 &&
                sourceLineMapSha256.Length == 0;
        }

        if (!WorkerProtocolJson.HasValidLocation(location) ||
            sourceTreeOrdinal < 0 ||
            sourceTreeOrdinal >= compilation.SyntaxTrees.Length ||
            !WorkerProtocolJson.IsSha256(sourceTreeSha256) ||
            !WorkerProtocolJson.IsSha256(sourceLineMapSha256))
        {
            return false;
        }

        var tree = compilation.SyntaxTrees[sourceTreeOrdinal];
        return tree != null &&
            string.Equals(tree.Path, sourceTreePath, StringComparison.Ordinal) &&
            string.Equals(tree.Sha256, sourceTreeSha256, StringComparison.Ordinal) &&
            string.Equals(tree.LineMapSha256, sourceLineMapSha256, StringComparison.Ordinal) &&
            HasValidLocationGeometry(
                location,
                tree,
                locationAlreadyValidated: true,
                cancellationToken: cancellationToken);
    }

    internal static CompilerLocationAuthorityArtifact CreateAuthority(
        CompilerSourceLocationOwnerKind ownerKind,
        string ownerId,
        WorkerSourceLocation location,
        CompilerCompilationSnapshot compilation)
    {
        if (!Enum.IsDefined(typeof(CompilerSourceLocationOwnerKind), ownerKind) ||
            string.IsNullOrWhiteSpace(ownerId) ||
            location == null ||
            compilation == null)
        {
            throw new InvalidDataException(
                "A compiler source-location authority is incomplete.");
        }

        if (IsNone(location))
        {
            return new CompilerLocationAuthorityArtifact
            {
                OwnerKind = ownerKind,
                OwnerId = ownerId,
                Location = CopyLocation(location),
                SourceTreeOrdinal = -1,
                SourceTreePath = string.Empty,
                SourceTreeSha256 = string.Empty,
                SourceLineMapSha256 = string.Empty
            };
        }

        Bind(
            location,
            compilation,
            out var sourceTreeOrdinal,
            out var sourceTreePath,
            out var sourceTreeSha256,
            out var sourceLineMapSha256);
        return new CompilerLocationAuthorityArtifact
        {
            OwnerKind = ownerKind,
            OwnerId = ownerId,
            Location = CopyLocation(location),
            SourceTreeOrdinal = sourceTreeOrdinal,
            SourceTreePath = sourceTreePath,
            SourceTreeSha256 = sourceTreeSha256,
            SourceLineMapSha256 = sourceLineMapSha256
        };
    }

    internal static void Bind(
        WorkerSourceLocation location,
        CompilerCompilationSnapshot compilation,
        out int sourceTreeOrdinal,
        out string sourceTreePath,
        out string sourceTreeSha256,
        out string sourceLineMapSha256)
    {
        var ordinal = FindUniqueTree(location, compilation);
        if (ordinal < 0)
        {
            throw new InvalidDataException(
                "A compiler source location is not bound to one physical tree.");
        }

        var tree = compilation.SyntaxTrees[ordinal]!;
        sourceTreeOrdinal = ordinal;
        sourceTreePath = tree.Path;
        sourceTreeSha256 = tree.Sha256;
        sourceLineMapSha256 = tree.LineMapSha256;
    }

    internal static WorkerSourceLocation CopyLocation(
        WorkerSourceLocation? value)
    {
        return new WorkerSourceLocation
        {
            Path = value?.Path ?? string.Empty,
            Start = value?.Start ?? 0,
            Length = value?.Length ?? 0,
            Line = value?.Line ?? 0,
            Column = value?.Column ?? 0
        };
    }

    internal static bool LocationsEqual(
        WorkerSourceLocation? left,
        WorkerSourceLocation? right)
    {
        return left != null && right != null &&
            left.Path == right.Path &&
            left.Start == right.Start &&
            left.Length == right.Length &&
            left.Line == right.Line &&
            left.Column == right.Column;
    }

    internal static bool TryMap(
        CompilerSourceLineMapEntry[]? entries,
        int sourceStart,
        out string mappedPath,
        out int mappedLine,
        out int mappedColumn)
    {
        mappedPath = string.Empty;
        mappedLine = 0;
        mappedColumn = 0;
        if (entries is not { Length: > 0 } || sourceStart < 0)
        {
            return false;
        }

        CompilerSourceLineMapEntry? selected = null;
        foreach (var entry in entries)
        {
            if (entry == null || entry.SourceStart > sourceStart)
            {
                break;
            }

            selected = entry;
        }

        if (selected == null)
        {
            return false;
        }

        var delta = (long)sourceStart - selected.SourceStart;
        var mappedDelta = Math.Max(delta - selected.CharacterOffset, 0);
        var line = (long)selected.MappedLine;
        var column = (long)selected.MappedColumn + mappedDelta;
        if (delta < 0 || line < 0 || column < 0 ||
            line >= int.MaxValue || column >= int.MaxValue)
        {
            return false;
        }

        mappedPath = selected.MappedPath;
        mappedLine = (int)line;
        mappedColumn = (int)column;
        return true;
    }
}
