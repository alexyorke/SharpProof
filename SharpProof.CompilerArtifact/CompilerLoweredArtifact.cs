using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;
internal static class CompilerLoweredArtifact
{
    private static readonly ImmutableArray<WorkerClaimEvidence> ManifestEvidenceMap =
    [
        WorkerClaimEvidence.DirectClause,
        WorkerClaimEvidence.ReturnAttribute,
        WorkerClaimEvidence.CompanionClause
    ];

    internal static CompilerCallableArtifact Encode(CompilerCallablePreparation preparation)
    {
        preparation = ArgumentNullGuard.NotNull(preparation, nameof(preparation));

        if (!preparation.IsSuccess)
        {
            return new CompilerCallableArtifact
            {
                CallableId = preparation.Entry.CallableId,
                FailureReason = preparation.FailureReason,
                Total = CompilerTotalCallableArtifactCodec.Encode(preparation.Total),
                TotalEntry = CompilerTotalCallableArtifactCodec.EncodeEntry(preparation.TotalEntry)
            };
        }

        var body = preparation.Body;
        if (body is { SummaryCalls.Count: > 0 })
        { throw new InvalidDataException("Relational-summary production is retired."); }
        var roots = preparation.Clauses.Select(static clause => clause.Condition).ToArray();
        var variables = new List<IrVarId>();
        var seenVariables = new HashSet<IrVarId>();
        void AddVariable(IrVarId variable)
        {
            if (seenVariables.Add(variable))
            {
                variables.Add(variable);
            }
        }

        foreach (var variable in preparation.Variables)
        {
            AddVariable(variable.Variable);
            if (variable.CurrentStateVariable is { } current)
            {
                AddVariable(current);
            }
        }
        if (body != null)
        {
            foreach (var binding in body.ParameterBindings)
            {
                AddVariable(binding.Key);
                AddVariable(binding.Value);
            }
        }
        var encoded = PortableIrGraphCodec.Encode(preparation.Factory, body?.Program, roots, variables);
        var canonicalByVariable = preparation.Variables.ToDictionary(
            static variable => variable.Variable);
        var artifact = new CompilerCallableArtifact
        {
            CallableId = preparation.Entry.CallableId,
            FailureReason = WorkerClaimReason.None,
            Total = CompilerTotalCallableArtifactCodec.Encode(preparation.Total),
            TotalEntry = CompilerTotalCallableArtifactCodec.EncodeEntry(preparation.TotalEntry),
            Graph = encoded.Graph,
            EffectClaims = preparation.EffectClaims.ToArray(),
            Clauses = [.. preparation.Clauses.Select((clause, index) =>
                new CompilerClauseArtifact {
                    Kind = clause.Kind, Evidence = clause.Evidence, Root = index,
                    ClaimId = clause.ClaimId, AssumptionId = clause.AssumptionId,
                })],
            Variables = [.. preparation.Variables.Select(variable => {
                var source = variable.SourceIntegerInterval;
                var sourceOrdinal = -1;
                if (variable.Role == CompilerVariableRole.PreState &&
                    variable.CurrentStateVariable is { } current &&
                    canonicalByVariable.TryGetValue(current, out var currentVariable))
                {
                    source = currentVariable.SourceIntegerInterval;
                    sourceOrdinal = currentVariable.Ordinal;
                }
                return new CompilerVariableArtifact {
                    Role = variable.Role, Ordinal = variable.Ordinal,
                    Variable = encoded.VariableIndices[variable.Variable],
                    CurrentStateVariable = variable.CurrentStateVariable.HasValue
                        ? encoded.VariableIndices[variable.CurrentStateVariable.Value] : -1,
                    SourceOrdinal = sourceOrdinal,
                    Minimum = variable.SourceIntegerInterval?.Minimum,
                    Maximum = variable.SourceIntegerInterval?.Maximum,
                    ScalarDomain = ScalarDomain(source),
                    ModelLabel = variable.ModelLabel
                };
            })]
        };
        if (body == null)
        {
            return artifact;
        }

        artifact.Body = new CompilerBodyArtifact { Kind = body.Kind };
        if (body.Kind == CompilerPreparedBodyKind.Trivial)
        {
            return artifact;
        }

        artifact.Body.ParameterBindings = [.. body.ParameterBindings
            .OrderBy(static item => item.Key.Value)
            .Select(item => {
                var sourceIndex = encoded.VariableIndices[item.Key];
                var sourceInfo = preparation.Factory.GetVariableInfo(item.Key);
                var target = canonicalByVariable[item.Value];
                return new CompilerVariableMappingArtifact {
                    Source = sourceIndex,
                    SourceOrdinal = target.Ordinal,
                    SourceType = encoded.Graph.Variables[sourceIndex].Type,
                    SourceName = preparation.Factory.GetString(sourceInfo.Name),
                    Target = encoded.VariableIndices[item.Value]
                };
            })];
        var allCalls = body.SpecCalls.Values
            .Select(static call => (
                call.Instruction,
                call.CallIdentity))
            .OrderBy(call => encoded.InstructionIndices[call.Instruction])
            .ToArray();
        if (allCalls.Length > 0)
        {
            var encodedInstructions = encoded.Graph.Blocks
                .SelectMany(static block => block.Instructions)
                .ToArray();
            foreach (var call in allCalls)
            {
                var instruction = encodedInstructions[
                    encoded.InstructionIndices[call.Instruction]];
                var member = encoded.Graph.Members[instruction.B];
                if (member.DocumentationCommentId is { } existing && existing != call.CallIdentity)
                {
                    throw new InvalidDataException("A lowered member has conflicting semantic identities.");
                }

                member.DocumentationCommentId = call.CallIdentity;
            }
        }
        artifact.Body.Calls = [.. allCalls
            .Select(item => new CompilerCallIdentityArtifact {
                Instruction = encoded.InstructionIndices[item.Instruction], Identity = item.CallIdentity
            })];
        artifact.Body.SpecCalls = [.. body.SpecCalls.Values.OrderBy(static item => item.Instruction.Value)
            .Select(item => new CompilerSpecCallArtifact {
                Instruction = encoded.InstructionIndices[item.Instruction], WitnessIdentifier = item.WitnessIdentifier,
                ConsumesMemoryHavoc = item.ConsumesMemoryHavoc
            })];
        return artifact;
    }

    private static CompilerScalarDomain ScalarDomain(
        CompilerIntegerInterval? interval)
    {
        return interval switch
        {
            null => CompilerScalarDomain.None,
            { Minimum: sbyte.MinValue, Maximum: sbyte.MaxValue } =>
                CompilerScalarDomain.SByte,
            { Minimum: byte.MinValue, Maximum: byte.MaxValue } =>
                CompilerScalarDomain.Byte,
            { Minimum: short.MinValue, Maximum: short.MaxValue } =>
                CompilerScalarDomain.Short,
            { Minimum: ushort.MinValue, Maximum: ushort.MaxValue } =>
                CompilerScalarDomain.UShort,
            { Minimum: int.MinValue, Maximum: int.MaxValue } =>
                CompilerScalarDomain.Int,
            { Minimum: uint.MinValue, Maximum: uint.MaxValue } =>
                CompilerScalarDomain.UInt,
            { Minimum: long.MinValue, Maximum: long.MaxValue } =>
                CompilerScalarDomain.Long,
            _ => throw new InvalidDataException(
                "A compiler integer interval is not a primitive scalar domain.")
        };
    }
    internal static ImmutableArray<CompilerCallablePreparation> Decode(
        CompilerCallableArtifact[] artifacts,
        WorkerClaimManifest manifest,
        CompilerCompilationSnapshot compilation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (artifacts == null)
        {
            throw new InvalidDataException("The lowered callable payload is missing.");
        }

        if (compilation == null)
        {
            throw new InvalidDataException("The compiler compilation evidence is missing.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var callables = manifest.Callables.ToDictionary(static item => item.CallableId, StringComparer.Ordinal);
        var claims = manifest.Claims.GroupBy(static item => item.CallableId)
            .ToDictionary(static group => group.Key,
                static group => group.OrderBy(static item => item.Ordinal).ToImmutableArray(),
                StringComparer.Ordinal);
        if (artifacts.Length != callables.Count)
        {
            throw new InvalidDataException("The lowered callable payload does not equal the manifest.");
        }
        var callableIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in artifacts)
        {
            if (artifact == null ||
                !callableIds.Add(artifact.CallableId) ||
                !callables.ContainsKey(artifact.CallableId))
            {
                throw new InvalidDataException("The lowered callable payload does not equal the manifest.");
            }
        }

        var result = ImmutableArray.CreateBuilder<CompilerCallablePreparation>(artifacts.Length);
        foreach (var artifact in artifacts.OrderBy(static item => item.CallableId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = callables[artifact.CallableId];
            var targetClaims = claims.TryGetValue(artifact.CallableId, out var rows) ? rows : [];
            if (!entry.ClaimIds.SequenceEqual(targetClaims.Select(static item => item.ClaimId), StringComparer.Ordinal))
            {
                throw new InvalidDataException("A lowered callable claim list does not equal the manifest.");
            }

            result.Add(Decode(
                artifact,
                entry,
                targetClaims,
                compilation,
                cancellationToken));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result.MoveToImmutable();
    }
    private static CompilerCallablePreparation Decode(
        CompilerCallableArtifact artifact,
        WorkerCallableManifestEntry entry,
        ImmutableArray<WorkerClaimManifestEntry> claims,
        CompilerCompilationSnapshot compilation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (artifact.FailureReason !=
                CompilerCallableArtifactReasonCatalog.SuccessReason &&
            !CompilerCallableArtifactReasonCatalog.IsFailureReason(
                artifact.FailureReason))
        {
            throw new InvalidDataException("A lowered callable reason is invalid.");
        }

        if (artifact.Body?.SummaryCalls is { Length: > 0 })
        { throw new InvalidDataException("Relational-summary descriptors are retired."); }

        var total = CompilerTotalCallableArtifactCodec.Decode(artifact.Total, entry, claims, cancellationToken);
        var totalEntry = CompilerTotalCallableArtifactCodec.DecodeEntry(artifact.TotalEntry, entry, cancellationToken);

        if (artifact.FailureReason !=
            CompilerCallableArtifactReasonCatalog.SuccessReason)
        {
            if (artifact.Graph != null || artifact.Body != null || artifact.Clauses is not { Length: 0 } ||
                artifact.Variables is not { Length: 0 })
            {
                throw new InvalidDataException("A failed lowered callable cannot contain executable evidence.");
            }

            return new CompilerCallablePreparation(
                new IrFactory(), entry, [], [], artifact.FailureReason, null)
            {
                EffectClaims = DecodeEffects(
                    artifact,
                    claims,
                    cancellationToken),
                Compilation = compilation,
                Total = total,
                TotalEntry = totalEntry
            };
        }
        if (artifact.Graph == null || artifact.Clauses == null || artifact.Variables == null)
        {
            throw new InvalidDataException("A successful lowered callable is incomplete.");
        }

        var decoded = PortableIrGraphCodec.Decode(
            artifact.Graph,
            ExternalVariableIndices(artifact),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (decoded.Roots.Count != artifact.Clauses.Length)
        {
            throw new InvalidDataException(
                "A lowered callable contains an invalid root closure.");
        }

        IrTerm Root(int index)
        {
            return At(decoded.Roots, index, "root");
        }

        IrVarId Variable(int index)
        {
            return At(decoded.Variables, index, "variable");
        }

        var clauses = artifact.Clauses.Select((row, index) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row == null ||
                !Enum.IsDefined(typeof(CompilerContractKind), row.Kind) ||
                !Enum.IsDefined(typeof(CompilerContractEvidence), row.Evidence) || row.Root != index ||
                (row.Kind == CompilerContractKind.Ensures
                    ? string.IsNullOrWhiteSpace(row.ClaimId) || row.AssumptionId != null
                    : row.ClaimId != null || string.IsNullOrWhiteSpace(row.AssumptionId)))
            {
                throw new InvalidDataException("A lowered contract clause is invalid.");
            }

            var condition = Root(row.Root);
            if (condition.Type != decoded.Factory.BooleanType)
            {
                throw new InvalidDataException(
                    "A lowered contract predicate is not Boolean.");
            }

            var clause = new CompilerPreparedClause(
                row.Kind,
                condition,
                row.Evidence,
                row.ClaimId,
                row.AssumptionId);

            return clause;
        }).ToImmutableArray();
        cancellationToken.ThrowIfCancellationRequested();
        var postconditionClaims = claims.Where(static item => item.Kind == WorkerClaimKind.Postcondition).ToArray();
        var loweredClaims = clauses.Where(static item => item.Kind == CompilerContractKind.Ensures).ToArray();
        if (!ClaimsMatchManifest(
                loweredClaims.Select(static item => (
                    item.ClaimId,
                    ManifestEvidence(item.Evidence))).ToArray(),
                postconditionClaims))
        {
            throw new InvalidDataException("Lowered claims do not equal the manifest.");
        }

        var declaredClauseAssumptions = entry.Assumptions
            .Where(static item => item.Kind is WorkerAssumptionKind.Precondition or WorkerAssumptionKind.UserAssume)
            .Select(static item => (item.Id, item.Kind)).OrderBy(static item => item.Id, StringComparer.Ordinal);
        var loweredClauseAssumptions = clauses
            .Where(static item => item.Kind != CompilerContractKind.Ensures)
            .Select(static item => (item.AssumptionId!, item.Kind == CompilerContractKind.Requires
                ? WorkerAssumptionKind.Precondition : WorkerAssumptionKind.UserAssume))
            .OrderBy(static item => item.Item1, StringComparer.Ordinal);
        if (!declaredClauseAssumptions.SequenceEqual(loweredClauseAssumptions))
        {
            throw new InvalidDataException("Lowered assumptions do not equal the manifest.");
        }

        var variables = artifact.Variables.Select(row =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row == null ||
                !Enum.IsDefined(typeof(CompilerVariableRole), row.Role) ||
                row.Minimum.HasValue != row.Maximum.HasValue ||
                row.Minimum > row.Maximum)
            {
                throw new InvalidDataException("A lowered canonical variable is invalid.");
            }

            var variable = Variable(row.Variable);
            IrVarId? current = row.CurrentStateVariable switch
            {
                -1 => null,
                >= 0 => Variable(row.CurrentStateVariable),
                _ => throw new InvalidDataException(
                    "A lowered canonical variable is invalid.")
            };
            CompilerIntegerInterval? interval = row.Minimum.HasValue
                ? new CompilerIntegerInterval(row.Minimum.Value, row.Maximum!.Value) : null;
            return new CompilerCanonicalVariable(row.Role, row.Ordinal, variable, current, interval, row.ModelLabel);
        }).ToImmutableArray();
        cancellationToken.ThrowIfCancellationRequested();
        ValidateVariables(decoded.Factory, variables, artifact.Variables);
        cancellationToken.ThrowIfCancellationRequested();
        var body = DecodeBody(
            artifact.Body,
            artifact.Graph,
            decoded,
            variables);
        cancellationToken.ThrowIfCancellationRequested();
        if (postconditionClaims.Length != 0 && body == null)
        {
            throw new InvalidDataException(
                "A successful postcondition callable requires a lowered body.");
        }
        return new CompilerCallablePreparation(
            decoded.Factory, entry, clauses, variables, WorkerClaimReason.None, body)
        {
            EffectClaims = DecodeEffects(
                artifact,
                claims,
                cancellationToken),
            Compilation = compilation,
            Total = total,
            TotalEntry = totalEntry
        };
    }

    internal static bool ClaimsMatchManifest(
        (string? ClaimId, WorkerClaimEvidence Evidence)[] lowered,
        WorkerClaimManifestEntry[] manifest)
    {
        return lowered.Select(static item => item.ClaimId).SequenceEqual(
                manifest.Select(static item => item.ClaimId), StringComparer.Ordinal) &&
            lowered.Select(static item => item.Evidence).SequenceEqual(
                manifest.Select(static item => item.Evidence));
    }

    private static int[] ExternalVariableIndices(
        CompilerCallableArtifact artifact)
    {
        var indices = new HashSet<int>();
        void Add(int index)
        {
            if (index >= 0)
            {
                indices.Add(index);
            }
        }

        foreach (var variable in artifact.Variables ?? [])
        {
            if (variable != null)
            {
                Add(variable.Variable);
                Add(variable.CurrentStateVariable);
            }
        }
        foreach (var binding in artifact.Body?.ParameterBindings ?? [])
        {
            if (binding != null)
            {
                Add(binding.Source);
                Add(binding.Target);
            }
        }
        return [.. indices.OrderBy(static index => index)];
    }
    private static ImmutableArray<CompilerEffectClaimArtifact> DecodeEffects(
        CompilerCallableArtifact artifact,
        ImmutableArray<WorkerClaimManifestEntry> claims,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (artifact.EffectClaims == null)
        {
            throw new InvalidDataException("Compiler effect-claim evidence is missing.");
        }

        var expected = claims.Where(static item => item.Kind == WorkerClaimKind.Effect).ToArray();
        if (artifact.EffectClaims.Length != expected.Length)
        {
            throw new InvalidDataException("Compiler effect-claim evidence does not equal the manifest.");
        }

        var effectClaimIds = new HashSet<string?>(StringComparer.Ordinal);
        for (var index = 0; index < artifact.EffectClaims.Length; index++)
        {
            if (!effectClaimIds.Add(artifact.EffectClaims[index]?.ClaimId))
            {
                throw new InvalidDataException(
                    "Compiler effect-claim evidence does not equal the manifest.");
            }
        }

        for (var index = 0; index < expected.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = artifact.EffectClaims[index];
            CompilerEffectClaimArtifactCodec.Validate(evidence);
            if (evidence.ClaimId != expected[index].ClaimId || evidence.ContractKind != expected[index].EffectContractKind)
            {
                throw new InvalidDataException("Compiler effect-claim evidence does not equal the manifest.");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return [.. artifact.EffectClaims];
    }
    private static void ValidateVariables(
        IrFactory factory,
        ImmutableArray<CompilerCanonicalVariable> variables,
        CompilerVariableArtifact[] artifactRows)
    {
        var canonical = new HashSet<IrVarId>(variables.Select(static item => item.Variable));
        var parameters = variables.Where(static item => item.Role == CompilerVariableRole.Parameter)
            .OrderBy(static item => item.Ordinal).ToArray();
        if (canonical.Count != variables.Length ||
            variables.Select(static item => item.ModelLabel).Distinct(StringComparer.Ordinal).Count() != variables.Length ||
            variables.Count(static item => item.Role == CompilerVariableRole.Receiver) > 1 ||
            variables.Count(static item => item.Role == CompilerVariableRole.Result) > 1 ||
            !parameters.Select(static item => item.Ordinal).SequenceEqual(Enumerable.Range(0, parameters.Length)))
        {
            throw new InvalidDataException("Lowered canonical variable roles are invalid.");
        }

        var currentByVariable = variables
            .Where(static item => item.Role is CompilerVariableRole.Receiver or CompilerVariableRole.Parameter)
            .ToDictionary(static item => item.Variable);
        for (var index = 0; index < variables.Length; index++)
        {
            var item = variables[index];
            var row = artifactRows[index];
            var info = factory.GetVariableInfo(item.Variable);
            var label = factory.GetString(info.Name);
            var sourceOrdinal = -1;
            var sourceInterval = item.SourceIntegerInterval;
            if (item.Role == CompilerVariableRole.PreState &&
                item.CurrentStateVariable is { } currentState &&
                currentByVariable.TryGetValue(currentState, out var currentVariable))
            {
                sourceOrdinal = currentVariable.Ordinal;
                sourceInterval = currentVariable.SourceIntegerInterval;
                canonical.Remove(currentState);
            }
            var scalarDomain = ScalarDomain(sourceInterval);
            var shape = item.Role switch
            {
                CompilerVariableRole.Receiver =>
                    item.Ordinal == -1 && item.CurrentStateVariable == null && item.ModelLabel == "receiver",
                CompilerVariableRole.Parameter => item.Ordinal >= 0 && item.CurrentStateVariable == null &&
                    item.ModelLabel == "parameter:" + item.Ordinal.ToString(CultureInfo.InvariantCulture),
                CompilerVariableRole.Result =>
                    item.Ordinal == -1 && item.CurrentStateVariable == null && item.ModelLabel == "result",
                CompilerVariableRole.PreState => item.Ordinal == -1 && item.CurrentStateVariable.HasValue &&
                    currentByVariable.ContainsKey(item.CurrentStateVariable.Value) &&
                    item.ModelLabel.StartsWith("pre:", StringComparison.Ordinal) &&
                    int.TryParse(item.ModelLabel.Substring(4), NumberStyles.None,
                        CultureInfo.InvariantCulture, out var ordinal) && ordinal >= 0,
                _ => false
            };
            if (!shape || item.ModelLabel != label ||
                row.SourceOrdinal != sourceOrdinal ||
                row.ScalarDomain != scalarDomain ||
                item.CurrentStateVariable.HasValue && factory.GetVariableInfo(item.CurrentStateVariable.Value).Type != info.Type ||
                item.SourceIntegerInterval is { } interval &&
                    (item.Role == CompilerVariableRole.PreState || info.Type != factory.IntegerType || !IsPrimitiveInterval(interval)))
            {
                throw new InvalidDataException("A lowered canonical variable is invalid.");
            }
        }
        if (canonical.Count != variables.Length - variables.Count(static item => item.CurrentStateVariable.HasValue))
        {
            throw new InvalidDataException("Lowered pre-state variables are not injective.");
        }
    }

    private static bool IsPrimitiveInterval(CompilerIntegerInterval value)
    {
        return (value.Minimum, value.Maximum) is
        (sbyte.MinValue, sbyte.MaxValue) or (byte.MinValue, byte.MaxValue) or (short.MinValue, short.MaxValue) or
        (ushort.MinValue, ushort.MaxValue) or (int.MinValue, int.MaxValue) or (uint.MinValue, uint.MaxValue) or
        (long.MinValue, long.MaxValue);
    }

    private static CompilerPreparedBody? DecodeBody(
        CompilerBodyArtifact? row,
        PortableIrGraph portable,
        DecodedPortableIrGraph graph,
        ImmutableArray<CompilerCanonicalVariable> variables)
    {
        if (row == null)
        {
            if (graph.Program != null)
            {
                throw new InvalidDataException("A bodyless callable cannot contain a program.");
            }

            return null;
        }
        if (row.ParameterBindings == null || row.Calls == null ||
            row.SpecCalls == null || row.SummaryCalls == null)
        {
            throw new InvalidDataException("A lowered body is incomplete.");
        }

        if (row.Kind == CompilerPreparedBodyKind.Trivial)
        {
            if (graph.Program != null || row.ParameterBindings.Length != 0 ||
                row.Calls.Length != 0 || row.SpecCalls.Length != 0 ||
                row.SummaryCalls.Length != 0)
            {
                throw new InvalidDataException("A trivial lowered body is invalid.");
            }

            return CompilerPreparedBody.Trivial();
        }
        if (row.Kind != CompilerPreparedBodyKind.Program ||
            graph.Program == null || graph.Program.Blocks.Length == 0 ||
            graph.Program.Blocks.Sum(static block => (long)block.Instructions.Length) > CompilerPreparedBody.MaximumInstructions ||
            graph.Program.Entry.Value != 0 || graph.Program.Entry != graph.Program.Blocks[0].Id)
        {
            throw new InvalidDataException("A lowered program body is invalid.");
        }

        var programVariables = ValidateExecutableBody(graph.Program, variables);
        if (row.SummaryCalls.Length != 0)
        { throw new InvalidDataException("Relational-summary descriptors are retired."); }

        var canonical = new HashSet<IrVarId>(variables.Select(static item => item.Variable));
        var parameters = variables
            .Where(static item => item.Role == CompilerVariableRole.Parameter)
            .ToDictionary(static item => item.Variable);
        var bindings = ImmutableDictionary.CreateBuilder<IrVarId, IrVarId>();
        var targets = new HashSet<IrVarId>();
        var sourceOrdinals = new HashSet<int>();
        foreach (var item in row.ParameterBindings)
        {
            if (item == null)
            {
                throw new InvalidDataException("A lowered parameter binding is invalid.");
            }

            var source = At(graph.Variables, item.Source, "variable");
            var target = At(graph.Variables, item.Target, "variable");
            var sourceInfo = graph.Factory.GetVariableInfo(source);
            var targetInfo = graph.Factory.GetVariableInfo(target);
            var sourceName = graph.Factory.GetString(sourceInfo.Name);
            var portableSource = At(portable.Variables, item.Source, "variable");
            if (canonical.Contains(source) || !parameters.TryGetValue(target, out var parameter) ||
                source == target || item.SourceOrdinal != parameter.Ordinal ||
                item.SourceType < 0 || item.SourceType >= portable.Types.Length ||
                portableSource.Type != item.SourceType || item.SourceName != sourceName ||
                !sourceName.StartsWith("Parameter:", StringComparison.Ordinal) ||
                !programVariables.Contains(source) || sourceInfo.Type != targetInfo.Type ||
                bindings.ContainsKey(source) || !targets.Add(target) ||
                !sourceOrdinals.Add(item.SourceOrdinal))
            {
                throw new InvalidDataException("A lowered parameter binding is invalid.");
            }

            bindings.Add(source, target);
        }
        var specs = ImmutableDictionary.CreateBuilder<IrInstructionId, CompilerPreparedSpecCall>();
        var callCount = graph.Instructions.Count(static instruction => instruction is IrCallInstruction);
        if (row.Calls.Length != callCount ||
            row.SpecCalls.Length != callCount)
        {
            throw new InvalidDataException(
                "Lowered call evidence does not equal program calls.");
        }

        var portableInstructions = row.Calls.Length == 0
            ? Array.Empty<PortableIrInstruction>()
            : portable.Blocks
                .SelectMany(static block => block.Instructions)
                .ToArray();
        var identities = new Dictionary<IrInstructionId, string>();
        for (var index = 0; index < row.Calls.Length; index++)
        {
            var identity = row.Calls[index] ??
                throw new InvalidDataException("A lowered call identity is invalid.");
            var instruction = At(
                graph.Instructions,
                identity.Instruction,
                "instruction");
            var portableInstruction = At(
                portableInstructions,
                identity.Instruction,
                "instruction");
            if (instruction is not IrCallInstruction call ||
                portableInstruction.Kind != IrInstructionKind.Call ||
                string.IsNullOrWhiteSpace(identity.Identity) ||
                At(
                    portable.Members,
                    portableInstruction.B,
                    "member").DocumentationCommentId != identity.Identity ||
                identities.ContainsKey(call.Id))
            {
                throw new InvalidDataException("A lowered call descriptor is invalid.");
            }

            identities.Add(call.Id, identity.Identity);
        }

        foreach (var spec in row.SpecCalls)
        {
            if (spec == null)
            {
                throw new InvalidDataException(
                    "A lowered spec-call descriptor is invalid.");
            }

            var instruction = At(
                graph.Instructions,
                spec.Instruction,
                "instruction");
            if (instruction is not IrCallInstruction call ||
                !identities.TryGetValue(call.Id, out var identity) ||
                string.IsNullOrWhiteSpace(spec.WitnessIdentifier) ||
                specs.ContainsKey(call.Id))
            {
                throw new InvalidDataException(
                    "A lowered spec-call descriptor is invalid.");
            }

            specs.Add(call.Id, new CompilerPreparedSpecCall(
                call.Id,
                identity,
                spec.WitnessIdentifier,
                spec.ConsumesMemoryHavoc));
        }

        if (specs.Count != callCount)
        {
            throw new InvalidDataException(
                "Lowered call evidence is incomplete.");
        }

        return CompilerPreparedBody.ProgramBody(
            graph.Program,
            bindings.ToImmutable(),
            specs.ToImmutable(),
            ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty);
    }

    private static HashSet<IrVarId> ValidateExecutableBody(
        IrProgram program,
        ImmutableArray<CompilerCanonicalVariable> variables)
    {
        const int maximumReachableBlocks = 64;
        var blocks = program.Blocks.ToDictionary(static block => block.Id);
        var colors = new Dictionary<IrBlockId, byte>();
        var reachable = 0;
        var programVariables = new HashSet<IrVarId>();
        var resultType = variables
            .SingleOrDefault(static item =>
                item.Role == CompilerVariableRole.Result) is { } result
            ? program.Factory.GetVariableInfo(result.Variable).Type
            : (IrTypeId?)null;

        if (!Visit(program.Entry))
        {
            throw new InvalidDataException(
                "A lowered program body is cyclic or exceeds its reachable block limit.");
        }

        foreach (var block in program.Blocks)
        {
            if (!colors.ContainsKey(block.Id))
            {
                CollectBlockVariables(block, programVariables);
            }
        }

        return programVariables;

        bool Visit(IrBlockId blockId)
        {
            if (colors.TryGetValue(blockId, out var color))
            {
                return color == 2;
            }

            if (++reachable > maximumReachableBlocks ||
                !blocks.TryGetValue(blockId, out var block) ||
                block.Instructions.IsDefaultOrEmpty)
            {
                return false;
            }

            colors.Add(blockId, 1);
            CollectBlockVariables(block, programVariables);
            var terminator = block.Instructions[block.Instructions.Length - 1];
            if (terminator is IrReturnInstruction returned)
            {
                if (resultType.HasValue &&
                    (returned.Value == null ||
                     returned.Value.Type != resultType.Value))
                {
                    return false;
                }
            }
            else
            {
                var successors = Successors(terminator);
                if (successors.First is { } first && !Visit(first))
                {
                    return false;
                }
                if (successors.Second is { } second && !Visit(second))
                {
                    return false;
                }
            }

            colors[blockId] = 2;
            return true;
        }

        static IrSuccessors Successors(IrInstruction terminator)
        {
            return IrInstructionFacts.TryGetSuccessors(terminator) ??
                throw new InvalidDataException(
                    "A lowered block does not end in a terminator.");
        }
    }

    internal static HashSet<IrVarId> CollectProgramVariables(IrProgram program)
    {
        var variables = new HashSet<IrVarId>();
        foreach (var block in program.Blocks)
        {
            CollectBlockVariables(block, variables);
        }

        return variables;
    }

    private static void CollectBlockVariables(
        IrBasicBlock block,
        HashSet<IrVarId> variables)
    {
        foreach (var instruction in block.Instructions)
        {
            switch (instruction)
            {
                case IrAssignInstruction assign:
                    variables.Add(assign.Target);
                    AddTermVariables(assign.Value, variables);
                    break;
                case IrLoadInstruction load:
                    variables.Add(load.Target);
                    AddLocationVariables(load.Location, variables);
                    break;
                case IrStoreInstruction store:
                    AddLocationVariables(store.Location, variables);
                    AddTermVariables(store.Value, variables);
                    break;
                case IrCallInstruction call:
                    if (call.Target.HasValue)
                    {
                        variables.Add(call.Target.Value);
                    }
                    AddTermVariables(call.Receiver, variables);
                    foreach (var argument in call.Arguments)
                    {
                        AddTermVariables(argument, variables);
                    }
                    break;
                case IrAssumeInstruction assume:
                    AddTermVariables(assume.Condition, variables);
                    break;
                case IrAssertInstruction assert:
                    AddTermVariables(assert.Condition, variables);
                    break;
                case IrHavocInstruction havoc:
                    variables.UnionWith(havoc.Variables);
                    break;
                case IrBranchInstruction branch:
                    AddTermVariables(branch.Condition, variables);
                    break;
                case IrReturnInstruction @return:
                    AddTermVariables(@return.Value, variables);
                    break;
            }
        }
    }

    private static void AddTermVariables(
        IrTerm? term,
        HashSet<IrVarId> variables)
    {
        if (term != null)
        {
            variables.UnionWith(IrTermAnalysis.CollectVariables(term));
        }
    }

    private static void AddLocationVariables(
        IrLocation location,
        HashSet<IrVarId> variables)
    {
        switch (location)
        {
            case IrMemberLocation member:
                AddTermVariables(member.Receiver, variables);
                foreach (var argument in member.Arguments)
                {
                    AddTermVariables(argument, variables);
                }
                break;
            case IrSequenceLocation sequence:
                AddTermVariables(sequence.Sequence, variables);
                AddTermVariables(sequence.Index, variables);
                break;
        }
    }

    internal static WorkerClaimEvidence ManifestEvidence(
        CompilerContractEvidence value)
    {
        var index = (int)value;
        return index >= 0 && index < ManifestEvidenceMap.Length
            ? ManifestEvidenceMap[index]
            : WorkerClaimEvidence.Unspecified;
    }

    private static T At<T>(IReadOnlyList<T> items, int index, string kind)
    {
        return index >= 0 && index < items.Count ? items[index] :
            throw new InvalidDataException("A lowered " + kind + " index is out of range.");
    }
}
