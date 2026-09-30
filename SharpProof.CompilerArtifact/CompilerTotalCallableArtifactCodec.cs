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
        var variables = preparation.Parameters.SelectMany(parameter => new[] { parameter.Entry, parameter.Current, parameter.Old })
            .Concat(preparation.Result is { } result ? [result] : Array.Empty<IrVarId>()).ToArray();
        var encoded = PortableIrGraphCodec.Encode(preparation.Program.Factory, preparation.Program,
            preparation.Clauses.SelectMany(clause => new[] { clause.Value, clause.Safe }).ToArray(), variables,
            operations: preparation.Clauses.Select(clause => clause.Operation).ToArray());
        return new()
        {
            Graph = encoded.Graph,
            Parameters = [.. preparation.Parameters.Select(parameter => new CompilerTotalParameterArtifact
            {
                Entry = encoded.VariableIndices[parameter.Entry], Current = encoded.VariableIndices[parameter.Current],
                Old = encoded.VariableIndices[parameter.Old]
            })],
            Result = preparation.Result is { } resultVariable ? encoded.VariableIndices[resultVariable] : -1,
            Clauses = [.. preparation.Clauses.Select((clause, ordinal) => new CompilerTotalClauseArtifact
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
        if (artifact == null)
        { return null; }
        cancellationToken.ThrowIfCancellationRequested();
        if (artifact.Graph == null || artifact.Parameters == null || artifact.Clauses == null || artifact.Result < -1)
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
        Require(factory.Semantics == IrExecutionSemantics.Total && decoded.Program != null &&
            decoded.Roots.Count == artifact.Clauses.Length * 2, "The Total graph has an invalid mode or root closure.");
        foreach (var term in artifact.Graph.Terms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = artifact.Graph.Types[term.Type];
            Require(term.Kind is IrTermKind.Boolean or IrTermKind.Integer or IrTermKind.Variable or IrTermKind.Unary or
                IrTermKind.Binary or IrTermKind.Conditional or IrTermKind.Cast &&
                (type.Kind == IrTypeKind.Boolean || type.Kind == IrTypeKind.Integer && type.Width is 8 or 16 or 32 or 64),
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
        var postconditions = claims.Where(claim => claim.Kind == WorkerClaimKind.Postcondition).ToArray();
        var preconditions = entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.Precondition).ToArray();
        Require(!entry.Assumptions.Any(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume),
            "Total source Assume evidence is not yet supported.");
        var clauses = ImmutableArray.CreateBuilder<CompilerTotalClause>(artifact.Clauses.Length);
        var claimOrdinal = 0;
        var assumptionOrdinal = 0;
        for (var ordinal = 0; ordinal < artifact.Clauses.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = artifact.Clauses[ordinal];
            Require(row.Kind is CompilerContractKind.Requires or CompilerContractKind.Ensures &&
                row.ValueRoot == ordinal * 2 && row.SafeRoot == ordinal * 2 + 1 &&
                row.Operation >= 0 && row.Operation < decoded.Operations.Count, "A Total clause has an invalid root or site binding.");
            var value = decoded.Roots[row.ValueRoot];
            var safe = decoded.Roots[row.SafeRoot];
            Require(value.Type == factory.BooleanType && safe.Type == factory.BooleanType &&
                IrTraversal.CollectVariables([value, safe]).All(variable => identities.Contains(variable)) &&
                (row.Kind != CompilerContractKind.Requires || IrTraversal.CollectVariables([value, safe]).All(entryVariables.Contains)),
                "A Total clause is not a closed canonical predicate.");
            var operation = decoded.Operations[row.Operation];
            var span = factory.GetOperationInfo(operation).SourceSpan;
            Require(span != null && span.Length > 0,
                "A Total clause has an invalid original source span.");
            if (row.Kind == CompilerContractKind.Ensures)
            {
                Require(claimOrdinal < postconditions.Length, "The Total claim list exceeds the manifest.");
                var claim = postconditions[claimOrdinal++];
                Require(row.ClaimId == claim.ClaimId && row.AssumptionId == null && claim.Evidence == WorkerClaimEvidence.DirectClause &&
                    claim.Location.Start == span!.Start && claim.Location.Length == span.Length,
                    "The Total claim binding does not equal the manifest.");
            }
            else
            {
                Require(assumptionOrdinal < preconditions.Length && row.AssumptionId == preconditions[assumptionOrdinal++].Id &&
                    row.ClaimId == null, "The Total assumption binding does not equal the manifest.");
            }
            clauses.Add(new(row.Kind, value, safe, operation, row.ClaimId, row.AssumptionId));
        }
        Require(claimOrdinal == postconditions.Length && assumptionOrdinal == preconditions.Length,
            "The Total clauses do not equal the manifest.");
        ValidateProgram(decoded.Program!, result, cancellationToken);
        return new(entry.CallableId, decoded.Program!, parameters.MoveToImmutable(), result, clauses.MoveToImmutable());
    }

    private static bool Scalar(IrFactory factory, IrTypeId type)
    {
        var info = factory.GetTypeInfo(type);
        return info.Kind == IrTypeKind.Boolean || info.Kind == IrTypeKind.Integer && info.Width is 8 or 16 or 32 or 64;
    }

    private static void ValidateProgram(IrProgram program, IrVarId? result, CancellationToken cancellationToken)
    {
        Require(program.Blocks.Length <= CompilerPreparedBody.MaximumInstructions &&
            program.Blocks.Sum(block => block.Instructions.Length) <= CompilerPreparedBody.MaximumInstructions,
            "The Total program exceeds its construction bound.");
        var order = IrBlockOrder.TryCreateAcyclicOrder(program, _ => { cancellationToken.ThrowIfCancellationRequested(); return true; }, out var failure);
        Require(failure == IrAcyclicOrderFailure.None, "The Total program must be acyclic.");
        var resultType = result is { } variable ? program.Factory.GetVariableInfo(variable).Type : (IrTypeId?)null;
        var pendingThrows = new Dictionary<IrBlockId, bool> { [program.Entry] = false };
        foreach (var blockId in order)
        {
            var block = program.GetBlock(blockId);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var instruction in block.Instructions)
            {
                Require(instruction.Kind is IrInstructionKind.Assign or IrInstructionKind.Branch or IrInstructionKind.Goto or
                    IrInstructionKind.Return or IrInstructionKind.Throw or IrInstructionKind.ExceptionalExit,
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        { throw new InvalidDataException(message); }
    }
}
