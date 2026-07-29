namespace SharpProof.CompilerArtifact;

internal static class CompilerManifestArtifactProducer
{
    internal static CompilerManifestArtifact Create(CSharpCompilation compilation, string projectDirectory,
        string targetFramework, WorkerFeatureSet features, ClaimManifestBuildResult discovery,
        int maximumExpressionDepth, CancellationToken cancellationToken,
        ImmutableArray<AdditionalText> additionalFiles = default)
    {
        var snapshot = CompilerCompilationCapture.Capture(
            compilation, projectDirectory, targetFramework, additionalFiles, cancellationToken);
        var diagnostics = compilation.GetDiagnostics(cancellationToken)
            .Where(static item => item.Severity == DiagnosticSeverity.Error)
            .OrderBy(static item => item.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(static item => item.Location.SourceSpan.Start)
            .ThenBy(static item => item.Id, StringComparer.Ordinal)
            .Select(CreateDiagnostic).ToArray();
        var targets = discovery.Targets.Values.OrderBy(static item => item.Entry.CallableId, StringComparer.Ordinal);
        CompilerCallableArtifact[] callables;
        if (diagnostics.Length != 0)
        {
            callables = [.. targets.Select(static item => new CompilerCallableArtifact {
                CallableId = item.Entry.CallableId, FailureReason = WorkerClaimReason.UnsupportedCallable,
                EffectClaims = [.. item.EffectClaims.Select(static claim => claim.Evidence)]
            })];
        }
        else
        {
            var lowerer = new CompilerCallableLowerer(compilation, new IrFactory());
            callables = [.. targets.Select(item => {
                var artifact = CompilerLoweredArtifact.Encode(
                    lowerer.Prepare(item, cancellationToken));
                artifact.EffectClaims = [.. item.EffectClaims.Select(
                    static claim => claim.Evidence)];
                return artifact;
            })];
        }
        var artifact = new CompilerManifestArtifact
        {
            Features = features,
            CompilationSha256 = CompilationFingerprint.ComputeSha256(snapshot),
            Compilation = snapshot,
            Manifest = discovery.Manifest,
            MaximumExpressionDepth = maximumExpressionDepth,
            CompilerDiagnostics = diagnostics,
            Callables = callables
        };
        CompilerManifestArtifactJson.Validate(artifact);
        return artifact;
    }
    private static CompilerDiagnosticArtifact CreateDiagnostic(Diagnostic diagnostic)
    {
        var source = diagnostic.Location.IsInSource;
        var span = source ? diagnostic.Location.GetMappedLineSpan() : default;
        return new CompilerDiagnosticArtifact
        {
            Code = "compiler." + diagnostic.Id,
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            Location = new WorkerSourceLocation
            {
                Path = span.Path ?? string.Empty,
                Start = source ? diagnostic.Location.SourceSpan.Start : 0,
                Length = source ? diagnostic.Location.SourceSpan.Length : 0,
                Line = source ? span.StartLinePosition.Line : 0,
                Column = source ? span.StartLinePosition.Character : 0
            }
        };
    }
}
