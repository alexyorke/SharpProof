using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;

namespace SharpProof
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SharpProofCodeFixProvider))]
    [Shared]
    public sealed class SharpProofCodeFixProvider : CodeFixProvider
    {
        private static readonly ImmutableArray<string> AllFixableDiagnosticIds = new[]
            { 2, 3, 5, 7, 8, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 4, 6, 29, 34, 35, 36, 37, 38, 39, 46, 45 }
            .Select(static number => $"SP{number:0000}")
            .ToImmutableArray();

        public override ImmutableArray<string> FixableDiagnosticIds => AllFixableDiagnosticIds;

        public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics[0];
            var document = context.Document;
            var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null)
                return;
            var configuration = AnalyzerConfiguration.FromOptions(document.Project.AnalyzerOptions);
            var attributePolicy = SharpProofAttributeIdentityPolicy.Create(configuration.AttributeStubNamespaces);

            if (TryGetSimpleRemoval(diagnostic.Id, out var removal))
            {
                RegisterSimpleRemovalCodeFix(context, document, root, diagnostic, attributePolicy, removal);
                return;
            }

            if (!int.TryParse(diagnostic.Id.Substring(2), out var diagnosticNumber)) return;
            switch (diagnosticNumber)
            {
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

        private static bool TryGetSimpleRemoval(string diagnosticId, out SimpleRemovalRegistration registration)
        {
            registration = diagnosticId switch
            {
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

        private enum SimpleRemovalOperation
        {
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
            SharpProofAttributeIdentityPolicy attributePolicy)
        {
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
            SharpProofAttributeIdentityPolicy attributePolicy)
        {
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
            CodeFixContext context, Document document, SyntaxNode root, Diagnostic diagnostic)
        {
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
            CodeFixContext context, Document document, SyntaxNode root, Diagnostic diagnostic)
        {
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
            out PostfixUnaryExpressionSyntax suppression)
        {
            for (var node = root.FindToken(span.Start).Parent; node != null; node = node.Parent)
                if (node is PostfixUnaryExpressionSyntax postfix &&
                    postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    suppression = postfix;
                    return true;
                }

            suppression = null!;
            return false;
        }

        internal static ExpressionSyntax RemoveNullForgivingOperator(PostfixUnaryExpressionSyntax suppression)
        {
            var operand = suppression.Operand;
            return operand.WithTrailingTrivia(
                operand.GetTrailingTrivia().AddRange(suppression.GetTrailingTrivia()));
        }

        internal void RegisterInferredContractCodeFix(
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic)
        {
            if (!diagnostic.Properties.TryGetValue(
                    SharpProofDiagnostics.SuggestedContractAttributeProperty,
                out var attributeExpression) ||
                attributeExpression == null ||
                string.IsNullOrWhiteSpace(attributeExpression) ||
                (!attributeExpression.StartsWith("global::SharpProof.Attributes.", StringComparison.Ordinal) &&
                 !attributeExpression.StartsWith(
                     "global::System.Diagnostics.CodeAnalysis.",
                     StringComparison.Ordinal)) ||
                !TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declaration) ||
                declaration is PropertyDeclarationSyntax or IndexerDeclarationSyntax)
                return;

            diagnostic.Properties.TryGetValue(
                SharpProofDiagnostics.SuggestedContractKindProperty,
                out var contractKind);
            var title = "Add inferred " +
                        (string.IsNullOrWhiteSpace(contractKind) ? "SharpProof" : contractKind) +
                        " contract";
            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    cancellationToken => contractKind != null &&
                                         contractKind.StartsWith("nullable-", StringComparison.Ordinal)
                        ? AddInferredNullableContractAttributeAsync(
                            document,
                            root,
                            declaration,
                            attributeExpression,
                            contractKind,
                            cancellationToken)
                        : AddInferredContractAttributeAsync(
                            document,
                            root,
                            declaration,
                            attributeExpression,
                            cancellationToken),
                    nameof(AddInferredContractAttributeAsync) + diagnostic.Id),
                diagnostic);
        }

        private static async Task<Document> AddInferredNullableContractAttributeAsync(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            string attributeExpression,
            string contractKind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (contractKind == "nullable-return")
            {
                var parsed = SyntaxFactory.ParseCompilationUnit(
                    "class __SharpProofPlaceholder { [return: " + attributeExpression +
                    "] object M() => null; }");
                var list = parsed.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault()?.AttributeLists.FirstOrDefault();
                if (list == null || list.ContainsDiagnostics) return document;

                var lineEnding = await GetLineEndingAsync(document, cancellationToken).ConfigureAwait(false);
                return InsertFormattedAttributeList(
                    document,
                    root,
                    declaration,
                    list.WithoutTrivia(),
                    lineEnding);
            }

            const string parameterPrefix = "nullable-parameter:";
            if (!contractKind.StartsWith(parameterPrefix, StringComparison.Ordinal))
                return document;

            var parameterName = contractKind.Substring(parameterPrefix.Length);
            var parameter = declaration.DescendantNodes()
                .OfType<ParameterSyntax>()
                .FirstOrDefault(candidate => candidate.Identifier.ValueText == parameterName);
            if (parameter == null)
            {
                if (parameterName != "value" ||
                    declaration is not AccessorDeclarationSyntax accessor ||
                    !accessor.IsKind(SyntaxKind.SetAccessorDeclaration) &&
                    !accessor.IsKind(SyntaxKind.InitAccessorDeclaration))
                    return document;

                var accessorUnit = SyntaxFactory.ParseCompilationUnit(
                    "class __SharpProofPlaceholder { object P { [param: " + attributeExpression +
                    "] set { } } }");
                var accessorAttributeList = accessorUnit.DescendantNodes()
                    .OfType<AccessorDeclarationSyntax>()
                    .FirstOrDefault()?.AttributeLists.FirstOrDefault();
                if (accessorAttributeList == null || accessorAttributeList.ContainsDiagnostics)
                    return document;

                var lineEnding = await GetLineEndingAsync(document, cancellationToken).ConfigureAwait(false);
                return InsertFormattedAttributeList(
                    document,
                    root,
                    accessor,
                    accessorAttributeList.WithoutTrivia(),
                    lineEnding);
            }

            var parameterUnit = SyntaxFactory.ParseCompilationUnit(
                "class __SharpProofPlaceholder { void M([" + attributeExpression + "] object value) { } }");
            var attributeList = parameterUnit.DescendantNodes().OfType<ParameterSyntax>()
                .FirstOrDefault()?.AttributeLists.FirstOrDefault();
            if (attributeList == null || attributeList.ContainsDiagnostics) return document;

            var updatedParameter = parameter.AddAttributeLists(
                attributeList.WithoutTrivia().WithTrailingTrivia(SyntaxFactory.Space));
            return document.WithSyntaxRoot(root.ReplaceNode(parameter, updatedParameter));
        }

        private void RegisterSimpleRemovalCodeFix(
            CodeFixContext context,
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            SharpProofAttributeIdentityPolicy attributePolicy,
            SimpleRemovalRegistration registration)
        {
            if (registration.Operation == SimpleRemovalOperation.MisplacedAttribute)
            {
                if (!TryFindAttributeSyntax(root, diagnostic.Location.SourceSpan, out var misplacedAttribute)) return;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        registration.Title,
                        c => RemoveMisplacedAttributeAsync(document, root, misplacedAttribute, c),
                        registration.EquivalenceKey),
                    diagnostic);
                return;
            }

            if (!TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declaration)) return;

            var shouldRemoveType = AcceptedAttribute(attributePolicy, registration.AttributeTypeNames);
            Func<CancellationToken, Task<Document>> apply = registration.Operation ==
                SimpleRemovalOperation.DeclarationAndAccessors
                ? c => RemoveAttributesMatchingAsync(document, root, declaration, shouldRemoveType, c)
                : c => RemoveContractAttributeAsync(
                    document, root, diagnostic, declaration, shouldRemoveType, c);
            context.RegisterCodeFix(
                CodeAction.Create(registration.Title, apply, registration.EquivalenceKey),
                diagnostic);
        }

        internal static bool TryFindPurityTargetDeclaration(SyntaxNode root, int position, out SyntaxNode declaration)
        {
            declaration = null!;
            for (var node = root.FindToken(position).Parent; node != null; node = node.Parent)
                if (node is MethodDeclarationSyntax or ConstructorDeclarationSyntax or OperatorDeclarationSyntax or
                    ConversionOperatorDeclarationSyntax or IndexerDeclarationSyntax or PropertyDeclarationSyntax or
                    AccessorDeclarationSyntax or LocalFunctionStatementSyntax)
                {
                    declaration = node;
                    return true;
                }

            return false;
        }

        internal static bool TryFindAttributeSyntax(SyntaxNode root, TextSpan span, out AttributeSyntax attribute)
        {
            var node = root.FindNode(span, false, true);
            attribute = node.FirstAncestorOrSelf<AttributeSyntax>() ?? (node as AttributeSyntax)!;
            return attribute != null;
        }

        private static SyntaxNode? GetHostForAttribute(AttributeSyntax attr) =>
            (attr.Parent as AttributeListSyntax)?.Parent;

        internal static bool CanMoveAttributeToGetter(AttributeSyntax attribute) =>
            AttributeTargetSyntaxFacts.IsGetterAliasTarget(GetHostForAttribute(attribute));

        private static SyntaxNode RemoveAttributeFromHost(
            SyntaxNode host,
            AttributeSyntax attrToRemove,
            bool preserveLeadingTrivia = true)
        {
            if (attrToRemove.Parent is not AttributeListSyntax list) return host;

            var nodeToRemove = list.Attributes.Count == 1
                ? (SyntaxNode)list
                : attrToRemove;
            var options = preserveLeadingTrivia && HasSignificantTrivia(nodeToRemove.GetLeadingTrivia())
                ? SyntaxRemoveOptions.KeepLeadingTrivia
                : SyntaxRemoveOptions.KeepNoTrivia;
            return host.RemoveNode(nodeToRemove, options) ?? host;
        }

        private static bool HasSignificantTrivia(SyntaxTriviaList trivia)
        {
            return trivia.Any(static item =>
                !item.IsKind(SyntaxKind.WhitespaceTrivia) &&
                !item.IsKind(SyntaxKind.EndOfLineTrivia));
        }

        private static SyntaxList<AttributeListSyntax> GetAttributeLists(SyntaxNode host)
        {
            return host switch
            {
                AccessorDeclarationSyntax a => a.AttributeLists,
                MemberDeclarationSyntax m => m.AttributeLists,
                ParameterSyntax p => p.AttributeLists,
                CompilationUnitSyntax u => u.AttributeLists,
                LocalFunctionStatementSyntax l => l.AttributeLists,
                _ => default
            };
        }

        private static SyntaxNode WithAttributeLists(SyntaxNode host, SyntaxList<AttributeListSyntax> lists)
        {
            return host switch
            {
                AccessorDeclarationSyntax a => a.WithAttributeLists(lists),
                MemberDeclarationSyntax m => m.WithAttributeLists(lists),
                LocalFunctionStatementSyntax l => l.WithAttributeLists(lists),
                ParameterSyntax p => p.WithAttributeLists(lists),
                CompilationUnitSyntax u => u.WithAttributeLists(lists),
                _ => host
            };
        }

        private static SyntaxNode RemoveAttributesMatchingFromDeclarationAndAccessors(
            SyntaxNode declaration,
            SemanticModel model,
            Func<INamedTypeSymbol?, bool> shouldRemoveType,
            out bool removedAny)
        {
            var nodesToRemove = new List<SyntaxNode>();
            var hosts = new List<SyntaxNode> { declaration };
            if (declaration is BasePropertyDeclarationSyntax { AccessorList: { } accessorList })
                hosts.AddRange(accessorList.Accessors);

            foreach (var host in hosts)
            foreach (var list in GetAttributeLists(host))
            {
                var matching = list.Attributes
                    .Where(attribute => shouldRemoveType(GetAttributeClass(model, attribute)))
                    .ToArray();
                if (matching.Length == 0) continue;

                if (matching.Length == list.Attributes.Count)
                    nodesToRemove.Add(list);
                else
                    nodesToRemove.AddRange(matching);
            }

            removedAny = nodesToRemove.Count != 0;
            if (!removedAny) return declaration;

            var trackedDeclaration = declaration.TrackNodes(nodesToRemove);
            foreach (var original in nodesToRemove)
            {
                var current = trackedDeclaration.GetCurrentNode(original);
                if (current == null) continue;

                var options = HasSignificantTrivia(current.GetLeadingTrivia())
                    ? SyntaxRemoveOptions.KeepLeadingTrivia
                    : SyntaxRemoveOptions.KeepNoTrivia;
                trackedDeclaration = trackedDeclaration.RemoveNode(current, options) ?? trackedDeclaration;
            }

            return trackedDeclaration;
        }

        private static INamedTypeSymbol? GetAttributeClass(SemanticModel model, AttributeSyntax attributeSyntax) =>
            model.GetSymbolInfo(attributeSyntax).Symbol switch
            {
                IMethodSymbol { MethodKind: MethodKind.Constructor } constructor => constructor.ContainingType,
                INamedTypeSymbol type => type,
                _ => null
            };

        internal static Func<INamedTypeSymbol?, bool> AcceptedAttribute(
            SharpProofAttributeIdentityPolicy policy,
            params string[] attributeTypeNames) =>
            type => attributeTypeNames.Any(name => policy.IsAccepted(type, name));

        internal static Task<Document> RemoveMisplacedAttributeAsync(Document document, SyntaxNode root, AttributeSyntax attr,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var host = GetHostForAttribute(attr);
            if (host == null)
                return Task.FromResult(document);
            var newHost = RemoveAttributeFromHost(host, attr);
            if (ReferenceEquals(host, newHost))
                return Task.FromResult(document);
            var newRoot = root.ReplaceNode(host, newHost);
            return Task.FromResult(document.WithSyntaxRoot(newRoot));
        }

        internal static async Task<Document> MoveAttributeToGetterAsync(
            Document document,
            SyntaxNode root,
            AttributeSyntax attribute,
            CancellationToken cancellationToken)
        {
            var host = GetHostForAttribute(attribute);
            if (host is not PropertyDeclarationSyntax && host is not IndexerDeclarationSyntax) return document;

            var hostWithoutAttribute = RemoveAttributeFromHost(host, attribute, preserveLeadingTrivia: false);
            var sourceAttributeList = (AttributeListSyntax)attribute.Parent!;
            var attributeList = sourceAttributeList.Attributes.Count == 1
                ? sourceAttributeList.WithAttributes(SyntaxFactory.SingletonSeparatedList(attribute))
                : SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));
            attributeList = attributeList
                .WithTarget(null)
                .WithAdditionalAnnotations(Formatter.Annotation);
            var updatedHost = AddAttributeToGetter(hostWithoutAttribute, attributeList);
            if (updatedHost == null) return document;

            return await Formatter.FormatAsync(
                    document.WithSyntaxRoot(root.ReplaceNode(host, updatedHost)),
                    Formatter.Annotation,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private static SyntaxNode? AddAttributeToGetter(
            SyntaxNode host,
            AttributeListSyntax attributeList)
        {
            return host switch
            {
                PropertyDeclarationSyntax property => AddAttributeToGetter(
                    property,
                    property.AccessorList,
                    property.ExpressionBody,
                    property.SemicolonToken,
                    attributeList,
                    static (declaration, accessorList) => declaration.WithAccessorList(accessorList),
                    static declaration => declaration
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)),
                IndexerDeclarationSyntax indexer => AddAttributeToGetter(
                    indexer,
                    indexer.AccessorList,
                    indexer.ExpressionBody,
                    indexer.SemicolonToken,
                    attributeList,
                    static (declaration, accessorList) => declaration.WithAccessorList(accessorList),
                    static declaration => declaration
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)),
                _ => null
            };
        }

        private static TDeclaration? AddAttributeToGetter<TDeclaration>(
            TDeclaration declaration,
            AccessorListSyntax? accessorList,
            ArrowExpressionClauseSyntax? expressionBody,
            SyntaxToken semicolonToken,
            AttributeListSyntax attributeList,
            Func<TDeclaration, AccessorListSyntax, TDeclaration> withAccessorList,
            Func<TDeclaration, TDeclaration> withoutExpressionBody)
            where TDeclaration : SyntaxNode
        {
            if (accessorList != null)
            {
                var getter = accessorList.Accessors.FirstOrDefault(static accessor =>
                    accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                if (getter == null) return null;

                var updatedGetter = getter.WithAttributeLists(
                        getter.AttributeLists.Insert(0, attributeList))
                    .WithAdditionalAnnotations(Formatter.Annotation);
                return withAccessorList(
                    declaration,
                    accessorList.WithAccessors(accessorList.Accessors.Replace(getter, updatedGetter)))
                    .WithAdditionalAnnotations(Formatter.Annotation);
            }

            if (expressionBody == null) return null;

            var expressionGetter = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                .WithAttributeLists(SyntaxFactory.SingletonList(attributeList))
                .WithExpressionBody(expressionBody)
                .WithSemicolonToken(semicolonToken)
                .WithAdditionalAnnotations(Formatter.Annotation);
            var updatedDeclaration = withoutExpressionBody(declaration)
                .WithAdditionalAnnotations(Formatter.Annotation);
            return withAccessorList(
                updatedDeclaration,
                SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(expressionGetter))
                    .WithOpenBraceToken(
                        SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                            .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
                            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed))
                    .WithAdditionalAnnotations(Formatter.Annotation));
        }

        private async Task<Document> RemoveContractAttributeAsync(
            Document document,
            SyntaxNode root,
            Diagnostic diagnostic,
            SyntaxNode declaration,
            Func<INamedTypeSymbol?, bool> shouldRemoveType,
            CancellationToken cancellationToken)
        {
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model != null)
                foreach (var location in new[] { diagnostic.Location }.Concat(diagnostic.AdditionalLocations))
                {
                    if (!location.IsInSource) continue;

                    if (TryFindAttributeSyntax(root, location.SourceSpan, out var attribute) &&
                        shouldRemoveType(GetAttributeClass(model, attribute)))
                        return await RemoveMisplacedAttributeAsync(document, root, attribute, cancellationToken)
                            .ConfigureAwait(false);
                }

            return await RemoveAttributesMatchingAsync(document, root, declaration, shouldRemoveType, cancellationToken)
                .ConfigureAwait(false);
        }

        internal async Task<Document> RemoveAttributesMatchingAsync(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            Func<INamedTypeSymbol?, bool> shouldRemoveType,
            CancellationToken cancellationToken)
        {
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model == null)
                return document;
            var newDecl = RemoveAttributesMatchingFromDeclarationAndAccessors(
                declaration,
                model,
                shouldRemoveType,
                out var removedAny);
            if (!removedAny) return document;

            var newRoot = root.ReplaceNode(declaration, newDecl);
            return document.WithSyntaxRoot(newRoot);
        }

        internal async Task<Document> AddEnforcePureAttributeAsync(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            SharpProofAttributeIdentityPolicy attributePolicy,
            CancellationToken cancellationToken)
        {
            var lists = GetAttributeLists(declaration);
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (model != null)
                foreach (var list in lists)
                    foreach (var attr in list.Attributes)
                    {
                        var c = GetAttributeClass(model, attr);
                        if (attributePolicy.IsAccepted(c, "EnforcePureAttribute"))
                            return document;
                    }

            var officialAttribute = model?.Compilation.GetTypeByMetadataName(
                "SharpProof.Attributes.EnforcePureAttribute");
            var useShortName = model != null &&
                               officialAttribute != null &&
                               HasUnaliasedSharpProofAttributesUsing(declaration) &&
                               IsUnambiguousAttributeName(
                                   model,
                                   declaration.SpanStart,
                                   "EnforcePure",
                                   officialAttribute);
            var attributeName = useShortName
                ? "EnforcePure"
                : "global::SharpProof.Attributes.EnforcePure";
            var lineEnding = await GetLineEndingAsync(document, cancellationToken).ConfigureAwait(false);
            var newAttrList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.ParseName(attributeName))));

            return InsertFormattedAttributeList(document, root, declaration, newAttrList, lineEnding);
        }

        private async Task<Document> AddInferredContractAttributeAsync(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            string attributeExpression,
            CancellationToken cancellationToken)
        {
            const string attributeNamespace = "global::SharpProof.Attributes.";
            if (string.IsNullOrWhiteSpace(attributeExpression) ||
                !attributeExpression.StartsWith(attributeNamespace, StringComparison.Ordinal) ||
                attributeExpression.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                return document;

            var useShortName = HasUnaliasedSharpProofAttributesUsing(declaration);

            var parsedUnit = SyntaxFactory.ParseCompilationUnit(
                "[" + attributeExpression + "] class __SharpProofAttributePlaceholder { }");
            var newAttributeList = parsedUnit.Members
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault()?
                .AttributeLists
                .FirstOrDefault();
            if (newAttributeList == null ||
                newAttributeList.Attributes.Count != 1 ||
                newAttributeList.ContainsDiagnostics)
                return document;

            if (useShortName)
                newAttributeList = ShortenSharpProofAttributeNames(newAttributeList, attributeNamespace);

            var lineEnding = await GetLineEndingAsync(document, cancellationToken).ConfigureAwait(false);
            return InsertFormattedAttributeList(
                document,
                root,
                declaration,
                newAttributeList.WithoutTrivia(),
                lineEnding);
        }

        private static Document InsertFormattedAttributeList(
            Document document,
            SyntaxNode root,
            SyntaxNode declaration,
            AttributeListSyntax attributeList,
            string lineEnding)
        {
            var originalDeclaration = declaration;
            (declaration, attributeList) = FormatInsertedAttribute(declaration, attributeList, lineEnding);
            var updatedDeclaration = WithAttributeLists(
                declaration,
                GetAttributeLists(declaration).Insert(0, attributeList));
            return document.WithSyntaxRoot(root.ReplaceNode(originalDeclaration, updatedDeclaration));
        }

        private static async Task<string> GetLineEndingAsync(
            Document document,
            CancellationToken cancellationToken)
        {
            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            return sourceText.ToString().IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
        }

        private static (SyntaxNode Declaration, AttributeListSyntax AttributeList) FormatInsertedAttribute(
            SyntaxNode declaration,
            AttributeListSyntax attributeList,
            string lineEnding)
        {
            var leadingTrivia = declaration.GetLeadingTrivia();
            var indentation = SyntaxFactory.TriviaList(
                leadingTrivia
                    .Reverse()
                    .TakeWhile(static trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                    .Reverse());
            var trailingTrivia = SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(lineEnding)).AddRange(indentation);
            return (
                declaration.WithLeadingTrivia(default(SyntaxTriviaList)),
                attributeList
                    .WithLeadingTrivia(leadingTrivia)
                    .WithTrailingTrivia(trailingTrivia));
        }

        private static bool HasUnaliasedSharpProofAttributesUsing(SyntaxNode declaration) =>
            declaration.AncestorsAndSelf()
                .SelectMany(static ancestor => ancestor switch
                {
                    CompilationUnitSyntax compilationUnit => compilationUnit.Usings,
                    BaseNamespaceDeclarationSyntax namespaceDeclaration => namespaceDeclaration.Usings,
                    _ => default
                })
                .Any(static directive =>
                    directive.Alias == null &&
                    string.Equals(directive.Name?.ToString(), "SharpProof.Attributes", StringComparison.Ordinal));

        private static bool IsUnambiguousAttributeName(
            SemanticModel model,
            int position,
            string shortName,
            INamedTypeSymbol expectedType)
        {
            var candidates = model.LookupNamespacesAndTypes(position, name: shortName)
                .Concat(model.LookupNamespacesAndTypes(position, name: shortName + "Attribute"))
                .OfType<INamedTypeSymbol>()
                .ToArray();
            return candidates.Length != 0 && candidates.All(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, expectedType.OriginalDefinition));
        }

        private static AttributeListSyntax ShortenSharpProofAttributeNames(
            AttributeListSyntax attributeList,
            string attributeNamespace)
        {
            return (AttributeListSyntax)new SharpProofAttributeNameRewriter(attributeNamespace).Visit(attributeList)!;
        }

        private sealed class SharpProofAttributeNameRewriter : CSharpSyntaxRewriter
        {
            private readonly string _attributeNamespace;

            internal SharpProofAttributeNameRewriter(string attributeNamespace)
            {
                _attributeNamespace = attributeNamespace;
            }

            public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
            {
                return Shorten(node) ?? base.VisitQualifiedName(node);
            }

            public override SyntaxNode? VisitAliasQualifiedName(AliasQualifiedNameSyntax node)
            {
                return Shorten(node) ?? base.VisitAliasQualifiedName(node);
            }

            public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                var expressionText = node.Expression.ToString();
                if (expressionText.StartsWith(_attributeNamespace, StringComparison.Ordinal))
                    return node.WithExpression(SyntaxFactory.ParseExpression(
                            expressionText.Substring(_attributeNamespace.Length))
                        .WithTriviaFrom(node.Expression));

                return base.VisitMemberAccessExpression(node);
            }

            private NameSyntax? Shorten(NameSyntax node)
            {
                var text = node.ToString();
                return text.StartsWith(_attributeNamespace, StringComparison.Ordinal)
                    ? SyntaxFactory.ParseName(text.Substring(_attributeNamespace.Length)).WithTriviaFrom(node)
                    : null;
            }
        }
    }
}
