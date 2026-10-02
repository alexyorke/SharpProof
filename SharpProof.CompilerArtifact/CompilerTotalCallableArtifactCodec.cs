using System.Collections.Immutable;
using System.Globalization;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

// Optional candidate evidence. This graph is independent of the authoritative
// legacy graph and is decoded once with its owning manifest callable.
internal static class CompilerTotalCallableArtifactCodec
{
    internal static CompilerTotalCallableArtifact? Encode(CompilerTotalCallablePreparation? preparation)
    {
        if (preparation == null)
        { return null; }
        var artifact = EncodeCore(preparation.Program.Factory, preparation.Program, preparation.Parameters, preparation.Result, preparation.Clauses, preparation.IsBodyAbstraction);
        artifact.EffectsCompleteAtEntry = preparation.EffectsCompleteAtEntry;
        artifact.ValidEffectClaimIds = [.. preparation.ValidEffectClaimIds];
        artifact.ExceptionConstraints = [.. preparation.ExceptionConstraints.Select(constraint => new CompilerTotalExceptionConstraintArtifact
        { ClaimId = constraint.ClaimId, AllowedKinds = [.. constraint.AllowedKinds] })];
        return artifact;
    }

    internal static CompilerTotalCallableArtifact? EncodeEntry(CompilerTotalEntryPreparation? preparation)
    {
        return preparation == null ? null : EncodeCore(preparation.Factory, null, preparation.Parameters, null, preparation.Clauses);
    }

    private static CompilerTotalCallableArtifact EncodeCore(IrFactory factory, IrProgram? program,
        ImmutableArray<CompilerTotalParameter> parameters, IrVarId? result, ImmutableArray<CompilerTotalClause> clauses, bool isBodyAbstraction = false)
    {
        var variables = parameters.SelectMany(parameter => new[] { parameter.Entry, parameter.Current, parameter.Old })
            .Concat(result is { } resultId ? [resultId] : Array.Empty<IrVarId>()).ToArray();
        var encoded = PortableIrGraphCodec.Encode(factory, program,
            clauses.SelectMany(clause => new[] { clause.Value, clause.Safe }).ToArray(), variables,
            operations: clauses.Select(clause => clause.Operation).ToArray());
        return new()
        {
            IsBodyAbstraction = isBodyAbstraction,
            Graph = encoded.Graph,
            Parameters = [.. parameters.Select(parameter => new CompilerTotalParameterArtifact
            {
                Entry = encoded.VariableIndices[parameter.Entry], Current = encoded.VariableIndices[parameter.Current],
                Old = encoded.VariableIndices[parameter.Old]
            })],
            Result = result is { } resultVariable ? encoded.VariableIndices[resultVariable] : -1,
            Clauses = [.. clauses.Select((clause, ordinal) => new CompilerTotalClauseArtifact
            {
                Kind = clause.Kind, ValueRoot = ordinal * 2, SafeRoot = ordinal * 2 + 1,
                Operation = encoded.OperationIndices[clause.Operation], ClaimId = clause.ClaimId, AssumptionId = clause.AssumptionId
            })]
        };
    }

    internal static CompilerTotalCallablePreparation? Decode(CompilerTotalCallableArtifact? artifact,
        WorkerCallableManifestEntry entry, ImmutableArray<WorkerClaimManifestEntry> claims,
        CancellationToken cancellationToken)
    {
        var decoded = DecodeCore(artifact, entry, claims, entryOnly: false, cancellationToken);
        return decoded == null ? null : new(entry.CallableId, decoded.Program!, decoded.Parameters, decoded.Result, decoded.Clauses, artifact!.IsBodyAbstraction)
        {
            ExceptionConstraints = decoded.ExceptionConstraints,
            EffectsCompleteAtEntry = artifact!.EffectsCompleteAtEntry,
            ValidEffectClaimIds = [.. artifact.ValidEffectClaimIds]
        };
    }

    internal static CompilerTotalEntryPreparation? DecodeEntry(CompilerTotalCallableArtifact? artifact,
        WorkerCallableManifestEntry entry, CancellationToken cancellationToken)
    {
        var decoded = DecodeCore(artifact, entry, [], entryOnly: true, cancellationToken);
        return decoded == null ? null : new(entry.CallableId, decoded.Factory, decoded.Parameters, decoded.Clauses);
    }

    private sealed record DecodedTotal(IrFactory Factory, IrProgram? Program, ImmutableArray<CompilerTotalParameter> Parameters,
        IrVarId? Result, ImmutableArray<CompilerTotalClause> Clauses, ImmutableArray<CompilerTotalExceptionConstraint> ExceptionConstraints);

    private static DecodedTotal? DecodeCore(CompilerTotalCallableArtifact? artifact,
        WorkerCallableManifestEntry entry, ImmutableArray<WorkerClaimManifestEntry> claims,
        bool entryOnly, CancellationToken cancellationToken)
    {
        if (artifact == null)
        { return null; }
        Require(!entryOnly || !artifact.IsBodyAbstraction, "An entry payload cannot carry a body abstraction.");
        Require(!entryOnly || !artifact.EffectsCompleteAtEntry, "An entry payload cannot claim complete effect initialization.");
        cancellationToken.ThrowIfCancellationRequested();
        if (artifact.Graph == null || artifact.Parameters == null || artifact.Clauses == null || artifact.ExceptionConstraints == null || artifact.Result < -1)
        { throw new InvalidDataException("The Total callable payload is incomplete."); }
        Require(artifact.Parameters!.Length <= CompilerPreparedBody.MaximumInstructions &&
            artifact.Clauses!.Length <= CompilerPreparedBody.MaximumInstructions, "The Total callable metadata exceeds its bound.");
        Require(artifact.Parameters.All(parameter => parameter != null) && artifact.Clauses.All(clause => clause != null),
            "The Total callable metadata contains a missing row.");
        var externalVariables = artifact.Parameters.SelectMany(parameter => new[] { parameter.Entry, parameter.Current, parameter.Old })
            .Concat(artifact.Result == -1 ? Array.Empty<int>() : [artifact.Result]).Distinct().OrderBy(index => index).ToArray();
        var externalOperations = artifact.Clauses.Select(clause => clause.Operation).Distinct().OrderBy(index => index).ToArray();
        var decoded = PortableIrGraphCodec.Decode(artifact.Graph!, externalVariables, externalOperations, cancellationToken);
        var factory = decoded.Factory;
        Require(factory.Semantics == IrExecutionSemantics.Total &&
            (entryOnly ? decoded.Program == null && artifact.Result == -1 : decoded.Program != null) &&
            decoded.Roots.Count == artifact.Clauses.Length * 2, "The Total graph has an invalid mode or root closure.");
        foreach (var term in artifact.Graph.Terms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(term.Kind is IrTermKind.Boolean or IrTermKind.Integer or IrTermKind.String or IrTermKind.Variable or IrTermKind.Null or IrTermKind.EmptyArray or IrTermKind.Length or IrTermKind.SequenceAccess or IrTermKind.Unary or
                IrTermKind.Binary or IrTermKind.Conditional or IrTermKind.Cast && SupportedType(artifact.Graph, term.Type) &&
                (term.Kind != IrTermKind.SequenceAccess || artifact.Graph.Types[term.Type].Kind is IrTypeKind.Boolean or IrTypeKind.Integer) &&
                (term.Kind != IrTermKind.EmptyArray || artifact.Graph.Types[term.Type].Kind == IrTypeKind.Sequence) &&
                (term.Kind != IrTermKind.Cast || artifact.Graph.Types[artifact.Graph.Terms[term.A].Type].Kind == IrTypeKind.Integer) &&
                (term.Kind != IrTermKind.Binary || artifact.Graph.Types[artifact.Graph.Terms[term.B].Type].Kind != IrTypeKind.String ||
                    (IrBinaryOperator)term.A is IrBinaryOperator.Equal or IrBinaryOperator.NotEqual or IrBinaryOperator.StringConcat),
                "The Total graph contains unsupported term evidence.");
        }
        var identities = new HashSet<IrVarId>();
        IrVarId Variable(int index, string name)
        {
            Require(index >= 0 && index < decoded.Variables.Count, "A Total canonical role has an invalid index.");
            var variable = decoded.Variables[index];
            var info = factory.GetVariableInfo(variable);
            Require(identities.Add(variable) && factory.GetString(info.Name) == name && Scalar(factory, info.Type),
                "Total canonical roles must be distinct, scalar, and in declaration order.");
            return variable;
        }
        var parameters = ImmutableArray.CreateBuilder<CompilerTotalParameter>(artifact.Parameters.Length);
        for (var ordinal = 0; ordinal < artifact.Parameters.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = artifact.Parameters[ordinal];
            var suffix = ordinal.ToString(CultureInfo.InvariantCulture);
            var parameter = new CompilerTotalParameter(Variable(row.Entry, "entry:" + suffix),
                Variable(row.Current, "current:" + suffix), Variable(row.Old, "old:" + suffix));
            var type = factory.GetVariableInfo(parameter.Entry).Type;
            Require(factory.GetVariableInfo(parameter.Current).Type == type && factory.GetVariableInfo(parameter.Old).Type == type,
                "Total canonical parameter types disagree.");
            parameters.Add(parameter);
        }
        IrVarId? result = artifact.Result == -1 ? null : Variable(artifact.Result, "result");
        var entryVariables = new HashSet<IrVarId>(parameters.Select(parameter => parameter.Entry));
        var currentVariables = new HashSet<IrVarId>(parameters.Select(parameter => parameter.Current));
        var postconditions = claims.Where(claim => claim.Kind == WorkerClaimKind.Postcondition).ToArray();
        var preconditions = entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.Precondition).ToArray();
        var userAssumptions = entryOnly ? [] : entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume).ToArray();
        var clauses = ImmutableArray.CreateBuilder<CompilerTotalClause>(artifact.Clauses.Length);
        var claimOrdinal = 0;
        var assumptionOrdinal = 0;
        var userAssumptionOrdinal = 0;
        for (var ordinal = 0; ordinal < artifact.Clauses.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = artifact.Clauses[ordinal];
            Require(!entryOnly || row.Kind == CompilerContractKind.Requires, "An entry payload can contain only Requires clauses.");
            Require(row.Kind is CompilerContractKind.Requires or CompilerContractKind.Ensures or CompilerContractKind.Assume &&
                row.ValueRoot == ordinal * 2 && row.SafeRoot == ordinal * 2 + 1 &&
                row.Operation >= 0 && row.Operation < decoded.Operations.Count, "A Total clause has an invalid root or site binding.");
            var value = decoded.Roots[row.ValueRoot];
            var safe = decoded.Roots[row.SafeRoot];
            Require(value.Type == factory.BooleanType && safe.Type == factory.BooleanType &&
                IrTraversal.CollectVariables([value, safe]).All(variable => identities.Contains(variable)) &&
                (row.Kind != CompilerContractKind.Requires || IrTraversal.CollectVariables([value, safe]).All(entryVariables.Contains)) &&
                (row.Kind != CompilerContractKind.Assume || IrTraversal.CollectVariables([value, safe]).All(currentVariables.Contains)),
                "A Total clause is not a closed canonical predicate.");
            var operation = decoded.Operations[row.Operation];
            var span = factory.GetOperationInfo(operation).SourceSpan;
            Require(span != null && span.Length > 0,
                "A Total clause has an invalid original source span.");
            if (row.Kind == CompilerContractKind.Ensures)
            {
                Require(claimOrdinal < postconditions.Length, "The Total claim list exceeds the manifest.");
                var claim = postconditions[claimOrdinal++];
                Require(row.ClaimId == claim.ClaimId && row.AssumptionId == null &&
                    claim.Evidence is WorkerClaimEvidence.DirectClause or WorkerClaimEvidence.ReturnAttribute or WorkerClaimEvidence.CompanionClause &&
                    claim.Location.Start == span!.Start && claim.Location.Length == span.Length,
                    "The Total claim binding does not equal the manifest.");
            }
            else if (row.Kind == CompilerContractKind.Requires)
            {
                Require(assumptionOrdinal < preconditions.Length && row.AssumptionId == preconditions[assumptionOrdinal++].Id &&
                    row.ClaimId == null, "The Total assumption binding does not equal the manifest.");
            }
            else
            {
                Require(userAssumptionOrdinal < userAssumptions.Length && row.AssumptionId == userAssumptions[userAssumptionOrdinal++].Id &&
                    row.ClaimId == null, "The Total user assumption binding does not equal the manifest.");
            }
            clauses.Add(new(row.Kind, value, safe, operation, row.ClaimId, row.AssumptionId));
        }
        Require(claimOrdinal == postconditions.Length && assumptionOrdinal == preconditions.Length && userAssumptionOrdinal == userAssumptions.Length,
            "The Total clauses do not equal the manifest.");
        if (!entryOnly)
        { ValidateProgram(decoded.Program!, result, clauses, parameters, artifact.IsBodyAbstraction, cancellationToken); }
        var exceptionConstraints = DecodeExceptionConstraints(artifact.ExceptionConstraints, claims, entryOnly, cancellationToken);
        Require(artifact.ValidEffectClaimIds != null && artifact.ValidEffectClaimIds.Length <= CompilerPreparedBody.MaximumInstructions &&
            (!entryOnly || artifact.ValidEffectClaimIds.Length == 0), "Validated effect claims have an invalid bound or mode.");
        var effectOwners = claims.Where(claim => claim.Kind == WorkerClaimKind.Effect).Select(claim => claim.ClaimId)
            .ToImmutableHashSet(StringComparer.Ordinal);
        Require(artifact.ValidEffectClaimIds!.SequenceEqual(artifact.ValidEffectClaimIds.Distinct().OrderBy(id => id, StringComparer.Ordinal)) &&
            artifact.ValidEffectClaimIds.All(id => !string.IsNullOrWhiteSpace(id) &&
                effectOwners.Contains(id)),
            "Validated effect claims must be canonical and owned by this callable.");
        return new(factory, decoded.Program, parameters.MoveToImmutable(), result, clauses.MoveToImmutable(), exceptionConstraints);
    }

    private static ImmutableArray<CompilerTotalExceptionConstraint> DecodeExceptionConstraints(
        CompilerTotalExceptionConstraintArtifact[] rows, ImmutableArray<WorkerClaimManifestEntry> claims,
        bool entryOnly, CancellationToken cancellationToken)
    {
        Require(rows.Length <= CompilerPreparedBody.MaximumInstructions && (!entryOnly || rows.Length == 0),
            "An exception constraint list has an invalid bound or mode.");
        var owned = claims.ToDictionary(claim => claim.ClaimId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var constraints = ImmutableArray.CreateBuilder<CompilerTotalExceptionConstraint>(rows.Length);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row == null || row.ClaimId == null || row.AllowedKinds == null)
            { throw new InvalidDataException("An exception constraint contains a missing row or field."); }
            Require(seen.Add(row.ClaimId) && owned.TryGetValue(row.ClaimId, out var claim) &&
                claim.Kind == WorkerClaimKind.Effect && claim.EffectContractKind is WorkerEffectContractKind.DoesNotThrow or WorkerEffectContractKind.AllowedExceptions,
                "An exception constraint must own a unique exception-effect claim.");
            var owner = owned[row.ClaimId];
            Require(row.AllowedKinds.Length <= Enum.GetValues(typeof(IrExceptionKind)).Length &&
                row.AllowedKinds.All(kind => Enum.IsDefined(typeof(IrExceptionKind), kind)) &&
                row.AllowedKinds.SequenceEqual(row.AllowedKinds.Distinct().OrderBy(kind => kind)) &&
                (owner.EffectContractKind != WorkerEffectContractKind.DoesNotThrow || row.AllowedKinds.Length == 0),
                "Allowed exception kinds must be canonical and agree with the effect contract.");
            constraints.Add(new(row.ClaimId, [.. row.AllowedKinds]));
        }
        return constraints.MoveToImmutable();
    }

    private static bool Scalar(IrFactory factory, IrTypeId type)
    {
        var info = factory.GetTypeInfo(type);
        return info.Kind == IrTypeKind.Boolean || info.Kind == IrTypeKind.Integer && info.Width is 8 or 16 or 32 or 64 ||
            info.Kind == IrTypeKind.Reference || type == factory.StringType || info.Kind == IrTypeKind.Sequence &&
                info.ElementType is { } element && (factory.GetTypeInfo(element).Kind is IrTypeKind.Boolean or IrTypeKind.Integer ||
                    element == factory.ObjectType || element == factory.StringType);
    }

    private static bool SupportedType(PortableIrGraph graph, int index)
    {
        var type = graph.Types[index];
        return type.Kind == IrTypeKind.Reference || type.Kind == IrTypeKind.String && type.Name == "string" ||
            type.Kind == IrTypeKind.Sequence && graph.Types[type.Element].Kind != IrTypeKind.Sequence && SupportedType(graph, type.Element) ||
            type.Kind == IrTypeKind.Boolean ||
            type.Kind == IrTypeKind.Integer && type.Width is 8 or 16 or 32 or 64;
    }

    private static void ValidateProgram(IrProgram program, IrVarId? result,
        IEnumerable<CompilerTotalClause> clauses, IEnumerable<CompilerTotalParameter> parameters, bool isBodyAbstraction, CancellationToken cancellationToken)
    {
        Require(program.Blocks.Length <= CompilerPreparedBody.MaximumInstructions &&
            program.Blocks.Sum(block => block.Instructions.Length) <= CompilerPreparedBody.MaximumInstructions,
            "The Total program exceeds its construction bound.");
        if (isBodyAbstraction)
        {
            ValidateBodyAbstraction(program, result, clauses, parameters);
            return;
        }
        var assumptions = new Dictionary<OperationId, CompilerTotalClause>();
        foreach (var clause in clauses.Where(clause => clause.Kind == CompilerContractKind.Assume))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(!assumptions.ContainsKey(clause.Operation), "Total user assumption sites must be distinct.");
            assumptions.Add(clause.Operation, clause);
        }
        var pointSites = new HashSet<OperationId>();
        foreach (var block in program.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (instruction is IrAssumeInstruction point)
                {
                    Require(assumptions.TryGetValue(point.Operation, out var clause) && pointSites.Add(point.Operation) &&
                        point.Condition.Id == program.Factory.Binary(IrBinaryOperator.AndAlso, clause.Safe, clause.Value).Id,
                        "A Total point assumption does not equal its owned clause evidence.");
                }
            }
        }
        Require(pointSites.Count == assumptions.Count, "A Total user assumption is missing its original program point.");
        var order = IrBlockOrder.TryCreateAcyclicOrder(program, _ => { cancellationToken.ThrowIfCancellationRequested(); return true; }, out var failure);
        Require(failure is IrAcyclicOrderFailure.None or IrAcyclicOrderFailure.CyclicControlFlow,
            "The Total program contains unsupported control flow.");
        if (failure == IrAcyclicOrderFailure.CyclicControlFlow)
        {
            ValidateCyclicProgram(program, result, parameters, pointSites.Count, cancellationToken);
            return;
        }
        if (assumptions.Count != 0)
        { ValidateAssumptionPrologue(program, parameters, order, pointSites.Count, cancellationToken); }
        var resultType = result is { } variable ? program.Factory.GetVariableInfo(variable).Type : (IrTypeId?)null;
        var pendingThrows = new Dictionary<IrBlockId, bool> { [program.Entry] = false };
        foreach (var blockId in order)
        {
            var block = program.GetBlock(blockId);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var instruction in block.Instructions)
            {
                Require(IsSourceInstruction(program, instruction, result, parameters),
                    "The Total source program contains unsupported executable evidence.");
                if (instruction is IrReturnInstruction returned)
                {
                    Require(returned.Value?.Type == resultType, "The Total return type disagrees with its canonical result.");
                }
            }
            var pending = pendingThrows[blockId];
            if (block.Terminator is IrExceptionalExitInstruction)
            { Require(pending, "A Total exceptional exit is reachable without a pending throw."); }
            else if (block.Terminator is IrThrowInstruction thrown)
            { Add(thrown.Target, true); }
            else if (block.Terminator is IrGotoInstruction go)
            { Add(go.Target, pending); }
            else if (block.Terminator is IrBranchInstruction branch)
            { Add(branch.WhenTrue, pending); Add(branch.WhenFalse, pending); }
        }

        void Add(IrBlockId block, bool pending)
        {
            pendingThrows[block] = !pendingThrows.TryGetValue(block, out var previous) ? pending : previous && pending;
        }
    }

    private static bool IsSourceInstruction(IrProgram program, IrInstruction instruction, IrVarId? result,
        IEnumerable<CompilerTotalParameter> parameters)
    {
        if (instruction is IrHavocInstruction havoc)
        {
            return havoc.HavocKind == IrHavocKind.Variables && havoc.Origin == IrHavocOrigin.Approximation &&
                havoc.Variables.Length == 1 && program.Factory.GetVariableInfo(havoc.Variables[0]).Type == program.Factory.BooleanType &&
                havoc.Variables[0] != result && !parameters.Any(parameter => havoc.Variables[0] == parameter.Entry ||
                    havoc.Variables[0] == parameter.Current || havoc.Variables[0] == parameter.Old);
        }
        return instruction.Kind is IrInstructionKind.Allocate or IrInstructionKind.Write or IrInstructionKind.Lock or IrInstructionKind.Assign or
            IrInstructionKind.Branch or IrInstructionKind.Goto or IrInstructionKind.Return or IrInstructionKind.Throw or
            IrInstructionKind.ExceptionalExit or IrInstructionKind.Assume;
    }

    private static void ValidateBodyAbstraction(IrProgram program, IrVarId? result,
        IEnumerable<CompilerTotalClause> clauses, IEnumerable<CompilerTotalParameter> parameters)
    {
        var canonical = parameters.ToArray();
        Require(program.Blocks.Length == 1 && clauses.All(clause => clause.Kind != CompilerContractKind.Assume),
            "A body abstraction must have one unconstrained normal block and no body assumptions.");
        var instructions = program.Blocks[0].Instructions;
        Require(instructions.Length == canonical.Length * 2 + 2, "A body abstraction has an invalid instruction shape.");
        var mutable = canonical.Select(parameter => parameter.Current).Concat(result is { } resultId ? [resultId] : Array.Empty<IrVarId>()).ToArray();
        Require(mutable.All(variable => program.Factory.GetTypeInfo(program.Factory.GetVariableInfo(variable).Type).Kind is IrTypeKind.Boolean or IrTypeKind.Integer),
            "A body abstraction cannot model reference or heap observations.");
        for (var ordinal = 0; ordinal < canonical.Length; ordinal++)
        {
            var parameter = canonical[ordinal];
            Require(instructions[ordinal * 2] is IrAssignInstruction current && current.Target == parameter.Current &&
                current.Value is IrVariableTerm input && input.Variable == parameter.Entry &&
                instructions[ordinal * 2 + 1] is IrAssignInstruction old && old.Target == parameter.Old &&
                old.Value is IrVariableTerm previous && previous.Variable == parameter.Entry,
                "A body abstraction must preserve the original entry snapshots.");
        }
        Require(instructions[instructions.Length - 2] is IrHavocInstruction, "A body abstraction must contain its approximation marker.");
        var havoc = (IrHavocInstruction)instructions[instructions.Length - 2];
        if (mutable.Length == 0)
        {
            Require(havoc.Variables.Length == 1, "A void abstraction must have one unused approximation marker.");
            var marker = program.Factory.GetVariableInfo(havoc.Variables[0]);
            Require(marker.Type == program.Factory.BooleanType && program.Factory.GetString(marker.Name) == "abstract-body",
                "A void abstraction has an invalid approximation marker.");
            mutable = [marker.Id];
        }
        Require(havoc.HavocKind == IrHavocKind.Variables && havoc.Origin == IrHavocOrigin.Approximation &&
            havoc.Variables.Length == mutable.Length && new HashSet<IrVarId>(havoc.Variables).SetEquals(mutable),
            "A body abstraction must forget every mutable scalar value.");
        Require(instructions[instructions.Length - 1] is IrReturnInstruction returned &&
            (result is { } returnedId ? returned.Value is IrVariableTerm value && value.Variable == returnedId : returned.Value == null),
            "A body abstraction cannot constrain the returned value.");
    }

    private static void ValidateCyclicProgram(IrProgram program, IrVarId? result,
        IEnumerable<CompilerTotalParameter> parameters, int pointCount, CancellationToken cancellationToken)
    {
        // Explore both pending-throw states. This is the finite fixed point of
        // the existing AND join: any nonthrowing incoming path forbids an exit.
        var resultType = result is { } variable ? program.Factory.GetVariableInfo(variable).Type : (IrTypeId?)null;
        var pending = new Queue<(IrBlockId Block, bool Thrown, bool Body)>();
        var visited = new HashSet<(IrBlockId Block, bool Thrown, bool Body)>();
        pending.Enqueue((program.Entry, false, false));
        var canonicalParameters = parameters.ToArray();
        var inputPrefix = pointCount == 0 ? 0 : canonicalParameters.Length * 2;
        var remaining = CompilerPreparedBody.MaximumInstructions * 8;
        while (pending.Count != 0)
        {
            Spend();
            var state = pending.Dequeue();
            if (!visited.Add(state))
            { continue; }
            var block = program.GetBlock(state.Block);
            Require(pointCount == 0 || state.Block != program.Entry || !state.Body,
                "A Total source body re-enters its canonical input initialization.");
            var body = state.Body;
            var ordinal = 0;
            foreach (var instruction in block.Instructions)
            {
                Spend();
                Require(IsSourceInstruction(program, instruction, result, parameters),
                    "The Total source program contains unsupported executable evidence.");
                if (instruction is IrReturnInstruction returned)
                { Require(returned.Value?.Type == resultType, "The Total return type disagrees with its canonical result."); }
                if (pointCount != 0 && !(state.Block == program.Entry && ordinal < inputPrefix))
                {
                    if (instruction is IrAssumeInstruction)
                    { Require(!body, "A Total source assumption is outside the direct prologue."); }
                    else if (instruction is not IrGotoInstruction)
                    { body = true; }
                }
                ordinal++;
            }
            switch (block.Terminator)
            {
                case IrExceptionalExitInstruction:
                    Require(state.Thrown, "A Total exceptional exit is reachable without a pending throw.");
                    break;
                case IrThrowInstruction thrown:
                    pending.Enqueue((thrown.Target, true, body));
                    break;
                case IrGotoInstruction go:
                    pending.Enqueue((go.Target, state.Thrown, body));
                    break;
                case IrBranchInstruction branch:
                    pending.Enqueue((branch.WhenTrue, state.Thrown, body));
                    pending.Enqueue((branch.WhenFalse, state.Thrown, body));
                    break;
            }
        }
        if (pointCount == 0)
        { return; }
        // Before the first body instruction, only canonical initialization,
        // point filters and unconditional links belong to a direct prologue.
        // Its single path must be finite, and contain every declared point.
        var entry = program.GetBlock(program.Entry);
        var initializationCount = 0;
        foreach (var parameter in canonicalParameters)
        {
            Initialization(parameter.Current, parameter.Entry);
            Initialization(parameter.Old, parameter.Entry);
        }
        var prologue = new HashSet<IrBlockId>();
        var current = program.Entry;
        var reachedPoints = 0;
        while (true)
        {
            Spend();
            Require(prologue.Add(current), "A Total source assumption prologue is cyclic.");
            var block = program.GetBlock(current);
            for (var ordinal = current == program.Entry ? initializationCount : 0; ordinal < block.Instructions.Length; ordinal++)
            {
                Spend();
                if (block.Instructions[ordinal] is IrAssumeInstruction)
                { reachedPoints++; }
                else if (block.Instructions[ordinal] is not IrGotoInstruction)
                {
                    Require(reachedPoints == pointCount, "A Total source assumption is outside the direct prologue.");
                    return;
                }
            }
            current = ((IrGotoInstruction)block.Terminator).Target;
        }

        void Initialization(IrVarId target, IrVarId source)
        {
            Spend();
            Require(initializationCount < entry.Instructions.Length && entry.Instructions[initializationCount] is IrAssignInstruction assign &&
                assign.Target == target && assign.Value is IrVariableTerm value && value.Variable == source &&
                program.Factory.GetOperationInfo(assign.Operation).SourceSpan == null,
                "The Total source prologue is missing its canonical input initialization.");
            initializationCount++;
        }
        void Spend()
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(--remaining >= 0, "The Total cyclic validation exceeds its construction bound.");
        }
    }

    private static void ValidateAssumptionPrologue(IrProgram program, IEnumerable<CompilerTotalParameter> parameters,
        IEnumerable<IrBlockId> order, int pointCount, CancellationToken cancellationToken)
    {
        var entry = program.GetBlock(program.Entry);
        var initializationCount = 0;
        foreach (var parameter in parameters)
        {
            Initialization(parameter.Current, parameter.Entry);
            Initialization(parameter.Old, parameter.Entry);
        }
        var bodyStarted = new Dictionary<IrBlockId, bool> { [program.Entry] = false };
        var reachedPoints = 0;
        foreach (var blockId in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = program.GetBlock(blockId);
            var body = bodyStarted[blockId];
            for (var ordinal = 0; ordinal < block.Instructions.Length; ordinal++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (blockId == program.Entry && ordinal < initializationCount)
                { continue; }
                if (block.Instructions[ordinal] is IrAssumeInstruction)
                {
                    Require(!body, "A Total source assumption is outside the direct prologue.");
                    reachedPoints++;
                }
                else if (block.Instructions[ordinal] is not IrGotoInstruction)
                { body = true; }
            }
            if (block.Terminator is IrGotoInstruction go)
            { Add(go.Target); }
            else if (block.Terminator is IrBranchInstruction branch)
            { Add(branch.WhenTrue); Add(branch.WhenFalse); }
            else if (block.Terminator is IrThrowInstruction thrown)
            { Add(thrown.Target); }

            void Add(IrBlockId destination)
            { bodyStarted[destination] = body || bodyStarted.TryGetValue(destination, out var previous) && previous; }
        }
        Require(reachedPoints == pointCount, "A Total source assumption is unreachable from the direct prologue.");

        void Initialization(IrVarId target, IrVarId source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(initializationCount < entry.Instructions.Length && entry.Instructions[initializationCount] is IrAssignInstruction assign &&
                assign.Target == target && assign.Value is IrVariableTerm value && value.Variable == source &&
                program.Factory.GetOperationInfo(assign.Operation).SourceSpan == null,
                "The Total source prologue is missing its canonical input initialization.");
            initializationCount++;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        { throw new InvalidDataException(message); }
    }
}
