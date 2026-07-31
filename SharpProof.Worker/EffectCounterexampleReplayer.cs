namespace SharpProof.Worker;

internal static class EffectCounterexampleReplayer
{
    internal static WorkerEffectViolationWitness? Replay(
        CompilerCallablePreparation target,
        CompilerEffectClaimArtifact evidence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(evidence);
        cancellationToken.ThrowIfCancellationRequested();
        CompilerEffectClaimArtifactCodec.Validate(evidence);

        var replay = evidence.Replay ??
            throw Malformed("A refuted effect claim has no replay artifact.");
        if (replay.ConstraintSha256 !=
            ComputeConstraintIdentity(
                evidence.ContractKind,
                evidence.Constraint))
        {
            throw Malformed(
                "The effect replay constraint does not equal the selected contract.");
        }

        if (replay.PathKind !=
            CompilerEffectReplayPathKind.Unconditional)
        {
            throw Malformed(
                "An effect replay artifact has an invalid path kind.");
        }

        WorkerEffectViolationWitness? violation = null;
        for (var index = 0; index < replay.Events.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var effectEvent = replay.Events[index];
            ValidateEvent(target, effectEvent, index);
            var observed = Interpret(effectEvent);
            if (observed == null)
            {
                return null;
            }

            if (violation == null &&
                IsViolation(evidence, observed.Effects))
            {
                violation = observed;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return violation != null &&
            WitnessesEqual(violation, evidence.Witness)
                ? violation
                : null;
    }

    private static void ValidateEvent(
        CompilerCallablePreparation target,
        CompilerEffectReplayEventArtifact effectEvent,
        int ordinal)
    {
        if (effectEvent == null ||
            effectEvent.Ordinal != ordinal ||
            effectEvent.OperationIdentitySha256 !=
            ComputeOperationIdentity(effectEvent))
        {
            throw Malformed(
                "An effect replay event has an invalid identity or order.");
        }

        var trees = target.Compilation.SyntaxTrees;
        if (trees == null ||
            effectEvent.SyntaxTreeOrdinal < 0 ||
            effectEvent.SyntaxTreeOrdinal >= trees.Length)
        {
            throw Malformed(
                "An effect replay event names an unknown syntax tree.");
        }

        var tree = trees[effectEvent.SyntaxTreeOrdinal];
        if (tree == null ||
            effectEvent.SyntaxTreeSha256 != tree.Sha256 ||
            effectEvent.SyntaxStart < 0 ||
            effectEvent.SyntaxLength <= 0 ||
            effectEvent.SyntaxStart > tree.TextLength ||
            effectEvent.SyntaxLength >
            tree.TextLength - effectEvent.SyntaxStart)
        {
            throw Malformed(
                "An effect replay event does not fit its syntax tree.");
        }

        var location = effectEvent.Location;
        if (!WorkerProtocolJson.HasValidLocation(location) ||
            location.Start != effectEvent.SyntaxStart ||
            location.Length != effectEvent.SyntaxLength)
        {
            throw Malformed(
                "An effect replay event has an invalid mapped location.");
        }
    }

    private static WorkerEffectViolationWitness? Interpret(
        CompilerEffectReplayEventArtifact effectEvent)
    {
        var detail = effectEvent.Kind switch
        {
            CompilerEffectReplayEventKind.ManagedObjectAllocation
                when !string.IsNullOrWhiteSpace(
                    effectEvent.MemberIdentity) =>
                FirstNonblank(
                    effectEvent.MemberDocumentationId,
                    effectEvent.MemberIdentity),
            CompilerEffectReplayEventKind.ManagedArrayAllocation
                when string.IsNullOrEmpty(
                    effectEvent.MemberIdentity) &&
                effectEvent.MemberDocumentationId == null =>
                FirstNonblank(
                    effectEvent.TypeDocumentationId,
                    effectEvent.TypeIdentity),
            _ => null
        };
        if (detail == null ||
            string.IsNullOrWhiteSpace(effectEvent.TypeIdentity) ||
            effectEvent.SpecWitnessIdentifier != null ||
            effectEvent.ScalarOperands.Length != 0 ||
            effectEvent.ExactExceptionTypeHierarchy.Length != 0)
        {
            return null;
        }

        return new WorkerEffectViolationWitness
        {
            Kind = effectEvent.Kind ==
                CompilerEffectReplayEventKind.ManagedObjectAllocation
                    ? "managed-allocation"
                    : "managed-array-allocation",
            Detail = detail,
            Effects = WorkerEffectSet.Allocates,
            Location = Copy(effectEvent.Location)
        };
    }

    private static bool IsViolation(
        CompilerEffectClaimArtifact evidence,
        WorkerEffectSet observed)
    {
        return evidence.ContractKind switch
        {
            WorkerEffectContractKind.ZeroAllocations =>
                (observed & WorkerEffectSet.Allocates) != 0,
            WorkerEffectContractKind.EffectContract =>
                (observed & ~evidence.Constraint.AllowedEffects) != 0,
            _ => false
        };
    }

    private static bool WitnessesEqual(
        WorkerEffectViolationWitness actual,
        WorkerEffectViolationWitness? claimed)
    {
        return claimed != null &&
            actual.Kind == claimed.Kind &&
            actual.Detail == claimed.Detail &&
            actual.Effects == claimed.Effects &&
            actual.Capabilities == claimed.Capabilities &&
            actual.ExactExceptionTypeHierarchy.SequenceEqual(
                claimed.ExactExceptionTypeHierarchy,
                StringComparer.Ordinal) &&
            LocationsEqual(actual.Location, claimed.Location);
    }

    private static bool LocationsEqual(
        WorkerSourceLocation left,
        WorkerSourceLocation right)
    {
        return left.Path == right.Path &&
            left.Start == right.Start &&
            left.Length == right.Length &&
            left.Line == right.Line &&
            left.Column == right.Column;
    }

    private static WorkerSourceLocation Copy(
        WorkerSourceLocation source)
    {
        return new WorkerSourceLocation
        {
            Path = source.Path,
            Start = source.Start,
            Length = source.Length,
            Line = source.Line,
            Column = source.Column
        };
    }

    private static string? FirstNonblank(
        string? preferred,
        string fallback)
    {
        return !string.IsNullOrWhiteSpace(preferred)
            ? preferred
            : !string.IsNullOrWhiteSpace(fallback)
                ? fallback
                : null;
    }

    internal static string ComputeConstraintIdentity(
        WorkerEffectContractKind kind,
        CompilerEffectConstraintArtifact constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        using var hash = new EffectReplayHashWriter();
        hash.Add(
            "SharpProof.CompilerEffectReplayConstraint",
            1,
            kind,
            constraint.AllowedEffects,
            constraint.AllowedCapabilities);
        foreach (var type in constraint.AllowedExceptionTypes
                     .OrderBy(static item => item, StringComparer.Ordinal))
        {
            hash.Add(type);
        }

        return hash.Finish();
    }

    internal static string ComputeOperationIdentity(
        CompilerEffectReplayEventArtifact effectEvent)
    {
        ArgumentNullException.ThrowIfNull(effectEvent);
        var location = effectEvent.Location;
        using var hash = new EffectReplayHashWriter();
        hash.Add(
            "SharpProof.CompilerEffectReplayOperation",
            1,
            effectEvent.Kind,
            effectEvent.SyntaxTreeOrdinal,
            effectEvent.SyntaxTreeSha256,
            effectEvent.SyntaxStart,
            effectEvent.SyntaxLength,
            effectEvent.MemberIdentity,
            effectEvent.MemberDocumentationId,
            effectEvent.TypeIdentity,
            effectEvent.TypeDocumentationId,
            effectEvent.SpecWitnessIdentifier);
        hash.Add(effectEvent.ScalarOperands.Length);
        foreach (var operand in effectEvent.ScalarOperands)
        {
            hash.Add(operand);
        }

        hash.Add(effectEvent.ExactExceptionTypeHierarchy.Length);
        foreach (var type in effectEvent.ExactExceptionTypeHierarchy)
        {
            hash.Add(type);
        }

        return hash.Add(
                location?.Path,
                location?.Start ?? -1,
                location?.Length ?? -1,
                location?.Line ?? -1,
                location?.Column ?? -1)
            .Finish();
    }

    private static InvalidDataException Malformed(string message)
    {
        return new InvalidDataException(message);
    }

    private sealed class EffectReplayHashWriter : IDisposable
    {
        private readonly IncrementalHash _hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _finished;

        internal EffectReplayHashWriter Add(params object?[] values)
        {
            foreach (var value in values)
            {
                _ = value switch
                {
                    null => Add((string?)null),
                    string text => Add(text),
                    int integer => Add(integer),
                    long integer => Add(integer),
                    Enum enumeration => Add(enumeration),
                    _ => throw new ArgumentException(
                        "Effect replay hash values must use an exact " +
                        "supported type.",
                        nameof(values))
                };
            }

            return this;
        }

        internal EffectReplayHashWriter Add(string? value)
        {
            return value == null
                ? AddFrame(ValueKind.Null, [])
                : AddFrame(
                    ValueKind.String,
                    Encoding.UTF8.GetBytes(value));
        }

        internal EffectReplayHashWriter Add(int value)
        {
            return AddFrame(
                ValueKind.Int32,
                Encoding.UTF8.GetBytes(
                    value.ToString(CultureInfo.InvariantCulture)));
        }

        internal EffectReplayHashWriter Add(long value)
        {
            return AddFrame(
                ValueKind.Int64,
                Encoding.UTF8.GetBytes(
                    value.ToString(CultureInfo.InvariantCulture)));
        }

        private EffectReplayHashWriter Add(Enum value)
        {
            var name = value.ToString();
            if (name.Length == 0 ||
                name[0] == '-' ||
                char.IsDigit(name[0]))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Canonical enum values must have a declared name.");
            }

            var type = value.GetType();
            return AddFrame(
                ValueKind.Enum,
                Encoding.UTF8.GetBytes(
                    (type.Assembly.GetName().Name ?? string.Empty) +
                    "\n" +
                    (type.FullName ?? type.Name) +
                    "\n" +
                    name));
        }

        private EffectReplayHashWriter AddFrame(
            ValueKind kind,
            byte[] bytes)
        {
            ObjectDisposedException.ThrowIf(_finished, this);

            var length = bytes.Length;
            _hash.AppendData([
                (byte)kind,
                (byte)length,
                (byte)(length >> 8),
                (byte)(length >> 16),
                (byte)(length >> 24)
            ]);
            _hash.AppendData(bytes);
            return this;
        }

        internal string Finish()
        {
            ObjectDisposedException.ThrowIf(_finished, this);

            _finished = true;
            return string.Concat(
                _hash.GetHashAndReset().Select(static value =>
                    value.ToString(
                        "x2",
                        CultureInfo.InvariantCulture)));
        }

        public void Dispose()
        {
            _finished = true;
            _hash.Dispose();
        }

        private enum ValueKind : byte
        {
            Null,
            String,
            Boolean,
            Int32,
            UInt32,
            Int64,
            Bytes,
            Enum
        }
    }
}
