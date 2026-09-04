using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;
internal static class CompilerLoweredArtifact
{
    private static readonly WorkerClaimEvidence[] ManifestEvidenceMap =
    [
        WorkerClaimEvidence.DirectClause,
        WorkerClaimEvidence.ReturnAttribute,
        WorkerClaimEvidence.CompanionClause
    ];

    private sealed class SummaryEvidenceIndex
    {
        private readonly CompilerCompilationSnapshot _compilation;
        private readonly Dictionary<(
            CompilerSummaryOrigin Origin,
            string CallIdentity,
            string EvidenceSha256,
            string EvidenceIdentity),
            (CompilerSummaryEvidenceSnapshot Row, int Count)> _rows = new();

        internal SummaryEvidenceIndex(CompilerCompilationSnapshot compilation)
        {
            _compilation = compilation;
            foreach (var row in compilation.SummaryEvidence ?? [])
            {
                if (row == null)
                {
                    continue;
                }

                var key = (
                    row.Origin,
                    row.CallIdentity,
                    row.EvidenceSha256,
                    row.EvidenceIdentity);
                if (_rows.TryGetValue(key, out var existing))
                {
                    _rows[key] = (existing.Row, existing.Count + 1);
                }
                else
                {
                    _rows.Add(key, (row, 1));
                }
            }
        }

        internal bool IsValid(
            CompilerSummaryOrigin origin,
            string? callIdentity,
            string? sha256,
            string? identity)
        {
            if (!Enum.IsDefined(typeof(CompilerSummaryOrigin), origin) ||
                !WorkerProtocolJson.IsSha256(sha256) ||
                !ValidSummaryCallIdentity(callIdentity) ||
                !ValidSummaryEvidenceIdentity(origin, identity, _compilation))
            {
                return false;
            }

            var key = (origin, callIdentity!, sha256!, identity!);
            return _rows.TryGetValue(key, out var match) &&
                match.Count == 1 &&
                CompilationFingerprint.ValidSummaryEvidenceRow(
                    match.Row,
                    _compilation,
                    authorityMode: true);
        }

        internal bool AreValidDependencies(
            CompilerSummaryEvidenceArtifact[]? evidence)
        {
            if (evidence == null)
            {
                return false;
            }

            string? previous = null;
            foreach (var item in evidence)
            {
                if (item == null ||
                    !IsValid(
                        item.Origin,
                        item.CallIdentity,
                        item.EvidenceSha256,
                        item.EvidenceIdentity))
                {
                    return false;
                }

                var key = ((int)item.Origin).ToString(
                        CultureInfo.InvariantCulture) + "|" +
                    item.CallIdentity + "|" +
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
    }

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
        foreach (var call in orderedSummaryCalls)
        {
            AddVariable(call.Result);
            foreach (var existential in call.ExistentialVariables)
            {
                AddVariable(existential);
            }
        }
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
        var allCalls = body.SpecCalls.Values
            .Select(static call => (
                call.Instruction,
                call.CallIdentity))
            .Concat(body.SummaryCalls.Values.Select(static call => (
                call.Instruction,
                call.CallIdentity)))
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
        if (orderedSummaryCalls.Length == 0)
        {
            artifact.Body.SummaryCalls = [];
        }
        else
        {
            var programInstructions = body.Program!.Blocks
                .SelectMany(static block => block.Instructions)
                .ToDictionary(static instruction => instruction.Id);
            artifact.Body.SummaryCalls = [.. orderedSummaryCalls.Select((item, index) =>
                BuildSummaryCallArtifact(item, index, programInstructions))];
        }
        return artifact;

        CompilerSummaryCallArtifact BuildSummaryCallArtifact(
            CompilerPreparedSummaryCall item,
            int index,
            Dictionary<IrInstructionId, IrInstruction> programInstructions)
        {
            if (!programInstructions.TryGetValue(item.Instruction, out var instruction) ||
                instruction is not IrCallInstruction call)
            {
                throw new InvalidDataException(
                    "A prepared summary call does not reference a call instruction.");
            }

            return new CompilerSummaryCallArtifact
            {
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
                        CallIdentity = evidence.CallIdentity,
                        EvidenceSha256 = evidence.EvidenceSha256,
                        EvidenceIdentity = evidence.EvidenceIdentity
                    })],
                InstantiationSha256 = SummaryInstantiationSha256(
                    preparation.Factory,
                    call,
                    item.Result,
                    item.ExistentialVariables,
                    item.NormalRelation,
                    item.DependencyEvidence)
            };
        }
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
                    compilation,
                    cancellationToken),
                Compilation = compilation
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
            cancellationToken.ThrowIfCancellationRequested();
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
            if (row.PredicateSha256 != PredicateSha256(decoded.Factory, clause))
            {
                throw new InvalidDataException("A lowered contract predicate does not equal its compiler inventory.");
            }

            return clause;
        }).ToImmutableArray();
        cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
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
        cancellationToken.ThrowIfCancellationRequested();
        ValidateVariables(decoded.Factory, variables, artifact.Variables);
        cancellationToken.ThrowIfCancellationRequested();
        var body = DecodeBody(
            artifact.Body,
            artifact.Graph,
            decoded,
            variables,
            artifact.Clauses.Length,
            compilation);
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
                compilation,
                cancellationToken),
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
        ImmutableArray<WorkerClaimManifestEntry> claims,
        CompilerCompilationSnapshot compilation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (artifact.EffectClaims == null)
        {
            throw new InvalidDataException("Compiler effect-claim evidence is missing.");
        }

        if (artifact.EffectAuthorities == null)
        {
            throw new InvalidDataException("Compiler effect authority is missing.");
        }

        var expected = claims.Where(static item => item.Kind == WorkerClaimKind.Effect).ToArray();
        if (artifact.EffectClaims.Length != expected.Length ||
            artifact.EffectAuthorities.Length != expected.Length ||
            artifact.EffectClaims.Select(static item => item?.ClaimId)
                .Distinct(StringComparer.Ordinal).Count() != artifact.EffectClaims.Length ||
            artifact.EffectAuthorities.Select(static item => item?.ClaimId)
                .Distinct(StringComparer.Ordinal).Count() != artifact.EffectAuthorities.Length)
        {
            throw new InvalidDataException("Compiler effect-claim evidence does not equal the manifest.");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = artifact.EffectClaims[index];
            var authority = artifact.EffectAuthorities[index];
            CompilerEffectClaimArtifactCodec.Validate(evidence, compilation);
            if (evidence.ClaimId != expected[index].ClaimId || evidence.ContractKind != expected[index].EffectContractKind)
            {
                throw new InvalidDataException("Compiler effect-claim evidence does not equal the manifest.");
            }

            var authorityMatches = CompilerEffectAuthority.Matches(
                evidence,
                authority,
                expected[index],
                compilation);
            if (!authorityMatches)
            {
                throw new InvalidDataException(
                    "Compiler effect evidence does not equal its compiler authority.");
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
        (ushort.MinValue, ushort.MaxValue) or (int.MinValue, int.MaxValue) or (uint.MinValue, uint.MaxValue) or
        (long.MinValue, long.MaxValue);
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

        var programVariables = ValidateExecutableBody(graph.Program, variables);
        var summaryEvidence = row.SummaryCalls.Length == 0
            ? null
            : new SummaryEvidenceIndex(compilation);

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
        var summaries = ImmutableDictionary.CreateBuilder<IrInstructionId, CompilerPreparedSummaryCall>();
        var callCount = graph.Instructions.Count(static instruction => instruction is IrCallInstruction);
        var summaryVariables = new HashSet<IrVarId>();
        if (row.Calls.Length != callCount ||
            row.SpecCalls.Length + row.SummaryCalls.Length != callCount)
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
                !summaryEvidence!.IsValid(
                    summary.Origin,
                    summary.Identity,
                    summary.EvidenceSha256,
                    summary.EvidenceIdentity) ||
                !summaryEvidence.AreValidDependencies(summary.DependencyEvidence) ||
                !WorkerProtocolJson.IsSha256(summary.InstantiationSha256) ||
                summary.NormalRelationRoot != clauseRootCount + index ||
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
            var freeIdentifiers = new HashSet<IrVarId>();
            var hasDuplicateFreeVariable = false;
            var hasCanonicalFreeVariable = false;
            var hasProgramFreeVariable = false;
            var hasSummaryFreeVariable = false;
            foreach (var variable in free)
            {
                hasDuplicateFreeVariable |= !freeIdentifiers.Add(variable);
                hasCanonicalFreeVariable |= canonical.Contains(variable);
                hasProgramFreeVariable |= programVariables.Contains(variable);
                hasSummaryFreeVariable |= summaryVariables.Contains(variable);
            }
            if (!call.Target.HasValue ||
                graph.Factory.GetVariableInfo(call.Target.Value).Type !=
                    graph.Factory.GetVariableInfo(result).Type ||
                hasDuplicateFreeVariable ||
                hasCanonicalFreeVariable ||
                hasProgramFreeVariable ||
                hasSummaryFreeVariable ||
                relation.Type != graph.Factory.BooleanType ||
                !HasValidSummaryFreeVariableRoles(
                    call,
                    result,
                    existentials,
                    relation) ||
            summary.InstantiationSha256 != SummaryInstantiationSha256(
                    graph.Factory,
                    call,
                    result,
                    existentials,
                    relation,
                    summary.DependencyEvidence))
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
                        evidence.CallIdentity,
                        evidence.EvidenceSha256,
                        evidence.EvidenceIdentity))])
            {
                InstantiationSha256 = summary.InstantiationSha256
            });
        }

        if (specs.Count + summaries.Count != callCount)
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

    private static bool HasValidSummaryFreeVariableRoles(
        IrCallInstruction call,
        IrVarId result,
        IReadOnlyList<IrVarId> existentials,
        IrTerm relation)
    {
        var relationVariables = IrTermAnalysis.CollectVariables(relation);
        var freeVariables = existentials.ToImmutableHashSet().Add(result);
        if (!freeVariables.IsSubsetOf(relationVariables))
        {
            return false;
        }

        var inputVariables = ImmutableHashSet.CreateBuilder<IrVarId>();
        if (call.Receiver != null)
        {
            inputVariables.UnionWith(
                IrTermAnalysis.CollectVariables(call.Receiver));
        }

        foreach (var argument in call.Arguments)
        {
            inputVariables.UnionWith(IrTermAnalysis.CollectVariables(argument));
        }

        return relationVariables
            .Where(variable => !freeVariables.Contains(variable))
            .All(inputVariables.Contains);
    }

    private static string SummaryInstantiationSha256(
        IrFactory factory,
        IrCallInstruction call,
        IrVarId result,
        IReadOnlyList<IrVarId> existentials,
        IrTerm relation,
        object dependencyEvidence)
    {
        var roots = new List<IrTerm>(
            (call.Receiver == null ? 0 : 1) +
            call.Arguments.Length +
            existentials.Count +
            2);
        if (call.Receiver != null)
        {
            roots.Add(call.Receiver);
        }

        roots.AddRange(call.Arguments);
        roots.Add(factory.Variable(result));
        roots.AddRange(existentials.Select(factory.Variable));
        roots.Add(relation);
        var graph = PortableIrGraphCodec.Encode(factory, null, roots).Graph;
        using var hash = new CanonicalHashWriter();
        return hash
            .Add("SharpProofCompilerSummaryCallInstantiation/v1")
            .Add(call.Receiver != null)
            .Add(call.Arguments.Length)
            .Add(existentials.Count)
            .Add(JsonSerializer.SerializeToUtf8Bytes(
                graph,
                WorkerProtocolJson.SharedOptions))
            .Add(JsonSerializer.SerializeToUtf8Bytes(
                dependencyEvidence,
                WorkerProtocolJson.SharedOptions))
            .Finish();
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

    private static bool ValidSummaryEvidenceIdentity(
        CompilerSummaryOrigin origin,
        string? identity,
        CompilerCompilationSnapshot compilation)
    {
        if (identity == null)
        {
            return false;
        }

        if (origin != CompilerSummaryOrigin.SpecificationPack)
        {
            return identity.Length == 0;
        }

        return CompilerSpecificationPackAuthorityValidation.IsValidPackIdentity(
            identity,
            compilation.SpecificationPackIds);
    }

    private static bool ValidSummaryCallIdentity(string? identity)
    {
        return identity is { Length: > 0 and <= 512 } &&
            identity.All(static character => !char.IsControl(character));
    }

    internal static WorkerClaimEvidence ManifestEvidence(
        CompilerContractEvidence value)
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
            .Add(JsonSerializer.SerializeToUtf8Bytes(graph, WorkerProtocolJson.SharedOptions)).Finish();
    }
    private static T At<T>(IReadOnlyList<T> items, int index, string kind)
    {
        return index >= 0 && index < items.Count ? items[index] :
            throw new InvalidDataException("A lowered " + kind + " index is out of range.");
    }
}
