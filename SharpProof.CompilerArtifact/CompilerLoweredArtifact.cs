using System.Text.Json;
namespace SharpProof.CompilerArtifact;
internal static class CompilerLoweredArtifact
{
    private static readonly WorkerClaimEvidence[] ManifestEvidenceMap =
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
                FailureReason = preparation.FailureReason
            };
        }

        var body = preparation.Body;
        var orderedSummaryCalls = body?.SummaryCalls.Values
            .OrderBy(static item => item.Instruction.Value)
            .ToArray() ?? [];
        var roots = preparation.Clauses
            .Select(static clause => clause.Condition)
            .Concat(orderedSummaryCalls.Select(
                static call => call.NormalRelation))
            .ToArray();
        var variables = preparation.Variables
            .SelectMany(static variable => variable.CurrentStateVariable.HasValue
                ? new[] { variable.Variable, variable.CurrentStateVariable.Value }
                : [variable.Variable])
            .Concat(body?.ParameterBindings.SelectMany(
                static item => new[] { item.Key, item.Value }) ?? [])
            .Concat(orderedSummaryCalls.SelectMany(static call =>
                call.ExistentialVariables.Insert(0, call.Result)))
            .Distinct()
            .ToArray();
        var encoded = PortableIrGraphCodec.Encode(preparation.Factory, body?.Program, roots, variables);
        var canonicalByVariable = preparation.Variables.ToDictionary(
            static variable => variable.Variable);
        var artifact = new CompilerCallableArtifact
        {
            CallableId = preparation.Entry.CallableId,
            FailureReason = WorkerClaimReason.None,
            Graph = encoded.Graph,
            EffectClaims = preparation.EffectClaims.ToArray(),
            Clauses = [.. preparation.Clauses.Select((clause, index) =>
                new CompilerClauseArtifact {
                    Kind = clause.Kind, Evidence = clause.Evidence, Root = index,
                    ClaimId = clause.ClaimId, AssumptionId = clause.AssumptionId,
                    PredicateSha256 = PredicateSha256(preparation.Factory, clause)
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
        var instructions = encoded.Graph.Blocks.SelectMany(static block => block.Instructions).ToArray();
        var allCalls = body.SpecCalls.Values
            .Select(static call => (
                call.Instruction,
                call.CallIdentity))
            .Concat(body.SummaryCalls.Values.Select(static call => (
                call.Instruction,
                call.CallIdentity)))
            .OrderBy(static call => call.Instruction.Value)
            .ToArray();
        foreach (var call in allCalls)
        {
            var instruction = instructions[encoded.InstructionIndices[call.Instruction]];
            var member = encoded.Graph.Members[instruction.B];
            if (member.DocumentationCommentId is { } existing && existing != call.CallIdentity)
            {
                throw new InvalidDataException("A lowered member has conflicting semantic identities.");
            }

            member.DocumentationCommentId = call.CallIdentity;
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
        artifact.Body.SummaryCalls = [.. orderedSummaryCalls.Select((item, index) =>
            new CompilerSummaryCallArtifact {
                Instruction = encoded.InstructionIndices[item.Instruction],
                Identity = item.CallIdentity,
                Origin = item.Origin,
                Result = encoded.VariableIndices[item.Result],
                ExistentialVariables = [.. item.ExistentialVariables.Select(
                    variable => encoded.VariableIndices[variable])],
                NormalRelationRoot = preparation.Clauses.Length + index,
                EvidenceSha256 = item.EvidenceSha256,
                EvidenceIdentity = item.EvidenceIdentity,
                DependencyEvidence = [.. item.DependencyEvidence.Select(
                    static evidence => new CompilerSummaryEvidenceArtifact
                    {
                        Origin = evidence.Origin,
                        EvidenceSha256 = evidence.EvidenceSha256,
                        EvidenceIdentity = evidence.EvidenceIdentity
                    })]
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
            _ => throw new InvalidDataException(
                "A compiler integer interval is not a primitive scalar domain.")
        };
    }
    internal static ImmutableArray<CompilerCallablePreparation> Decode(
        CompilerCallableArtifact[] artifacts,
        WorkerClaimManifest manifest,
        CompilerCompilationSnapshot compilation)
    {
        if (artifacts == null)
        {
            throw new InvalidDataException("The lowered callable payload is missing.");
        }

        if (compilation == null)
        {
            throw new InvalidDataException("The compiler compilation evidence is missing.");
        }

        var callables = manifest.Callables.ToDictionary(static item => item.CallableId, StringComparer.Ordinal);
        var claims = manifest.Claims.GroupBy(static item => item.CallableId)
            .ToDictionary(static group => group.Key,
                static group => group.OrderBy(static item => item.Ordinal).ToImmutableArray(),
                StringComparer.Ordinal);
        if (artifacts.Length != callables.Count ||
            artifacts.Select(static item => item?.CallableId).Distinct(StringComparer.Ordinal).Count() != artifacts.Length ||
            artifacts.Any(item => item == null || !callables.ContainsKey(item.CallableId)))
        {
            throw new InvalidDataException("The lowered callable payload does not equal the manifest.");
        }

        var result = ImmutableArray.CreateBuilder<CompilerCallablePreparation>(artifacts.Length);
        foreach (var artifact in artifacts.OrderBy(static item => item.CallableId, StringComparer.Ordinal))
        {
            var entry = callables[artifact.CallableId];
            var targetClaims = claims.TryGetValue(artifact.CallableId, out var rows) ? rows : [];
            if (!entry.ClaimIds.SequenceEqual(targetClaims.Select(static item => item.ClaimId), StringComparer.Ordinal))
            {
                throw new InvalidDataException("A lowered callable claim list does not equal the manifest.");
            }

            result.Add(Decode(artifact, entry, targetClaims, compilation));
        }
        return result.MoveToImmutable();
    }
    private static CompilerCallablePreparation Decode(
        CompilerCallableArtifact artifact,
        WorkerCallableManifestEntry entry,
        ImmutableArray<WorkerClaimManifestEntry> claims,
        CompilerCompilationSnapshot compilation)
    {
        if (artifact.FailureReason !=
                CompilerCallableArtifactReasonCatalog.SuccessReason &&
            !CompilerCallableArtifactReasonCatalog.IsFailureReason(
                artifact.FailureReason))
        {
            throw new InvalidDataException("A lowered callable reason is invalid.");
        }

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
                EffectClaims = DecodeEffects(artifact, claims),
                Compilation = compilation
            };
        }
        if (artifact.Graph == null || artifact.Clauses == null || artifact.Variables == null)
        {
            throw new InvalidDataException("A successful lowered callable is incomplete.");
        }

        var decoded = PortableIrGraphCodec.Decode(
            artifact.Graph,
            ExternalVariableIndices(artifact));
        var summaryRootCount = artifact.Body?.SummaryCalls?.Length ?? 0;
        if (decoded.Roots.Count != artifact.Clauses.Length + summaryRootCount)
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
            if (row == null ||
                !Enum.IsDefined(typeof(CompilerContractKind), row.Kind) ||
                !Enum.IsDefined(typeof(CompilerContractEvidence), row.Evidence) || row.Root != index ||
                (row.Kind == CompilerContractKind.Ensures
                    ? string.IsNullOrWhiteSpace(row.ClaimId) || row.AssumptionId != null
                    : row.ClaimId != null || string.IsNullOrWhiteSpace(row.AssumptionId)) ||
                !WorkerProtocolJson.IsSha256(row.PredicateSha256))
            {
                throw new InvalidDataException("A lowered contract clause is invalid.");
            }

            var clause = new CompilerPreparedClause(row.Kind, Root(row.Root), row.Evidence, row.ClaimId, row.AssumptionId);
            if (row.PredicateSha256 != PredicateSha256(decoded.Factory, clause))
            {
                throw new InvalidDataException("A lowered contract predicate does not equal its compiler inventory.");
            }

            return clause;
        }).ToImmutableArray();
        var postconditionClaims = claims.Where(static item => item.Kind == WorkerClaimKind.Postcondition).ToArray();
        var loweredClaims = clauses.Where(static item => item.Kind == CompilerContractKind.Ensures).ToArray();
        if (loweredClaims.Length != postconditionClaims.Length ||
            !loweredClaims.Select(static item => item.ClaimId!).SequenceEqual(
                postconditionClaims.Select(static item => item.ClaimId), StringComparer.Ordinal) ||
            !loweredClaims.Select(static item => ManifestEvidence(item.Evidence)).SequenceEqual(
                postconditionClaims.Select(static item => item.Evidence)))
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
            if (row == null ||
                !Enum.IsDefined(typeof(CompilerVariableRole), row.Role) ||
                row.Minimum.HasValue != row.Maximum.HasValue ||
                row.Minimum > row.Maximum)
            {
                throw new InvalidDataException("A lowered canonical variable is invalid.");
            }

            var variable = Variable(row.Variable);
            IrVarId? current = row.CurrentStateVariable < 0 ? null : Variable(row.CurrentStateVariable);
            CompilerIntegerInterval? interval = row.Minimum.HasValue
                ? new CompilerIntegerInterval(row.Minimum.Value, row.Maximum!.Value) : null;
            return new CompilerCanonicalVariable(row.Role, row.Ordinal, variable, current, interval, row.ModelLabel);
        }).ToImmutableArray();
        ValidateVariables(decoded.Factory, variables, artifact.Variables);
        var body = DecodeBody(
            artifact.Body,
            artifact.Graph,
            decoded,
            variables,
            artifact.Clauses.Length,
            compilation);
        if (postconditionClaims.Length != 0 && body == null)
        {
            throw new InvalidDataException(
                "A successful postcondition callable requires a lowered body.");
        }
        return new CompilerCallablePreparation(
            decoded.Factory, entry, clauses, variables, WorkerClaimReason.None, body)
        {
            EffectClaims = DecodeEffects(artifact, claims),
            Compilation = compilation
        };
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
        foreach (var summary in artifact.Body?.SummaryCalls ?? [])
        {
            if (summary == null)
            {
                continue;
            }
            Add(summary.Result);
            foreach (var existential in summary.ExistentialVariables ?? [])
            {
                Add(existential);
            }
        }
        return [.. indices.OrderBy(static index => index)];
    }
    private static ImmutableArray<CompilerEffectClaimArtifact> DecodeEffects(
        CompilerCallableArtifact artifact,
        ImmutableArray<WorkerClaimManifestEntry> claims)
    {
        if (artifact.EffectClaims == null)
        {
            throw new InvalidDataException("Compiler effect-claim evidence is missing.");
        }

        var expected = claims.Where(static item => item.Kind == WorkerClaimKind.Effect).ToArray();
        if (artifact.EffectClaims.Length != expected.Length ||
            artifact.EffectClaims.Select(static item => item?.ClaimId)
                .Distinct(StringComparer.Ordinal).Count() != artifact.EffectClaims.Length)
        {
            throw new InvalidDataException("Compiler effect-claim evidence does not equal the manifest.");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            var evidence = artifact.EffectClaims[index];
            CompilerEffectClaimArtifactCodec.Validate(evidence);
            if (evidence.ClaimId != expected[index].ClaimId || evidence.ContractKind != expected[index].EffectContractKind)
            {
                throw new InvalidDataException("Compiler effect-claim evidence does not equal the manifest.");
            }
        }
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

        var current = new HashSet<IrVarId>(variables
            .Where(static item => item.Role is CompilerVariableRole.Receiver or CompilerVariableRole.Parameter)
            .Select(static item => item.Variable));
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
                    current.Contains(item.CurrentStateVariable.Value) &&
                    item.ModelLabel.StartsWith("pre:", StringComparison.Ordinal) &&
                    int.TryParse(item.ModelLabel.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) &&
                    ordinal >= 0,
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
        if (variables.Where(static item => item.Role == CompilerVariableRole.PreState)
            .Select(static item => item.CurrentStateVariable!.Value).Distinct().Count() !=
            variables.Count(static item => item.Role == CompilerVariableRole.PreState))
        {
            throw new InvalidDataException("Lowered pre-state variables are not injective.");
        }
    }

    private static bool IsPrimitiveInterval(CompilerIntegerInterval value)
    {
        return (value.Minimum, value.Maximum) is
        (sbyte.MinValue, sbyte.MaxValue) or (byte.MinValue, byte.MaxValue) or (short.MinValue, short.MaxValue) or
        (ushort.MinValue, ushort.MaxValue) or (int.MinValue, int.MaxValue) or (uint.MinValue, uint.MaxValue);
    }

    private static CompilerPreparedBody? DecodeBody(
        CompilerBodyArtifact? row,
        PortableIrGraph portable,
        DecodedPortableIrGraph graph,
        ImmutableArray<CompilerCanonicalVariable> variables,
        int clauseRootCount,
        CompilerCompilationSnapshot compilation)
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
            graph.Program == null || graph.Blocks.Count == 0 ||
            graph.Program.Blocks.Sum(static block => (long)block.Instructions.Length) > CompilerPreparedBody.MaximumInstructions ||
            graph.Program.Entry.Value != 0 || graph.Program.Entry != graph.Blocks[0])
        {
            throw new InvalidDataException("A lowered program body is invalid.");
        }

        ValidateExecutableBody(graph.Program, variables);

        var canonical = new HashSet<IrVarId>(variables.Select(static item => item.Variable));
        var parameters = variables
            .Where(static item => item.Role == CompilerVariableRole.Parameter)
            .ToDictionary(static item => item.Variable);
        var bindings = ImmutableDictionary.CreateBuilder<IrVarId, IrVarId>();
        var targets = new HashSet<IrVarId>();
        var sourceOrdinals = new HashSet<int>();
        var programVariables = CollectProgramVariables(graph.Program);
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
        var summaries = ImmutableDictionary.CreateBuilder<IrInstructionId, CompilerPreparedSummaryCall>();
        var calls = graph.Instructions.OfType<IrCallInstruction>().ToArray();
        var summaryVariables = new HashSet<IrVarId>();
        var portableCalls = portable.Blocks.SelectMany(static block => block.Instructions).Where(
            static instruction => instruction.Kind == IrInstructionKind.Call).ToArray();
        if (row.Calls.Length != calls.Length ||
            row.SpecCalls.Length + row.SummaryCalls.Length != calls.Length)
        {
            throw new InvalidDataException(
                "Lowered call evidence does not equal program calls.");
        }

        var identities = new Dictionary<IrInstructionId, string>();
        for (var index = 0; index < row.Calls.Length; index++)
        {
            var identity = row.Calls[index] ??
                throw new InvalidDataException("A lowered call identity is invalid.");
            var call = calls[index];
            if (At(graph.Instructions, identity.Instruction, "instruction").Id != call.Id ||
                string.IsNullOrWhiteSpace(identity.Identity) ||
                At(portable.Members, portableCalls[index].B, "member").DocumentationCommentId != identity.Identity)
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

        for (var index = 0; index < row.SummaryCalls.Length; index++)
        {
            var summary = row.SummaryCalls[index] ??
                throw new InvalidDataException(
                    "A lowered summary-call descriptor is invalid.");
            var instruction = At(
                graph.Instructions,
                summary.Instruction,
                "instruction");
            if (instruction is not IrCallInstruction call ||
                !identities.TryGetValue(call.Id, out var identity) ||
                summary.Identity != identity ||
                !ValidSummaryEvidence(
                    summary.Origin,
                    summary.EvidenceSha256,
                    summary.EvidenceIdentity,
                    compilation) ||
                !ValidDependencyEvidence(
                    summary.DependencyEvidence,
                    compilation) ||
                summary.NormalRelationRoot != clauseRootCount + index ||
                !WorkerProtocolJson.IsSha256(summary.EvidenceSha256) ||
                summary.ExistentialVariables == null ||
                specs.ContainsKey(call.Id) ||
                summaries.ContainsKey(call.Id))
            {
                throw new InvalidDataException(
                    "A lowered summary-call descriptor is invalid.");
            }

            var result = At(
                graph.Variables,
                summary.Result,
                "variable");
            var existentials = summary.ExistentialVariables
                .Select(index => At(
                    graph.Variables,
                    index,
                    "variable"))
                .ToImmutableArray();
            var relation = At(
                graph.Roots,
                summary.NormalRelationRoot,
                "root");
            var free = existentials.Insert(0, result);
            if (!call.Target.HasValue ||
                graph.Factory.GetVariableInfo(call.Target.Value).Type !=
                graph.Factory.GetVariableInfo(result).Type ||
                free.Distinct().Count() != free.Length ||
                free.Any(canonical.Contains) ||
                free.Any(programVariables.Contains) ||
                free.Any(summaryVariables.Contains) ||
                relation.Type != graph.Factory.BooleanType)
            {
                throw new InvalidDataException(
                    "A lowered source-call relation is invalid.");
            }
            summaryVariables.UnionWith(free);

            summaries.Add(call.Id, new CompilerPreparedSummaryCall(
                call.Id,
                identity,
                summary.Origin,
                result,
                existentials,
                relation,
                summary.EvidenceSha256,
                summary.EvidenceIdentity,
                [.. summary.DependencyEvidence.Select(static evidence =>
                    new CompilerPreparedSummaryEvidence(
                        evidence.Origin,
                        evidence.EvidenceSha256,
                        evidence.EvidenceIdentity))]));
        }

        if (specs.Count + summaries.Count != calls.Length)
        {
            throw new InvalidDataException(
                "Lowered call evidence is incomplete.");
        }

        return CompilerPreparedBody.ProgramBody(
            graph.Program,
            bindings.ToImmutable(),
            specs.ToImmutable(),
            summaries.ToImmutable());
    }

    private static void ValidateExecutableBody(
        IrProgram program,
        ImmutableArray<CompilerCanonicalVariable> variables)
    {
        const int maximumReachableBlocks = 64;
        var blocks = program.Blocks.ToDictionary(static block => block.Id);
        var colors = new Dictionary<IrBlockId, byte>();
        var reachable = 0;
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
                foreach (var successor in Successors(terminator))
                {
                    if (!Visit(successor))
                    {
                        return false;
                    }
                }
            }

            colors[blockId] = 2;
            return true;
        }

        static ImmutableArray<IrBlockId> Successors(IrInstruction terminator)
        {
            return terminator switch
            {
                IrBranchInstruction branch when
                    branch.WhenTrue == branch.WhenFalse => [branch.WhenTrue],
                IrBranchInstruction branch =>
                    [branch.WhenTrue, branch.WhenFalse],
                IrGotoInstruction go => [go.Target],
                IrReturnInstruction => [],
                _ => throw new InvalidDataException(
                    "A lowered block does not end in a terminator.")
            };
        }
    }

    internal static HashSet<IrVarId> CollectProgramVariables(
        IrProgram program)
    {
        var variables = new HashSet<IrVarId>();

        void AddTerm(IrTerm? term)
        {
            if (term != null)
            {
                variables.UnionWith(
                    IrTermAnalysis.CollectVariables(term));
            }
        }

        void AddLocation(IrLocation location)
        {
            switch (location)
            {
                case IrMemberLocation member:
                    AddTerm(member.Receiver);
                    foreach (var argument in member.Arguments)
                    {
                        AddTerm(argument);
                    }
                    break;
                case IrSequenceLocation sequence:
                    AddTerm(sequence.Sequence);
                    AddTerm(sequence.Index);
                    break;
            }
        }

        foreach (var instruction in program.Blocks.SelectMany(
                     static block => block.Instructions))
        {
            switch (instruction)
            {
                case IrAssignInstruction assign:
                    variables.Add(assign.Target);
                    AddTerm(assign.Value);
                    break;
                case IrLoadInstruction load:
                    variables.Add(load.Target);
                    AddLocation(load.Location);
                    break;
                case IrStoreInstruction store:
                    AddLocation(store.Location);
                    AddTerm(store.Value);
                    break;
                case IrCallInstruction call:
                    if (call.Target.HasValue)
                    {
                        variables.Add(call.Target.Value);
                    }
                    AddTerm(call.Receiver);
                    foreach (var argument in call.Arguments)
                    {
                        AddTerm(argument);
                    }
                    break;
                case IrAssumeInstruction assume:
                    AddTerm(assume.Condition);
                    break;
                case IrAssertInstruction assert:
                    AddTerm(assert.Condition);
                    break;
                case IrHavocInstruction havoc:
                    variables.UnionWith(havoc.Variables);
                    break;
                case IrBranchInstruction branch:
                    AddTerm(branch.Condition);
                    break;
                case IrReturnInstruction @return:
                    AddTerm(@return.Value);
                    break;
            }
        }

        return variables;
    }

    private static bool ValidSummaryEvidenceIdentity(
        CompilerSummaryOrigin origin,
        string? identity)
    {
        if (identity == null)
        {
            return false;
        }

        if (origin != CompilerSummaryOrigin.SpecificationPack)
        {
            return identity.Length == 0;
        }

        return identity.Length is > 0 and <= 128 &&
            identity.Contains('@') &&
            identity.All(static character =>
                character is >= 'a' and <= 'z' or
                >= '0' and <= '9' or '.' or '-' or '@');
    }

    private static bool ValidSummaryEvidence(
        CompilerSummaryOrigin origin,
        string? sha256,
        string? identity,
        CompilerCompilationSnapshot compilation)
    {
        return Enum.IsDefined(typeof(CompilerSummaryOrigin), origin) &&
            WorkerProtocolJson.IsSha256(sha256) &&
            ValidSummaryEvidenceIdentity(origin, identity) &&
            (origin != CompilerSummaryOrigin.ImplementationIl ||
                (compilation.References ?? []).SelectMany(
                        static reference => reference?.Modules ?? [])
                    .Any(module => module != null &&
                        module.Sha256 == sha256));
    }

    private static bool ValidDependencyEvidence(
        CompilerSummaryEvidenceArtifact[]? evidence,
        CompilerCompilationSnapshot compilation)
    {
        if (evidence == null)
        {
            return false;
        }

        string? previous = null;
        foreach (var item in evidence)
        {
            if (item == null ||
                !ValidSummaryEvidence(
                    item.Origin,
                    item.EvidenceSha256,
                    item.EvidenceIdentity,
                    compilation))
            {
                return false;
            }

            var key = ((int)item.Origin).ToString(
                    CultureInfo.InvariantCulture) + "|" +
                item.EvidenceIdentity + "|" + item.EvidenceSha256;
            if (previous != null &&
                StringComparer.Ordinal.Compare(previous, key) >= 0)
            {
                return false;
            }

            previous = key;
        }

        return true;
    }
    private static WorkerClaimEvidence ManifestEvidence(CompilerContractEvidence value)
    {
        var index = (int)value;
        return index >= 0 && index < ManifestEvidenceMap.Length
            ? ManifestEvidenceMap[index]
            : WorkerClaimEvidence.Unspecified;
    }

    private static string PredicateSha256(IrFactory factory, CompilerPreparedClause clause)
    {
        var graph = PortableIrGraphCodec.Encode(factory, null, [clause.Condition]).Graph;
        using var hash = new CanonicalHashWriter();
        return hash.Add("SharpProofClausePredicate/v1", clause.Kind, clause.Evidence, clause.ClaimId ?? clause.AssumptionId)
            .Add(JsonSerializer.SerializeToUtf8Bytes(graph, WorkerProtocolJson.Options)).Finish();
    }
    private static T At<T>(IReadOnlyList<T> items, int index, string kind)
    {
        return index >= 0 && index < items.Count ? items[index] :
            throw new InvalidDataException("A lowered " + kind + " index is out of range.");
    }
}
