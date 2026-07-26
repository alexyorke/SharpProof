namespace SharpProof.Migration;

[ExportCodeFixProvider(
    LanguageNames.CSharp,
    Name = nameof(LegacyContractMigrationCodeFixProvider))]
[Shared]
public sealed class LegacyContractMigrationCodeFixProvider : CodeFixProvider {
    public const string EquivalenceKey = "SharpProof.MigrateLegacyContracts";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ["CS0618"];

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(
        CodeFixContext context) {
        var root = await context.Document.GetSyntaxRootAsync(
            context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;
        var attribute = root.FindNode(
                context.Span,
                getInnermostNodeForTie: true)
            .FirstAncestorOrSelf<AttributeSyntax>();
        if (attribute == null) return;
        var migrated = await LegacyContractMigration.TryMigrateAsync(
            context.Document,
            attribute,
            context.CancellationToken).ConfigureAwait(false);
        if (migrated == null) return;
        context.RegisterCodeFix(
            CodeAction.Create(
                "Migrate legacy SharpProof contracts",
                _ => Task.FromResult(migrated),
                EquivalenceKey),
            context.Diagnostics);
    }
}
