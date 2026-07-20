namespace SharpProof;

    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SharpProofCodeFixProvider))]
    [Shared]
    public sealed partial class SharpProofCodeFixProvider : CodeFixProvider {
        private static readonly ImmutableArray<string> AllFixableDiagnosticIds = new[]
            { 2, 3, 5, 7, 8, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 4, 6, 29, 34, 35, 36, 37, 38, 39, 46, 45 }
            .Select(static number => $"SP{number:0000}")
            .ToImmutableArray();

        public override ImmutableArray<string> FixableDiagnosticIds => AllFixableDiagnosticIds;

        public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
            var diagnostic = context.Diagnostics[0];
            var document = context.Document;
            var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null)
                return;
            var configuration = AnalyzerConfiguration.FromOptions(document.Project.AnalyzerOptions);
            var attributePolicy = SharpProofAttributeIdentityPolicy.Create(configuration.AttributeStubNamespaces);

            if (TryGetSimpleRemoval(diagnostic.Id, out var removal)) {
                RegisterSimpleRemovalCodeFix(context, document, root, diagnostic, attributePolicy, removal);
                return;
            }

            if (!int.TryParse(diagnostic.Id.Substring(2), out var diagnosticNumber)) return;
            switch (diagnosticNumber) {
                case 4:
                    RegisterAddPurityCodeFix(context, document, root, diagnostic, attributePolicy);
                    break;
                case 6:
                    RegisterSynchronizationCodeFix(context, document, root, diagnostic, attributePolicy);
                    break;
                case 29:
                    RegisterMisplacedRequiresCodeFix(context, document, root, diagnostic);
                    break;
                case (>= 34 and <= 39) or 46:
                    RegisterInferredContractCodeFix(context, document, root, diagnostic);
                    break;
                case 45:
                    RegisterNullForgivingCodeFix(context, document, root, diagnostic);
                    break;
            }
        }

        private static bool TryGetSimpleRemoval(string diagnosticId, out SimpleRemovalRegistration registration) {
            registration = diagnosticId switch {
                "SP0002" => new(diagnosticId, "Remove [EnforcePure] and [Pure] attributes", SimpleRemovalOperation.DeclarationAndAccessors, "RemoveAttributesMatchingAsyncSP0002", "EnforcePureAttribute", "PureAttribute"),
                "SP0003" => new(diagnosticId, "Remove misplaced purity attribute", SimpleRemovalOperation.MisplacedAttribute, "RemoveMisplacedAttributeAsync"),
                "SP0005" => new(diagnosticId, "Remove conflicting purity boundary attributes", SimpleRemovalOperation.DeclarationAndAccessors, "RemoveAttributesMatchingAsyncSP0005", "PureAttribute", "PureExternalAttribute", "ImpureAttribute"),
                "SP0007" => new(diagnosticId, "Remove misplaced [AllowSynchronization] attribute", SimpleRemovalOperation.MisplacedAttribute, "RemoveMisplacedAttributeAsyncSP0007"),
                "SP0008" => new(diagnosticId, "Remove [AllowSynchronization] attribute", SimpleRemovalOperation.DeclarationAndAccessors, "RemoveAttributesMatchingAsyncSP0008", "AllowSynchronizationAttribute"),
                "SP0013" => new(diagnosticId, "Remove [ZeroAllocations] attribute", SimpleRemovalOperation.DiagnosticContract, "RemoveContractAttributeAsyncSP0013", "ZeroAllocationsAttribute"),
                "SP0014" => new(diagnosticId, "Remove misplaced [ZeroAllocations] attribute", SimpleRemovalOperation.MisplacedAttribute, "RemoveMisplacedAttributeAsyncSP0014"),
                "SP0015" => new(diagnosticId, "Remove [AllowedCapabilities] attribute", SimpleRemovalOperation.DiagnosticContract, "RemoveContractAttributeAsyncSP0015", "AllowedCapabilitiesAttribute"),
                "SP0016" => new(diagnosticId, "Remove [AllowedCapabilities] attribute", SimpleRemovalOperation.DiagnosticContract, "RemoveContractAttributeAsyncSP0016", "AllowedCapabilitiesAttribute"),
                "SP0017" => new(diagnosticId, "Remove misplaced [AllowedCapabilities] attribute", SimpleRemovalOperation.MisplacedAttribute, "RemoveMisplacedAttributeAsyncSP0017"),
                "SP0018" => new(diagnosticId, "Remove [Ensures] attribute", SimpleRemovalOperation.DiagnosticContract, "RemoveContractAttributeAsyncSP0018", "EnsuresAttribute"),
                "SP0019" => new(diagnosticId, "Remove [Ensures] attribute", SimpleRemovalOperation.DiagnosticContract, "RemoveContractAttributeAsyncSP0019", "EnsuresAttribute"),
                "SP0020" => new(diagnosticId, "Remove misplaced [Ensures] attribute", SimpleRemovalOperation.MisplacedAttribute, "RemoveMisplacedAttributeAsyncSP0020"),
                "SP0021" => new(diagnosticId, "Remove [ExpectedComplexity] attribute", SimpleRemovalOperation.DiagnosticContract, "RemoveContractAttributeAsyncSP0021", "ExpectedComplexityAttribute"),
                "SP0022" => new(diagnosticId, "Remove [ExpectedComplexity] attribute", SimpleRemovalOperation.DiagnosticContract, "RemoveContractAttributeAsyncSP0022", "ExpectedComplexityAttribute"),
                "SP0023" => new(diagnosticId, "Remove misplaced [ExpectedComplexity] attribute", SimpleRemovalOperation.MisplacedAttribute, "RemoveMisplacedAttributeAsyncSP0023"),
                _ => null!
            };
            return registration != null;
        }

        private enum SimpleRemovalOperation {
            MisplacedAttribute,
            DeclarationAndAccessors,
            DiagnosticContract
        }

        private sealed record SimpleRemovalRegistration(
            string DiagnosticId,
            string Title,
            SimpleRemovalOperation Operation,
            string EquivalenceKey,
            params string[] AttributeTypeNames);

        private void RegisterAddPurityCodeFix(
            CodeFixContext context, Document document, SyntaxNode root, Diagnostic diagnostic,
            SharpProofAttributeIdentityPolicy attributePolicy) {
            if (!TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declaration) ||
                declaration is PropertyDeclarationSyntax or IndexerDeclarationSyntax)
                return;

            Register(context, diagnostic, "Add [EnforcePure] attribute",
                cancellationToken => AddEnforcePureAttributeAsync(
                    document, root, declaration, attributePolicy, cancellationToken),
                "AddEnforcePureAttributeAsync");
        }

        private void RegisterSynchronizationCodeFix(
            CodeFixContext context, Document document, SyntaxNode root, Diagnostic diagnostic,
            SharpProofAttributeIdentityPolicy attributePolicy) {
            if (!TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declaration))
                return;

            Register(context, diagnostic, "Add [EnforcePure] attribute",
                cancellationToken => AddEnforcePureAttributeAsync(
                    document, root, declaration, attributePolicy, cancellationToken),
                "AddEnforcePureAttributeAsyncSP0006a");
            Register(context, diagnostic, "Remove [AllowSynchronization] attribute",
                cancellationToken => RemoveAttributesMatchingAsync(
                    document, root, declaration,
                    AcceptedAttribute(attributePolicy, "AllowSynchronizationAttribute"), cancellationToken),
                "RemoveAttributesMatchingAsyncSP0006b");
        }

        private static void RegisterMisplacedRequiresCodeFix(
            CodeFixContext context, Document document, SyntaxNode root, Diagnostic diagnostic) {
            if (!TryFindAttributeSyntax(root, diagnostic.Location.SourceSpan, out var attribute)) return;

            if (CanMoveAttributeToGetter(attribute))
                Register(context, diagnostic, "Move [Requires] attribute to getter",
                    cancellationToken => MoveAttributeToGetterAsync(document, root, attribute, cancellationToken),
                    "MoveAttributeToGetterAsyncSP0029");

            Register(context, diagnostic, "Remove misplaced [Requires] attribute",
                cancellationToken => RemoveMisplacedAttributeAsync(document, root, attribute, cancellationToken),
                "RemoveMisplacedAttributeAsyncSP0029");
        }

        private static void RegisterNullForgivingCodeFix(
            CodeFixContext context, Document document, SyntaxNode root, Diagnostic diagnostic) {
            if (!TryFindNullForgivingExpression(root, diagnostic.Location.SourceSpan, out var suppression)) return;

            Register(context, diagnostic, "Remove unnecessary null-forgiving operator",
                _ => Task.FromResult(document.WithSyntaxRoot(
                    root.ReplaceNode(suppression, RemoveNullForgivingOperator(suppression)))),
                "RemoveUnnecessaryNullForgivingOperator");
        }

        private static void Register(
            CodeFixContext context, Diagnostic diagnostic, string title,
            Func<CancellationToken, Task<Document>> createChangedDocument,
            string equivalenceKey) =>
            context.RegisterCodeFix(
                CodeAction.Create(title, createChangedDocument, equivalenceKey),
                diagnostic);

        internal static bool TryFindNullForgivingExpression(
            SyntaxNode root,
            TextSpan span,
            out PostfixUnaryExpressionSyntax suppression) {
            for (var node = root.FindToken(span.Start).Parent; node != null; node = node.Parent)
                if (node is PostfixUnaryExpressionSyntax postfix &&
                    postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression)) {
                    suppression = postfix;
                    return true;
                }

            suppression = null!;
            return false;
        }

        internal static ExpressionSyntax RemoveNullForgivingOperator(PostfixUnaryExpressionSyntax suppression) {
            var operand = suppression.Operand;
            return operand.WithTrailingTrivia(
                operand.GetTrailingTrivia().AddRange(suppression.GetTrailingTrivia()));
        }

}
