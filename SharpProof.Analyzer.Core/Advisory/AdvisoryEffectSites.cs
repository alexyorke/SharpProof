namespace SharpProof.Analyzer;

internal enum AdvisoryEffectSiteKind
{
    Allocation,
    Write,
    Read,
    Lock,
    Call,
    Throw
}

// One effect site of a lowered body. A throw escapes when the exceptional
// exit is reachable from it; a caught one still constructs its exception.
internal sealed record AdvisoryEffectSite(AdvisoryEffectSiteKind Kind, Location Location)
{
    internal IrWriteRegion Region { get; init; }
    internal IrOpaqueCallEffects Effects { get; init; }
    internal IrExceptionKind Exception { get; init; }
    internal bool Escapes { get; init; }
    // A static field read reads ambient state.
    internal bool Static { get; init; }
    // An explicit throw's static type and its bases.
    internal ImmutableArray<string> ThrownTypes { get; init; } = [];
}

internal sealed record AdvisoryEffectAnalysis(ImmutableArray<AdvisoryEffectSite> Sites, string? Gap);

// The effect sites of the same Total IR body the worker verifies, found by
// graph reachability alone. A site this pass does not report cannot be
// reached, but a reported site may be infeasible: only Z3 proves an effect
// claim.
internal static class AdvisoryEffectSites
{
    private static readonly string[] ReadPrefixes =
        ["FieldReference@", "StaticFieldReference@", "PropertyReference@", "ArrayElementReference@", "Increment@", "Decrement@",
            "CompoundAssignment@"];

    internal static AdvisoryEffectAnalysis Analyze(CSharpCompilation compilation, IMethodSymbol method,
        SyntaxNode declaration, CancellationToken cancellationToken)
    {
        var (body, gap) = AdvisoryLowering.Lower(compilation, method, declaration, cancellationToken);
        if (body == null)
        { return Gap(gap!); }
        var program = body.Program;
        var reachable = Reachable(program, [program.Entry]);
        var escapes = Escaping(program);
        var factory = program.Factory;
        var sequenceReaders = IrSequenceReads.Readers(program);
        var sites = ImmutableArray.CreateBuilder<AdvisoryEffectSite>();
        foreach (var block in program.Blocks.Where(block => reachable.Contains(block.Id)))
        {
            foreach (var instruction in block.Instructions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var location = body.Locate(instruction.Operation) ?? method.Locations.FirstOrDefault() ?? Location.None;
                if (sequenceReaders.Contains(instruction.Id))
                { sites.Add(new(AdvisoryEffectSiteKind.Read, location)); }
                switch (instruction)
                {
                    case IrAllocationInstruction:
                        sites.Add(new(AdvisoryEffectSiteKind.Allocation, location));
                        break;
                    case IrWriteInstruction write:
                        sites.Add(new(AdvisoryEffectSiteKind.Write, location)
                        { Region = IrWriteSites.IsObservable(factory, write) ? write.Region : IrWriteRegion.Local });
                        break;
                    case IrLockInstruction:
                        sites.Add(new(AdvisoryEffectSiteKind.Lock, location));
                        break;
                    case IrCallInstruction { Receiver: null, Target: null } call:
                        sites.Add(new(AdvisoryEffectSiteKind.Call, location) { Effects = IrOpaqueCallSite.Effects(factory, call.Operation) });
                        break;
                    case IrHavocInstruction { Origin: IrHavocOrigin.Approximation } havoc when ReadsState(factory, havoc.Operation):
                        sites.Add(new(AdvisoryEffectSiteKind.Read, location)
                        { Static = Describe(factory, havoc.Operation).StartsWith("StaticFieldReference@", StringComparison.Ordinal) });
                        break;
                    case IrThrowInstruction thrown:
                        sites.Add(new(AdvisoryEffectSiteKind.Throw, location)
                        {
                            Exception = thrown.ExceptionKind,
                            Escapes = escapes.Contains(thrown.Target),
                            ThrownTypes = ThrownTypes(factory, thrown.Operation)
                        });
                        break;
                }
            }
        }
        return new(sites.ToImmutable(), null);
    }

    private static AdvisoryEffectAnalysis Gap(string reason)
    { return new([], reason); }

    private static HashSet<IrBlockId> Reachable(IrProgram program, IEnumerable<IrBlockId> roots)
    {
        var reachable = new HashSet<IrBlockId>();
        var pending = new Stack<IrBlockId>(roots);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!reachable.Add(current))
            { continue; }
            foreach (var successor in Successors(program.GetBlock(current)))
            { pending.Push(successor); }
        }
        return reachable;
    }

    private static HashSet<IrBlockId> Escaping(IrProgram program)
    {
        var predecessors = program.Blocks.SelectMany(block => Successors(block).Select(successor => (successor, block.Id)))
            .ToLookup(edge => edge.successor, edge => edge.Id);
        var escaping = new HashSet<IrBlockId>();
        var pending = new Stack<IrBlockId>(program.Blocks
            .Where(block => block.Instructions.LastOrDefault() is IrExceptionalExitInstruction).Select(block => block.Id));
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!escaping.Add(current))
            { continue; }
            foreach (var predecessor in predecessors[current])
            { pending.Push(predecessor); }
        }
        return escaping;
    }

    private static IEnumerable<IrBlockId> Successors(IrBasicBlock block)
    {
        return block.Instructions.LastOrDefault() switch
        {
            IrGotoInstruction go => [go.Target],
            IrBranchInstruction branch => [branch.WhenTrue, branch.WhenFalse],
            IrThrowInstruction thrown => [thrown.Target],
            _ => []
        };
    }

    // Field and element reads, including the read inside an increment or a
    // compound assignment, are approximations at these sites.
    private static bool ReadsState(IrFactory factory, OperationId site)
    {
        var description = Describe(factory, site);
        return ReadPrefixes.Any(prefix => description.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static ImmutableArray<string> ThrownTypes(IrFactory factory, OperationId site)
    {
        var description = Describe(factory, site);
        const string Prefix = "explicit-throw:";
        if (!description.StartsWith(Prefix, StringComparison.Ordinal))
        { return []; }
        description = description.Substring(Prefix.Length);
        if (description.StartsWith("exact:", StringComparison.Ordinal))
        { description = description.Substring("exact:".Length); }
        return [.. description.Split(';').Where(name => name.Length != 0)];
    }

    private static string Describe(IrFactory factory, OperationId site)
    { return factory.GetOperationInfo(site).Description is { } id ? factory.GetString(id) : ""; }
}
