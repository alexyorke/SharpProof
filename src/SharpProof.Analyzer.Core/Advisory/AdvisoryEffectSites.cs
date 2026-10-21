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
    public Location Location { get; init; } = Location ?? throw new System.ArgumentNullException(nameof(Location));
}

internal sealed record AdvisoryEffectAnalysis(ImmutableArray<AdvisoryEffectSite> Sites, string? Gap)
{
    public ImmutableArray<AdvisoryEffectSite> Sites { get; init; } = Sites.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Sites)) : Sites;
}

// The effect sites of the same Total IR body the worker verifies, found by
// graph reachability alone. A site this pass does not report cannot be
// reached, but a reported site may be infeasible: only Z3 proves an effect
// claim.
internal static class AdvisoryEffectSites
{
    private static readonly ImmutableArray<string> ReadPrefixes =
        ["FieldReference@", "StaticFieldReference@", "PropertyReference@", "ArrayElementReference@", "Increment@", "Decrement@",
            "CompoundAssignment@"];

    internal static AdvisoryEffectAnalysis Analyze(CSharpCompilation compilation, IMethodSymbol method,
        SyntaxNode declaration, CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        declaration = RepositoryParameterGuard.NotNull(declaration, nameof(declaration));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        var (body, gap) = AdvisoryLowering.Lower(compilation, method, declaration, cancellationToken);
        if (body == null)
        { return Gap(gap ?? throw new System.InvalidOperationException("Lowering returned no body and no gap reason.")); }
        return Analyze(body.Program, body.Locate, method.Locations.FirstOrDefault() ?? Location.None, cancellationToken);
    }

    internal static AdvisoryEffectAnalysis Analyze(IrProgram program, Func<OperationId, Location?> locate,
        Location fallback, CancellationToken cancellationToken)
    {
        fallback = RepositoryParameterGuard.NotNull(fallback, nameof(fallback));
        locate = RepositoryParameterGuard.NotNull(locate, nameof(locate));
        program = RepositoryParameterGuard.NotNull(program, nameof(program));
        var reachable = Reachable(program, [program.Entry]);
        var escapes = Escaping(program);
        var factory = program.Factory;
        var sequenceReaders = IrSequenceReads.Readers(program);
        var sites = ImmutableArray.CreateBuilder<AdvisoryEffectSite>();
        foreach (var block in program.Blocks.Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (reachable.Contains(block.Id))))
        {
            foreach (var instruction in block.Instructions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var location = locate(instruction.Operation) ?? fallback;
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
    { reason = RepositoryParameterGuard.NotNull(reason, nameof(reason)); return new([], reason); }

    private static ImmutableHashSet<IrBlockId> Reachable(IrProgram program, IEnumerable<IrBlockId> roots)
    {
        program = RepositoryParameterGuard.NotNull(program, nameof(program));
        roots = RepositoryParameterGuard.NotNull(roots, nameof(roots));
        var reachable = ImmutableHashSet.CreateBuilder<IrBlockId>();
        var pending = ImmutableStack<IrBlockId>.Empty;
        foreach (var root in roots)
        { pending = pending.Push(root); }
        while (!pending.IsEmpty)
        {
            var current = pending.Peek();
            pending = pending.Pop();
            if (!reachable.Add(current))
            { continue; }
            foreach (var successor in Successors(program.GetBlock(current)))
            { pending = pending.Push(successor); }
        }
        return reachable.ToImmutable();
    }

    private static ImmutableHashSet<IrBlockId> Escaping(IrProgram program)
    {
        program = RepositoryParameterGuard.NotNull(program, nameof(program));
        var predecessors = program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (Successors(block).Select(successor => successor.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(successor)) : ((successor, block.Id)))))
            .ToLookup(edge => edge.successor, edge => edge.Id);
        var escaping = ImmutableHashSet.CreateBuilder<IrBlockId>();
        var roots = program.Blocks
            .Where(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions.LastOrDefault() is IrExceptionalExitInstruction)).Select(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Id));
        var pending = ImmutableStack<IrBlockId>.Empty;
        foreach (var root in roots)
        { pending = pending.Push(root); }
        while (!pending.IsEmpty)
        {
            var current = pending.Peek();
            pending = pending.Pop();
            if (!escaping.Add(current))
            { continue; }
            foreach (var predecessor in predecessors[current])
            { pending = pending.Push(predecessor); }
        }
        return escaping.ToImmutable();
    }

    private static IEnumerable<IrBlockId> Successors(IrBasicBlock block)
    {
        block = RepositoryParameterGuard.NotNull(block, nameof(block));
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
        RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        var description = Describe(factory, site);
        return ReadPrefixes.Any(prefix => prefix is null ? throw new System.ArgumentNullException(nameof(prefix)) : (description.StartsWith(prefix, StringComparison.Ordinal)));
    }

    private static ImmutableArray<string> ThrownTypes(IrFactory factory, OperationId site)
    {
        RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site));
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        var description = Describe(factory, site);
        const string Prefix = "explicit-throw:";
        if (!description.StartsWith(Prefix, StringComparison.Ordinal))
        { return []; }
        description = description.Substring(Prefix.Length);
        if (description.StartsWith("exact:", StringComparison.Ordinal))
        { description = description.Substring("exact:".Length); }
        return [.. description.Split(';').Where(name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (name.Length != 0))];
    }

    private static string Describe(IrFactory factory, OperationId site)
    { factory = RepositoryParameterGuard.NotNull(factory, nameof(factory)); RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site)); return factory.GetOperationInfo(site).Description is { } id ? factory.GetString(id) : ""; }
}
