namespace SharpProof.CompilerArtifact;

internal static class CompilerEffectAuthority
{
    internal static CompilerEffectAuthorityArtifact Create(
        WorkerClaimManifestEntry entry,
        CompilerEffectClaimArtifact evidence,
        string? sourceTreePath)
    {
        entry = ArgumentNullGuard.NotNull(entry, nameof(entry));
        evidence = ArgumentNullGuard.NotNull(evidence, nameof(evidence));

        return new CompilerEffectAuthorityArtifact
        {
            ClaimId = entry.ClaimId,
            ContractKind = evidence.ContractKind,
            Outcome = evidence.Outcome,
            Reason = evidence.Reason,
            Certainty = evidence.Certainty,
            Constraint = CopyConstraint(evidence.Constraint),
            Witness = CopyWitness(evidence.Witness),
            Replay = CopyReplay(evidence.Replay),
            Evidence = evidence.Evidence,
            Source = CopyLocation(entry.Location),
            SourceTreePath = sourceTreePath ?? string.Empty
        };
    }

    internal static void BindSourceTree(
        CompilerEffectAuthorityArtifact authority,
        CompilerCompilationSnapshot compilation)
    {
        authority = ArgumentNullGuard.NotNull(authority, nameof(authority));
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));

        if (CompilerSourceLocationAuthority.IsNone(authority.Source))
        {
            authority.SourceTreeOrdinal = -1;
            authority.SourceTreePath = string.Empty;
            authority.SourceTreeSha256 = string.Empty;
            authority.SourceLineMapSha256 = string.Empty;
            return;
        }

        var treePath = string.IsNullOrWhiteSpace(authority.SourceTreePath)
            ? authority.Source.Path
            : authority.SourceTreePath;
        var ordinal = Array.FindIndex(
            compilation.SyntaxTrees,
            tree => tree != null &&
                string.Equals(tree.Path, treePath, StringComparison.Ordinal));
        if (ordinal < 0)
        {
            throw new InvalidDataException(
                "A compiler effect authority does not name its source tree.");
        }

        authority.SourceTreeOrdinal = ordinal;
        authority.SourceTreePath = compilation.SyntaxTrees[ordinal].Path;
        authority.SourceTreeSha256 = compilation.SyntaxTrees[ordinal].Sha256;
        authority.SourceLineMapSha256 = compilation.SyntaxTrees[ordinal].LineMapSha256;
    }

    internal static bool Matches(
        CompilerEffectClaimArtifact evidence,
        CompilerEffectAuthorityArtifact authority,
        WorkerClaimManifestEntry expected,
        CompilerCompilationSnapshot compilation)
    {
        try
        {
            if (evidence == null || authority == null || expected == null ||
                compilation is not { SyntaxTrees: not null })
            {
                return false;
            }

            if (!HasValidAuthorityPayload(authority, compilation) ||
                authority.Source == null ||
                authority.Constraint == null ||
                authority.SourceTreePath == null ||
                authority.SourceTreeSha256 == null ||
                authority.SourceLineMapSha256 == null ||
                authority.ClaimId != expected.ClaimId ||
                authority.ClaimId != evidence.ClaimId ||
                authority.ContractKind != expected.EffectContractKind ||
                authority.ContractKind != evidence.ContractKind ||
                authority.Outcome != evidence.Outcome ||
                authority.Reason != evidence.Reason ||
                authority.Certainty != evidence.Certainty ||
                authority.Evidence != evidence.Evidence ||
                !LocationsEqual(authority.Source, expected.Location) ||
                !ConstraintsEqual(authority.Constraint, evidence.Constraint) ||
                !WitnessesEqual(authority.Witness, evidence.Witness) ||
                !ReplaysEqual(authority.Replay, evidence.Replay))
            {
                return false;
            }

            if (authority.SourceTreeOrdinal < 0 ||
                authority.SourceTreeOrdinal >= compilation.SyntaxTrees.Length)
            {
                return CompilerSourceLocationAuthority.IsNone(authority.Source) &&
                    authority.SourceTreeOrdinal == -1 &&
                    authority.SourceTreePath.Length == 0 &&
                    authority.SourceTreeSha256.Length == 0 &&
                    authority.SourceLineMapSha256.Length == 0;
            }

            var tree = compilation.SyntaxTrees[authority.SourceTreeOrdinal];
            return tree != null &&
                authority.SourceTreePath == tree.Path &&
                authority.SourceTreeSha256 == tree.Sha256 &&
                authority.SourceLineMapSha256 == tree.LineMapSha256;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or
            InvalidOperationException or NullReferenceException)
        {
            return false;
        }
    }

    private static bool HasValidAuthorityPayload(
        CompilerEffectAuthorityArtifact authority,
        CompilerCompilationSnapshot compilation)
    {
        try
        {
            var evidence = ToEvidence(authority);
            CompilerEffectClaimArtifactCodec.Seal(evidence);
            CompilerEffectClaimArtifactCodec.Validate(evidence, compilation);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or
            InvalidOperationException)
        {
            return false;
        }
    }

    private static CompilerEffectClaimArtifact ToEvidence(
        CompilerEffectAuthorityArtifact authority)
    {
        return new CompilerEffectClaimArtifact
        {
            ClaimId = authority.ClaimId,
            ContractKind = authority.ContractKind,
            Outcome = authority.Outcome,
            Reason = authority.Reason,
            Certainty = authority.Certainty,
            Constraint = CopyConstraint(authority.Constraint),
            Witness = CopyWitness(authority.Witness),
            Replay = CopyReplay(authority.Replay),
            Evidence = authority.Evidence
        };
    }

    private static bool ConstraintsEqual(
        CompilerEffectConstraintArtifact left,
        CompilerEffectConstraintArtifact right)
    {
        return left != null && right != null &&
            left.AllowedEffects == right.AllowedEffects &&
            left.AllowedCapabilities == right.AllowedCapabilities &&
            left.AllowedExceptionTypes.SequenceEqual(
                right.AllowedExceptionTypes,
                StringComparer.Ordinal);
    }

    private static bool WitnessesEqual(
        WorkerEffectViolationWitness? left,
        WorkerEffectViolationWitness? right)
    {
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        return left.Kind == right.Kind &&
            left.Detail == right.Detail &&
            left.Effects == right.Effects &&
            left.Capabilities == right.Capabilities &&
            left.ExactExceptionTypeHierarchy.SequenceEqual(
                right.ExactExceptionTypeHierarchy,
                StringComparer.Ordinal) &&
            LocationsEqual(left.Location, right.Location);
    }

    private static bool ReplaysEqual(
        CompilerEffectReplayArtifact? left,
        CompilerEffectReplayArtifact? right)
    {
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        return left.PathKind == right.PathKind &&
            left.ConstraintSha256 == right.ConstraintSha256 &&
            left.Events.SequenceEqual(right.Events, ReplayEventComparer.Instance);
    }

    private static bool LocationsEqual(
        WorkerSourceLocation left,
        WorkerSourceLocation right)
    {
        return left != null && right != null &&
            (left.Path, left.Start, left.Length, left.Line, left.Column) ==
            (right.Path, right.Start, right.Length, right.Line, right.Column);
    }

    private static CompilerEffectConstraintArtifact CopyConstraint(
        CompilerEffectConstraintArtifact value)
    {
        return new CompilerEffectConstraintArtifact
        {
            AllowedEffects = value?.AllowedEffects ?? WorkerEffectSet.None,
            AllowedCapabilities = value?.AllowedCapabilities ??
                WorkerEffectCapabilitySet.None,
            AllowedExceptionTypes = [.. value?.AllowedExceptionTypes ?? []]
        };
    }

    private static WorkerEffectViolationWitness? CopyWitness(
        WorkerEffectViolationWitness? value)
    {
        return value == null
            ? null
            : new WorkerEffectViolationWitness
            {
                Kind = value.Kind,
                Detail = value.Detail,
                Effects = value.Effects,
                Capabilities = value.Capabilities,
                ExactExceptionTypeHierarchy = [.. value.ExactExceptionTypeHierarchy],
                Location = CopyLocation(value.Location)
            };
    }

    private static CompilerEffectReplayArtifact? CopyReplay(
        CompilerEffectReplayArtifact? value)
    {
        return value == null
            ? null
            : new CompilerEffectReplayArtifact
            {
                PathKind = value.PathKind,
                ConstraintSha256 = value.ConstraintSha256,
                Events = [.. value.Events.Select(CopyReplayEvent)]
            };
    }

    private static CompilerEffectReplayEventArtifact CopyReplayEvent(
        CompilerEffectReplayEventArtifact value)
    {
        return new CompilerEffectReplayEventArtifact
        {
            Ordinal = value.Ordinal,
            Kind = value.Kind,
            SyntaxTreeOrdinal = value.SyntaxTreeOrdinal,
            SyntaxTreeSha256 = value.SyntaxTreeSha256,
            SyntaxTreeSnapshotSha256 = value.SyntaxTreeSnapshotSha256,
            SyntaxTreeLineMapSha256 = value.SyntaxTreeLineMapSha256,
            SyntaxStart = value.SyntaxStart,
            SyntaxLength = value.SyntaxLength,
            OperationIdentitySha256 = value.OperationIdentitySha256,
            MemberIdentity = value.MemberIdentity,
            MemberDocumentationId = value.MemberDocumentationId,
            TypeIdentity = value.TypeIdentity,
            TypeDocumentationId = value.TypeDocumentationId,
            SpecWitnessIdentifier = value.SpecWitnessIdentifier,
            ScalarOperands = [.. value.ScalarOperands],
            ExactExceptionTypeHierarchy = [.. value.ExactExceptionTypeHierarchy],
            Location = CopyLocation(value.Location),
            SourceTreeOrdinal = value.SourceTreeOrdinal,
            SourceTreePath = value.SourceTreePath,
            SourceTreeSha256 = value.SourceTreeSha256,
            SourceLineMapSha256 = value.SourceLineMapSha256
        };
    }

    private static WorkerSourceLocation CopyLocation(
        WorkerSourceLocation value)
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

    private sealed class ReplayEventComparer : IEqualityComparer<CompilerEffectReplayEventArtifact>
    {
        internal static readonly ReplayEventComparer Instance = new();

        public bool Equals(
            CompilerEffectReplayEventArtifact? left,
            CompilerEffectReplayEventArtifact? right)
        {
            if (left == null || right == null)
            {
                return left == null && right == null;
            }

            return left.Ordinal == right.Ordinal &&
                left.Kind == right.Kind &&
                left.SyntaxTreeOrdinal == right.SyntaxTreeOrdinal &&
                left.SyntaxTreeSha256 == right.SyntaxTreeSha256 &&
                left.SyntaxTreeSnapshotSha256 == right.SyntaxTreeSnapshotSha256 &&
                left.SyntaxTreeLineMapSha256 == right.SyntaxTreeLineMapSha256 &&
                left.SyntaxStart == right.SyntaxStart &&
                left.SyntaxLength == right.SyntaxLength &&
                left.OperationIdentitySha256 == right.OperationIdentitySha256 &&
                left.MemberIdentity == right.MemberIdentity &&
                left.MemberDocumentationId == right.MemberDocumentationId &&
                left.TypeIdentity == right.TypeIdentity &&
                left.TypeDocumentationId == right.TypeDocumentationId &&
                left.SpecWitnessIdentifier == right.SpecWitnessIdentifier &&
                left.ScalarOperands.SequenceEqual(right.ScalarOperands) &&
                left.ExactExceptionTypeHierarchy.SequenceEqual(
                    right.ExactExceptionTypeHierarchy,
                    StringComparer.Ordinal) &&
                left.SourceTreeOrdinal == right.SourceTreeOrdinal &&
                left.SourceTreePath == right.SourceTreePath &&
                left.SourceTreeSha256 == right.SourceTreeSha256 &&
                left.SourceLineMapSha256 == right.SourceLineMapSha256 &&
                LocationsEqual(left.Location, right.Location);
        }

        public int GetHashCode(
            CompilerEffectReplayEventArtifact value)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + value.Ordinal;
                hash = hash * 31 + (int)value.Kind;
                hash = hash * 31 + value.SyntaxTreeOrdinal;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    value.SyntaxTreeSha256 ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    value.SyntaxTreeSnapshotSha256 ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    value.SyntaxTreeLineMapSha256 ?? string.Empty);
                hash = hash * 31 + value.SyntaxStart;
                hash = hash * 31 + value.SyntaxLength;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    value.OperationIdentitySha256 ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    value.MemberIdentity ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    value.MemberDocumentationId ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    value.TypeIdentity ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    value.TypeDocumentationId ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    value.SpecWitnessIdentifier ?? string.Empty);
                hash = hash * 31 + value.SourceTreeOrdinal;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    value.SourceTreePath ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    value.SourceTreeSha256 ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    value.SourceLineMapSha256 ?? string.Empty);
                foreach (var operand in value.ScalarOperands ?? [])
                {
                    hash = hash * 31 + operand.GetHashCode();
                }
                foreach (var type in value.ExactExceptionTypeHierarchy ?? [])
                {
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(type);
                }
                var location = value.Location;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(
                    location?.Path ?? string.Empty);
                hash = hash * 31 + (location?.Start ?? 0);
                hash = hash * 31 + (location?.Length ?? 0);
                hash = hash * 31 + (location?.Line ?? 0);
                hash = hash * 31 + (location?.Column ?? 0);
                return hash;
            }
        }
    }
}
