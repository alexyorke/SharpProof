using System.Text.Json;
namespace SharpProof.CompilerArtifact;
#pragma warning disable IDE0055 // Compact closed-artifact codec preserves the size ratchet.
internal sealed class CompilerCallableArtifact {
    public string CallableId { get; set; } = string.Empty; public WorkerClaimReason FailureReason { get; set; }
    public PortableIrGraph? Graph { get; set; } public CompilerClauseArtifact[] Clauses { get; set; } = [];
    public CompilerVariableArtifact[] Variables { get; set; } = []; public CompilerBodyArtifact? Body { get; set; }
}
internal sealed class CompilerClauseArtifact {
    public CompilerContractKind Kind { get; set; } public CompilerContractEvidence Evidence { get; set; }
    public int Root { get; set; } = -1; public string? ClaimId { get; set; } public string? AssumptionId { get; set; }
    public string PredicateSha256 { get; set; } = string.Empty;
}
internal sealed class CompilerVariableArtifact {
    public CompilerVariableRole Role { get; set; } public int Ordinal { get; set; }
    public int Variable { get; set; } = -1; public int CurrentStateVariable { get; set; } = -1;
    public long? Minimum { get; set; } public long? Maximum { get; set; }
    public string ModelLabel { get; set; } = string.Empty;
}
internal sealed class CompilerBodyArtifact {
    public CompilerPreparedBodyKind Kind { get; set; }
    public CompilerVariableMappingArtifact[] ParameterBindings { get; set; } = []; public CompilerCallIdentityArtifact[] Calls { get; set; } = [];
    public CompilerSpecCallArtifact[] SpecCalls { get; set; } = [];
}
internal sealed class CompilerVariableMappingArtifact {
    public int Source { get; set; } = -1; public int Target { get; set; } = -1;
}
internal sealed class CompilerCallIdentityArtifact { public int Instruction { get; set; } = -1; public string Identity { get; set; } = string.Empty; }
internal sealed class CompilerSpecCallArtifact {
    public int Instruction { get; set; } = -1; public string WitnessIdentifier { get; set; } = string.Empty;
    public bool ConsumesMemoryHavoc { get; set; }
}
internal static class CompilerLoweredArtifact {
    internal static CompilerCallableArtifact Encode(CompilerCallablePreparation preparation) {
        if (preparation == null) throw new ArgumentNullException(nameof(preparation));
        if (!preparation.IsSuccess)
            return new CompilerCallableArtifact {
                CallableId = preparation.Entry.CallableId, FailureReason = preparation.FailureReason
            };
        var body = preparation.Body;
        var roots = preparation.Clauses.Select(static clause => clause.Condition).ToArray();
        var variables = preparation.Variables
            .SelectMany(static variable => variable.CurrentStateVariable.HasValue
                ? new[] { variable.Variable, variable.CurrentStateVariable.Value }
                : [variable.Variable])
            .Concat(body?.ParameterBindings.SelectMany(static item => new[] { item.Key, item.Value }) ?? [])
            .Distinct().ToArray();
        var encoded = PortableIrGraphCodec.Encode(preparation.Factory, body?.Program, roots, variables);
        var artifact = new CompilerCallableArtifact {
            CallableId = preparation.Entry.CallableId,
            FailureReason = WorkerClaimReason.None,
            Graph = encoded.Graph,
            Clauses = [.. preparation.Clauses.Select((clause, index) =>
                new CompilerClauseArtifact {
                    Kind = clause.Kind, Evidence = clause.Evidence, Root = index,
                    ClaimId = clause.ClaimId, AssumptionId = clause.AssumptionId,
                    PredicateSha256 = PredicateSha256(preparation.Factory, clause)
                })],
            Variables = [.. preparation.Variables.Select(variable => {
                var interval = variable.SourceIntegerInterval;
                return new CompilerVariableArtifact {
                    Role = variable.Role, Ordinal = variable.Ordinal,
                    Variable = encoded.VariableIndices[variable.Variable],
                    CurrentStateVariable = variable.CurrentStateVariable.HasValue ?
                        encoded.VariableIndices[variable.CurrentStateVariable.Value] : -1,
                    Minimum = interval?.Minimum, Maximum = interval?.Maximum,
                    ModelLabel = variable.ModelLabel
                };
            })]
        };
        if (body == null) return artifact;
        artifact.Body = new CompilerBodyArtifact { Kind = body.Kind };
        if (body.Kind == CompilerPreparedBodyKind.Trivial) return artifact;
        artifact.Body.ParameterBindings = [.. body.ParameterBindings
            .OrderBy(static item => item.Key.Value)
            .Select(item => new CompilerVariableMappingArtifact {
                Source = encoded.VariableIndices[item.Key],
                Target = encoded.VariableIndices[item.Value]
            })];
        var instructions = encoded.Graph.Blocks.SelectMany(static block => block.Instructions).ToArray();
        foreach (var call in body.SpecCalls.Values) {
            var instruction = instructions[encoded.InstructionIndices[call.Instruction]];
            var member = encoded.Graph.Members[instruction.B];
            if (member.DocumentationCommentId is { } existing && existing != call.CallIdentity)
                throw new InvalidDataException("A lowered member has conflicting semantic identities.");
            member.DocumentationCommentId = call.CallIdentity;
        }
        artifact.Body.Calls = [.. body.SpecCalls.Values.OrderBy(static item => item.Instruction.Value)
            .Select(item => new CompilerCallIdentityArtifact {
                Instruction = encoded.InstructionIndices[item.Instruction], Identity = item.CallIdentity
            })];
        artifact.Body.SpecCalls = [.. body.SpecCalls.Values
            .OrderBy(static item => item.Instruction.Value)
            .Select(item => new CompilerSpecCallArtifact {
                Instruction = encoded.InstructionIndices[item.Instruction], WitnessIdentifier = item.WitnessIdentifier,
                ConsumesMemoryHavoc = item.ConsumesMemoryHavoc
            })];
        return artifact;
    }
    internal static ImmutableArray<CompilerCallablePreparation> Decode(
        CompilerCallableArtifact[] artifacts, WorkerClaimManifest manifest) {
        if (artifacts == null) throw new InvalidDataException("The lowered callable payload is missing.");
        var callables = manifest.Callables.ToDictionary(
            static item => item.CallableId, StringComparer.Ordinal);
        var claims = manifest.Claims.GroupBy(static item => item.CallableId)
            .ToDictionary(static group => group.Key,
                static group => group.OrderBy(static item => item.Ordinal).ToImmutableArray(),
                StringComparer.Ordinal);
        if (artifacts.Length != callables.Count ||
            artifacts.Select(static item => item?.CallableId)
                .Distinct(StringComparer.Ordinal).Count() != artifacts.Length ||
            artifacts.Any(item => item == null || !callables.ContainsKey(item.CallableId)))
            throw new InvalidDataException("The lowered callable payload does not equal the manifest.");
        var result = ImmutableArray.CreateBuilder<CompilerCallablePreparation>(artifacts.Length);
        foreach (var artifact in artifacts.OrderBy(static item => item.CallableId, StringComparer.Ordinal)) {
            var entry = callables[artifact.CallableId];
            var targetClaims = claims.TryGetValue(
                artifact.CallableId, out var rows) ? rows : [];
            if (!entry.ClaimIds.SequenceEqual(
                    targetClaims.Select(static item => item.ClaimId),
                    StringComparer.Ordinal))
                throw new InvalidDataException("A lowered callable claim list does not equal the manifest.");
            result.Add(Decode(artifact, entry, targetClaims));
        }
        return result.MoveToImmutable();
    }
    private static CompilerCallablePreparation Decode(CompilerCallableArtifact artifact,
        WorkerCallableManifestEntry entry, ImmutableArray<WorkerClaimManifestEntry> claims) {
        if (!Enum.IsDefined(typeof(WorkerClaimReason), artifact.FailureReason) ||
            artifact.FailureReason == WorkerClaimReason.Unspecified)
            throw new InvalidDataException("A lowered callable reason is invalid.");
        if (artifact.FailureReason != WorkerClaimReason.None) {
            if (artifact.Graph != null || artifact.Body != null ||
                artifact.Clauses is not { Length: 0 } ||
                artifact.Variables is not { Length: 0 })
                throw new InvalidDataException("A failed lowered callable cannot contain executable evidence.");
            return new CompilerCallablePreparation(
                new IrFactory(), entry, [], [], artifact.FailureReason, null);
        }
        if (artifact.Graph == null || artifact.Clauses == null ||
            artifact.Variables == null)
            throw new InvalidDataException("A successful lowered callable is incomplete.");
        var decoded = PortableIrGraphCodec.Decode(artifact.Graph);
        if (decoded.Roots.Count != artifact.Clauses.Length)
            throw new InvalidDataException("A lowered callable contains non-clause roots.");
        IrTerm Root(int index) => At(decoded.Roots, index, "root");
        IrVarId Variable(int index) => At(decoded.Variables, index, "variable");
        var clauses = artifact.Clauses.Select((row, index) => {
            if (row == null ||
                !Enum.IsDefined(typeof(CompilerContractKind), row.Kind) || !Enum.IsDefined(typeof(CompilerContractEvidence), row.Evidence) || row.Root != index ||
                row.Kind == CompilerContractKind.Ensures != !string.IsNullOrWhiteSpace(row.ClaimId) ||
                row.Kind != CompilerContractKind.Ensures != !string.IsNullOrWhiteSpace(row.AssumptionId) ||
                row.Kind != CompilerContractKind.Ensures && row.ClaimId != null ||
                row.Kind == CompilerContractKind.Ensures && row.AssumptionId != null ||
                !WorkerProtocolJson.IsSha256(row.PredicateSha256))
                throw new InvalidDataException("A lowered contract clause is invalid.");
            var clause = new CompilerPreparedClause(
                row.Kind, Root(row.Root), row.Evidence, row.ClaimId, row.AssumptionId);
            if (row.PredicateSha256 != PredicateSha256(decoded.Factory, clause))
                throw new InvalidDataException("A lowered contract predicate does not equal its compiler inventory.");
            return clause;
        }).ToImmutableArray();
        var loweredClaims = clauses.Where(static item => item.Kind == CompilerContractKind.Ensures).ToArray();
        if (loweredClaims.Length != claims.Length ||
            !loweredClaims.Select(static item => item.ClaimId!).SequenceEqual(claims.Select(static item => item.ClaimId), StringComparer.Ordinal) ||
            !loweredClaims.Select(static item => ManifestEvidence(item.Evidence)).SequenceEqual(claims.Select(static item => item.Evidence)))
            throw new InvalidDataException("Lowered claims do not equal the manifest.");
        var declaredClauseAssumptions = entry.Assumptions.Where(static item =>
                item.Kind is WorkerAssumptionKind.Precondition or WorkerAssumptionKind.UserAssume)
            .Select(static item => (item.Id, item.Kind)).OrderBy(static item => item.Id, StringComparer.Ordinal);
        var loweredClauseAssumptions = clauses.Where(static item => item.Kind != CompilerContractKind.Ensures)
            .Select(static item => (item.AssumptionId!, item.Kind == CompilerContractKind.Requires
                ? WorkerAssumptionKind.Precondition : WorkerAssumptionKind.UserAssume))
            .OrderBy(static item => item.Item1, StringComparer.Ordinal);
        if (!declaredClauseAssumptions.SequenceEqual(loweredClauseAssumptions))
            throw new InvalidDataException("Lowered assumptions do not equal the manifest.");
        var variables = artifact.Variables.Select(row => {
            if (row == null ||
                !Enum.IsDefined(typeof(CompilerVariableRole), row.Role) ||
                row.Minimum.HasValue != row.Maximum.HasValue ||
                row.Minimum > row.Maximum)
                throw new InvalidDataException("A lowered canonical variable is invalid.");
            var variable = Variable(row.Variable);
            IrVarId? current = row.CurrentStateVariable < 0 ? null : Variable(row.CurrentStateVariable);
            CompilerIntegerInterval? interval = row.Minimum.HasValue ?
                new CompilerIntegerInterval(row.Minimum.Value, row.Maximum!.Value) : null;
            return new CompilerCanonicalVariable(
                row.Role, row.Ordinal, variable, current, interval, row.ModelLabel);
        }).ToImmutableArray();
        ValidateVariables(decoded.Factory, variables);
        var body = DecodeBody(artifact.Body, artifact.Graph, decoded, variables);
        return new CompilerCallablePreparation(
            decoded.Factory, entry, clauses, variables, WorkerClaimReason.None, body);
    }
    private static void ValidateVariables(IrFactory factory, ImmutableArray<CompilerCanonicalVariable> variables) {
        var canonical = new HashSet<IrVarId>(variables.Select(static item => item.Variable));
        var parameters = variables.Where(static item => item.Role == CompilerVariableRole.Parameter).OrderBy(static item => item.Ordinal).ToArray();
        if (canonical.Count != variables.Length ||
            variables.Select(static item => item.ModelLabel).Distinct(StringComparer.Ordinal).Count() != variables.Length ||
            variables.Count(static item => item.Role == CompilerVariableRole.Receiver) > 1 ||
            variables.Count(static item => item.Role == CompilerVariableRole.Result) > 1 ||
            !parameters.Select(static item => item.Ordinal).SequenceEqual(Enumerable.Range(0, parameters.Length)))
            throw new InvalidDataException("Lowered canonical variable roles are invalid.");
        var current = new HashSet<IrVarId>(variables.Where(static item =>
            item.Role is CompilerVariableRole.Receiver or CompilerVariableRole.Parameter).Select(static item => item.Variable));
        foreach (var item in variables) {
            var info = factory.GetVariableInfo(item.Variable); var label = factory.GetString(info.Name);
            var shape = item.Role switch {
                CompilerVariableRole.Receiver => item.Ordinal == -1 && item.CurrentStateVariable == null && item.ModelLabel == "receiver",
                CompilerVariableRole.Parameter => item.Ordinal >= 0 && item.CurrentStateVariable == null &&
                    item.ModelLabel == "parameter:" + item.Ordinal.ToString(CultureInfo.InvariantCulture),
                CompilerVariableRole.Result => item.Ordinal == -1 && item.CurrentStateVariable == null && item.ModelLabel == "result",
                CompilerVariableRole.PreState => item.Ordinal == -1 && item.CurrentStateVariable.HasValue &&
                    current.Contains(item.CurrentStateVariable.Value) && item.ModelLabel.StartsWith("pre:", StringComparison.Ordinal) &&
                    int.TryParse(item.ModelLabel.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) && ordinal >= 0,
                _ => false
            };
            if (!shape || item.ModelLabel != label ||
                item.CurrentStateVariable.HasValue && factory.GetVariableInfo(item.CurrentStateVariable.Value).Type != info.Type ||
                item.SourceIntegerInterval is { } interval &&
                    (item.Role == CompilerVariableRole.PreState || info.Type != factory.IntegerType || !IsPrimitiveInterval(interval)))
                throw new InvalidDataException("A lowered canonical variable is invalid.");
        }
        if (variables.Where(static item => item.Role == CompilerVariableRole.PreState).Select(
                static item => item.CurrentStateVariable!.Value).Distinct().Count() != variables.Count(
                static item => item.Role == CompilerVariableRole.PreState))
            throw new InvalidDataException("Lowered pre-state variables are not injective.");
    }

    private static bool IsPrimitiveInterval(CompilerIntegerInterval value) => (value.Minimum, value.Maximum) is
        (sbyte.MinValue, sbyte.MaxValue) or (byte.MinValue, byte.MaxValue) or (short.MinValue, short.MaxValue) or
        (ushort.MinValue, ushort.MaxValue) or (int.MinValue, int.MaxValue) or (uint.MinValue, uint.MaxValue);
    private static CompilerPreparedBody? DecodeBody(CompilerBodyArtifact? row, PortableIrGraph portable,
        DecodedPortableIrGraph graph, ImmutableArray<CompilerCanonicalVariable> variables) {
        if (row == null) {
            if (graph.Program != null) throw new InvalidDataException("A bodyless callable cannot contain a program.");
            return null;
        }
        if (row.ParameterBindings == null || row.Calls == null || row.SpecCalls == null)
            throw new InvalidDataException("A lowered body is incomplete.");
        if (row.Kind == CompilerPreparedBodyKind.Trivial) {
            if (graph.Program != null || row.ParameterBindings.Length != 0 ||
                row.Calls.Length != 0 || row.SpecCalls.Length != 0)
                throw new InvalidDataException("A trivial lowered body is invalid.");
            return CompilerPreparedBody.Trivial();
        }
        if (row.Kind != CompilerPreparedBodyKind.Program ||
            graph.Program == null || graph.Blocks.Count == 0 ||
            graph.Program.Blocks.Sum(static block => (long)block.Instructions.Length) > CompilerPreparedBody.MaximumInstructions ||
            graph.Program.Entry.Value != 0 || graph.Program.Entry != graph.Blocks[0])
            throw new InvalidDataException("A lowered program body is invalid.");
        var canonical = new HashSet<IrVarId>(variables.Select(static item => item.Variable));
        var parameters = new HashSet<IrVarId>(variables.Where(static item =>
            item.Role == CompilerVariableRole.Parameter).Select(static item => item.Variable));
        var bindings = ImmutableDictionary.CreateBuilder<IrVarId, IrVarId>();
        var targets = new HashSet<IrVarId>();
        foreach (var item in row.ParameterBindings) {
            if (item == null) throw new InvalidDataException("A lowered parameter binding is invalid.");
            var source = At(graph.Variables, item.Source, "variable"); var target = At(graph.Variables, item.Target, "variable");
            if (canonical.Contains(source) || !parameters.Contains(target) || source == target ||
                graph.Factory.GetVariableInfo(source).Type != graph.Factory.GetVariableInfo(target).Type ||
                bindings.ContainsKey(source) || !targets.Add(target))
                throw new InvalidDataException("A lowered parameter binding is invalid.");
            bindings.Add(source, target);
        }
        var specs = ImmutableDictionary.CreateBuilder<IrInstructionId, CompilerPreparedSpecCall>();
        var calls = graph.Instructions.OfType<IrCallInstruction>().ToArray();
        var portableCalls = portable.Blocks.SelectMany(static block => block.Instructions).Where(
            static instruction => instruction.Kind == IrInstructionKind.Call).ToArray();
        if (row.Calls.Length != calls.Length || row.SpecCalls.Length != calls.Length)
            throw new InvalidDataException("Lowered spec calls do not equal program calls.");
        for (var index = 0; index < row.Calls.Length; index++) {
            var identity = row.Calls[index] ?? throw new InvalidDataException("A lowered call identity is invalid.");
            var spec = row.SpecCalls[index] ?? throw new InvalidDataException("A lowered spec-call descriptor is invalid."); var call = calls[index];
            if (At(graph.Instructions, identity.Instruction, "instruction").Id != call.Id ||
                At(graph.Instructions, spec.Instruction, "instruction").Id != call.Id ||
                string.IsNullOrWhiteSpace(identity.Identity) || string.IsNullOrWhiteSpace(spec.WitnessIdentifier) ||
                At(portable.Members, portableCalls[index].B, "member").DocumentationCommentId != identity.Identity)
                throw new InvalidDataException("A lowered call descriptor is invalid.");
            specs.Add(call.Id, new CompilerPreparedSpecCall(call.Id, identity.Identity, spec.WitnessIdentifier, spec.ConsumesMemoryHavoc));
        }
        return CompilerPreparedBody.ProgramBody(graph.Program, bindings.ToImmutable(), specs.ToImmutable());
    }
    private static WorkerClaimEvidence ManifestEvidence(CompilerContractEvidence value) => value switch {
        CompilerContractEvidence.CompilerBoundInvocation => WorkerClaimEvidence.DirectClause,
        CompilerContractEvidence.Companion => WorkerClaimEvidence.CompanionClause,
        CompilerContractEvidence.ClosedAttribute => WorkerClaimEvidence.ReturnAttribute,
        _ => WorkerClaimEvidence.Unspecified
    };
    private static string PredicateSha256(IrFactory factory, CompilerPreparedClause clause) {
        var graph = PortableIrGraphCodec.Encode(factory, null, [clause.Condition]).Graph;
        using var hash = new CanonicalHashWriter();
        return hash.Add("SharpProofClausePredicate/v1", clause.Kind, clause.Evidence, clause.ClaimId ?? clause.AssumptionId)
            .Add(JsonSerializer.SerializeToUtf8Bytes(graph, WorkerProtocolJson.Options)).Finish();
    }
    private static T At<T>(IReadOnlyList<T> items, int index, string kind) =>
        index >= 0 && index < items.Count ? items[index] :
            throw new InvalidDataException("A lowered " + kind + " index is out of range.");
}
