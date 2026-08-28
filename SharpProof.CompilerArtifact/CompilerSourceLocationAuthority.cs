using System.Runtime.CompilerServices;

namespace SharpProof.CompilerArtifact;

// Source locations are compiler evidence, not display-only hints.  Keep the
// physical-tree binding and mapped geometry predicate in the shared artifact
// assembly so the collector and worker cannot gradually diverge.
internal static class CompilerSourceLocationAuthority
{
    // A producer validates many locations against the same immutable
    // compilation snapshot.  Keep the cache scoped to that validation
    // operation so mutable/deserialized snapshots are never trusted across
    // calls or consumers.
    internal sealed class ValidationContext
    {
        private readonly Dictionary<CompilerSyntaxTreeSnapshot, bool> _lineMaps =
            new(ReferenceComparer.Instance);
        private readonly Dictionary<CompilerSyntaxTreeSnapshot, string>
            _snapshotHashes = new(ReferenceComparer.Instance);

        internal int LineMapValidationCount { get; private set; }
        internal int SnapshotHashComputeCount { get; private set; }

        internal bool HasValidLineMap(CompilerSyntaxTreeSnapshot tree)
        {
            if (_lineMaps.TryGetValue(tree, out var valid))
            {
                return valid;
            }

            LineMapValidationCount++;
            valid = HasValidLineMapCore(tree);
            _lineMaps.Add(tree, valid);
            return valid;
        }

        internal string GetSyntaxTreeSnapshotSha256(
            CompilerSyntaxTreeSnapshot tree)
        {
            if (_snapshotHashes.TryGetValue(tree, out var hash))
            {
                return hash;
            }

            SnapshotHashComputeCount++;
            hash = CompilationFingerprint.ComputeSyntaxTreeSnapshotSha256(tree);
            _snapshotHashes.Add(tree, hash);
            return hash;
        }
    }

    private sealed class ReferenceComparer :
        IEqualityComparer<CompilerSyntaxTreeSnapshot>
    {
        internal static readonly ReferenceComparer Instance = new();

        public bool Equals(
            CompilerSyntaxTreeSnapshot? left,
            CompilerSyntaxTreeSnapshot? right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(CompilerSyntaxTreeSnapshot value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }

    internal static bool IsNone(WorkerSourceLocation? value)
    {
        return value is
        {
            Path.Length: 0,
            Start: 0,
            Length: 0,
            Line: 0,
            Column: 0
        };
    }

    internal static bool HasValidLineMap(
        CompilerSyntaxTreeSnapshot? tree)
    {
        return tree != null && HasValidLineMapCore(tree);
    }

    internal static bool HasValidLineMap(
        CompilerSyntaxTreeSnapshot? tree,
        ValidationContext? context)
    {
        return tree != null &&
            (context?.HasValidLineMap(tree) ?? HasValidLineMapCore(tree));
    }

    private static bool HasValidLineMapCore(
        CompilerSyntaxTreeSnapshot tree)
    {
        if (tree == null ||
            !WorkerProtocolJson.IsSha256(tree.LineMapSha256) ||
            tree.LineMap is not { Length: > 0 } entries ||
            tree.LineMapSha256 != CompilationFingerprint.ComputeLineMapSha256(entries))
        {
            return false;
        }

        var previousStart = -1;
        foreach (var entry in entries)
        {
            if (entry == null ||
                entry.SourceStart < 0 ||
                entry.SourceLength < 0 ||
                entry.SourceStart <= previousStart ||
                entry.SourceStart > tree.TextLength ||
                entry.SourceLength > tree.TextLength - entry.SourceStart ||
                entry.MappedLine < 0 ||
                entry.MappedColumn < 0 ||
                string.IsNullOrWhiteSpace(entry.MappedPath))
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
        ValidationContext? context = null)
    {
        if (location == null || tree == null ||
            !WorkerProtocolJson.HasValidLocation(location) ||
            !HasValidLineMap(tree, context) ||
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
        ValidationContext? context = null)
    {
        if (location == null || compilation is not { SyntaxTrees: not null })
        {
            return -1;
        }

        var ordinal = -1;
        for (var index = 0; index < compilation.SyntaxTrees.Length; index++)
        {
            var tree = compilation.SyntaxTrees[index];
            if (tree == null ||
                !HasValidLocationGeometry(location, tree, context))
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
        ValidationContext? context = null)
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
            HasValidLocationGeometry(location, tree, context);
    }

    internal static CompilerLocationAuthorityArtifact CreateAuthority(
        CompilerSourceLocationOwnerKind ownerKind,
        string ownerId,
        WorkerSourceLocation location,
        CompilerCompilationSnapshot compilation,
        ValidationContext? context = null)
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

        var ordinal = FindUniqueTree(location, compilation, context);
        if (ordinal < 0)
        {
            throw new InvalidDataException(
                "A compiler source location is not bound to one physical tree.");
        }

        var tree = compilation.SyntaxTrees[ordinal]!;
        return new CompilerLocationAuthorityArtifact
        {
            OwnerKind = ownerKind,
            OwnerId = ownerId,
            Location = CopyLocation(location),
            SourceTreeOrdinal = ordinal,
            SourceTreePath = tree.Path,
            SourceTreeSha256 = tree.Sha256,
            SourceLineMapSha256 = tree.LineMapSha256
        };
    }

    internal static void Bind(
        WorkerSourceLocation location,
        CompilerCompilationSnapshot compilation,
        out int sourceTreeOrdinal,
        out string sourceTreePath,
        out string sourceTreeSha256,
        out string sourceLineMapSha256,
        ValidationContext? context = null)
    {
        var ordinal = FindUniqueTree(location, compilation, context);
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
        var line = (long)selected.MappedLine;
        var column = (long)selected.MappedColumn + delta;
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
