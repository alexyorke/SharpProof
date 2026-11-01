// This step runs only in the build-time compiler collector.
namespace SharpProof.CompilerCollector;

// Call-precondition claims join the manifest after lowering, because only a
// lowered body says which calls are modeled. A call inside an inlined callee
// that is verified on its own belongs to that callee: the callee's Requires is
// not assumed in the inlined body, so checking the call again in its caller
// would repeat the failure the caller already reports at its own call.
internal static class CompilerCallPreconditionClaims
{
    internal sealed record LoweredCallable(ManifestCallableTarget Target, CompilerCallablePreparation Preparation)
    {
        public ManifestCallableTarget Target { get; init; } = Target ?? throw new ArgumentNullException(nameof(Target));
        public CompilerCallablePreparation Preparation { get; init; } = Preparation ?? throw new ArgumentNullException(nameof(Preparation));
    }

    internal static (WorkerClaimManifest Manifest, ImmutableArray<LoweredCallable> Callables) Attach(CSharpCompilation compilation,
        ImmutableArray<CompilerSyntaxTreeSnapshot> trees, WorkerClaimManifest manifest, ImmutableArray<LoweredCallable> lowered,
        ImmutableHashSet<ManifestCallableTarget> contractCallers, CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        manifest = RepositoryParameterGuard.NotNull(manifest, nameof(manifest));
        contractCallers = RepositoryParameterGuard.NotNull(contractCallers, nameof(contractCallers));
        RepositoryParameterGuard.RequireInitialized(trees.IsDefault, nameof(trees), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(lowered.IsDefault, nameof(lowered), "Array must be initialized.");
        var documents = ImmutableDictionary.CreateBuilder<string, SyntaxTree>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < compilation.SyntaxTrees.Length; ordinal++)
        {
            if (!documents.ContainsKey(trees[ordinal].Path))
            { documents.Add(trees[ordinal].Path, compilation.SyntaxTrees[ordinal]); }
        }
        var sources = documents.ToImmutable();
        var verified = lowered.Where(IsVerified).Select(static callable => callable is null ? throw new ArgumentNullException(nameof(callable)) : CompilerIdentityBridge.CreateSymbolDisplay(callable.Target.Method))
            .ToImmutableHashSet(StringComparer.Ordinal);
        var claims = ImmutableArray.CreateBuilder<WorkerClaimManifestEntry>();
        var published = ImmutableArray.CreateBuilder<LoweredCallable>(lowered.Length);
        foreach (var callable in lowered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var callableClaims = IsVerified(callable) ? CreateClaims(compilation, sources, callable, verified, cancellationToken) : default;
            if (callableClaims.Rows.IsDefaultOrEmpty)
            {
                if (!contractCallers.Contains(callable.Target))
                { published.Add(callable); }
                continue;
            }
            claims.AddRange(callableClaims.Claims);
            var entry = WithClaims(callable.Preparation.Entry, callableClaims.Claims);
            published.Add(callable with
            {
                Preparation = callable.Preparation with
                {
                    Entry = entry,
                    Total = callable.Preparation.Total! with { CallPreconditions = callableClaims.Rows }
                }
            });
        }
        var result = manifest with
        {
            Callables = [.. published.Select(static callable => callable is null ? throw new ArgumentNullException(nameof(callable)) : callable.Preparation.Entry)],
            Claims = [.. manifest.Claims, .. claims]
        };
        WorkerProtocolJson.SealManifest(result);
        return (result, published.ToImmutable());
    }

    // A callable's rows are checked only where its own body can be trusted to
    // carry them all.
    private static bool IsVerified(LoweredCallable callable)
    {
        callable = RepositoryParameterGuard.NotNull(callable, nameof(callable));
        return callable.Target.IsVerifierSupported && callable.Preparation.IsSuccess &&
            callable.Preparation.Total is { IsBodyAbstraction: false };
    }

    private static (ImmutableArray<CompilerTotalCallPrecondition> Rows, ImmutableArray<WorkerClaimManifestEntry> Claims) CreateClaims(
        CSharpCompilation compilation, ImmutableDictionary<string, SyntaxTree> documents, LoweredCallable callable,
        ImmutableHashSet<string> verified, CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        documents = RepositoryParameterGuard.NotNull(documents, nameof(documents));
        callable = RepositoryParameterGuard.NotNull(callable, nameof(callable));
        verified = RepositoryParameterGuard.NotNull(verified, nameof(verified));
        var total = callable.Preparation.Total!;
        if (total.CallPreconditions.IsEmpty)
        { return default; }
        var factory = total.Program.Factory;
        var markers = ImmutableDictionaryCapture.Create(total.Program.Blocks.SelectMany(static block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions),
            static instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.Id,
            static instruction => instruction ?? throw new ArgumentNullException(nameof(instruction)));
        var entry = callable.Preparation.Entry;
        var ranks = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        var rows = ImmutableArray.CreateBuilder<CompilerTotalCallPrecondition>(total.CallPreconditions.Length);
        var claims = ImmutableArray.CreateBuilder<WorkerClaimManifestEntry>();
        foreach (var row in total.CallPreconditions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsOwned(row, verified))
            {
                rows.Add(row);
                continue;
            }
            var callee = WorkerCallPreconditionTarget.StableCallee(row.CalleeIdentity);
            var key = callee + "#" + row.ClauseOrdinal.ToString(CultureInfo.InvariantCulture);
            ranks.TryGetValue(key, out var rank);
            ranks[key] = rank + 1;
            var claimId = SemanticClaimIdentity.CreateCallPrecondition(compilation.Assembly.Identity.Name, entry.CallableId,
                callee, row.ClauseOrdinal, rank);
            claims.Add(new WorkerClaimManifestEntry
            {
                ClaimId = claimId,
                CallableId = entry.CallableId,
                Ordinal = entry.ClaimIds.Length + claims.Count,
                Kind = WorkerClaimKind.CallPrecondition,
                Evidence = WorkerClaimEvidence.CallSite,
                CallPrecondition = new WorkerCallPreconditionTarget
                {
                    Callee = row.CalleeIdentity,
                    ClauseOrdinal = row.ClauseOrdinal,
                    ClauseLocation = Project(documents, factory.GetOperationInfo(row.ClauseSite).SourceSpan!)
                },
                Location = Project(documents, factory.GetOperationInfo(markers[row.Instruction].Operation).SourceSpan!)
            });
            rows.Add(row with { ClaimId = claimId });
        }
        return claims.Count == 0 ? default : (rows.MoveToImmutable(), claims.ToImmutable());
    }

    // The last hop is the contract callee; the hops before it are the frames
    // the call was inlined through.
    private static bool IsOwned(CompilerTotalCallPrecondition row, ImmutableHashSet<string> verified)
    {
        row = RepositoryParameterGuard.NotNull(row, nameof(row));
        verified = RepositoryParameterGuard.NotNull(verified, nameof(verified));
        return row.MetadataClause == null && !row.Ancestry.Take(row.Ancestry.Length - 1)
            .Any(hop => hop is null ? throw new ArgumentNullException(nameof(hop)) : verified.Contains(hop.CalleeIdentity));
    }

    private static WorkerSourceLocation Project(ImmutableDictionary<string, SyntaxTree> documents, IrSourceSpan span)
    {
        documents = RepositoryParameterGuard.NotNull(documents, nameof(documents));
        span = RepositoryParameterGuard.NotNull(span, nameof(span));
        return CompilerSourceLocationProjection.Create(Location.Create(documents[span.Document],
            new Microsoft.CodeAnalysis.Text.TextSpan(span.Start, span.Length)));
    }

    private static WorkerCallableManifestEntry WithClaims(WorkerCallableManifestEntry entry, ImmutableArray<WorkerClaimManifestEntry> claims)
    {
        entry = RepositoryParameterGuard.NotNull(entry, nameof(entry));
        RepositoryParameterGuard.RequireInitialized(claims.IsDefault, nameof(claims), "Array must be initialized.");
        return entry with
        {
            ClaimIds = [.. entry.ClaimIds, .. claims.Select(static claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.ClaimId)],
            SelectedFeatures = entry.SelectedFeatures.Contains(WorkerSelectedFeature.Contracts) ? entry.SelectedFeatures
                : entry.SelectedFeatures.Add(WorkerSelectedFeature.Contracts),
            SelectionReasons = entry.SelectionReasons.Contains(WorkerSelectionReason.DiscoveredCallPrecondition) ? entry.SelectionReasons
                : entry.SelectionReasons.Add(WorkerSelectionReason.DiscoveredCallPrecondition)
        };
    }
}
