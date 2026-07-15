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
        private static readonly ImmutableDictionary<string, SimpleRemovalRegistration> SimpleRemovalRegistrations =
            ImmutableArray.Create(
                    new SimpleRemovalRegistration(SharpProofDiagnostics.PurityNotVerifiedId,
                        "Remove [EnforcePure] and [Pure] attributes", SimpleRemovalOperation.DeclarationAndAccessors,
                        nameof(RemoveAttributesMatchingAsync) + "SP0002", "EnforcePureAttribute", "PureAttribute"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.MisplacedAttributeId,
                        "Remove misplaced purity attribute", SimpleRemovalOperation.MisplacedAttribute,
                        nameof(RemoveMisplacedAttributeAsync)),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.ConflictingPurityAttributesId,
                        "Remove conflicting purity boundary attributes", SimpleRemovalOperation.DeclarationAndAccessors,
                        nameof(RemoveAttributesMatchingAsync) + "SP0005", "PureAttribute", "PureExternalAttribute",
                        "ImpureAttribute"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeId,
                        "Remove misplaced [AllowSynchronization] attribute", SimpleRemovalOperation.MisplacedAttribute,
                        nameof(RemoveMisplacedAttributeAsync) + "SP0007"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.RedundantAllowSynchronizationId,
                        "Remove [AllowSynchronization] attribute", SimpleRemovalOperation.DeclarationAndAccessors,
                        nameof(RemoveAttributesMatchingAsync) + "SP0008", "AllowSynchronizationAttribute"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.AllocationInZeroAllocationMethodId,
                        "Remove [ZeroAllocations] attribute", SimpleRemovalOperation.DiagnosticContract,
                        nameof(RemoveContractAttributeAsync) + "SP0013", "ZeroAllocationsAttribute"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.MisplacedZeroAllocationsAttributeId,
                        "Remove misplaced [ZeroAllocations] attribute", SimpleRemovalOperation.MisplacedAttribute,
                        nameof(RemoveMisplacedAttributeAsync) + "SP0014"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.CapabilityViolationId,
                        "Remove [AllowedCapabilities] attribute", SimpleRemovalOperation.DiagnosticContract,
                        nameof(RemoveContractAttributeAsync) + SharpProofDiagnostics.CapabilityViolationId,
                        "AllowedCapabilitiesAttribute"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.CapabilityUnknownId,
                        "Remove [AllowedCapabilities] attribute", SimpleRemovalOperation.DiagnosticContract,
                        nameof(RemoveContractAttributeAsync) + SharpProofDiagnostics.CapabilityUnknownId,
                        "AllowedCapabilitiesAttribute"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeId,
                        "Remove misplaced [AllowedCapabilities] attribute", SimpleRemovalOperation.MisplacedAttribute,
                        nameof(RemoveMisplacedAttributeAsync) + "SP0017"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.EnsuresNotProvenId,
                        "Remove [Ensures] attribute", SimpleRemovalOperation.DiagnosticContract,
                        nameof(RemoveContractAttributeAsync) + SharpProofDiagnostics.EnsuresNotProvenId,
                        "EnsuresAttribute"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.EnsuresUnsupportedId,
                        "Remove [Ensures] attribute", SimpleRemovalOperation.DiagnosticContract,
                        nameof(RemoveContractAttributeAsync) + SharpProofDiagnostics.EnsuresUnsupportedId,
                        "EnsuresAttribute"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.MisplacedEnsuresAttributeId,
                        "Remove misplaced [Ensures] attribute", SimpleRemovalOperation.MisplacedAttribute,
                        nameof(RemoveMisplacedAttributeAsync) + "SP0020"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.ComplexityExceededId,
                        "Remove [ExpectedComplexity] attribute", SimpleRemovalOperation.DiagnosticContract,
                        nameof(RemoveContractAttributeAsync) + SharpProofDiagnostics.ComplexityExceededId,
                        "ExpectedComplexityAttribute"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId,
                        "Remove [ExpectedComplexity] attribute", SimpleRemovalOperation.DiagnosticContract,
                        nameof(RemoveContractAttributeAsync) + SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId,
                        "ExpectedComplexityAttribute"),
                    new SimpleRemovalRegistration(SharpProofDiagnostics.MisplacedExpectedComplexityAttributeId,
                        "Remove misplaced [ExpectedComplexity] attribute", SimpleRemovalOperation.MisplacedAttribute,
                        nameof(RemoveMisplacedAttributeAsync) + "SP0023"))
                .ToImmutableDictionary(static registration => registration.DiagnosticId, StringComparer.Ordinal);

        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
            SharpProofDiagnostics.PurityNotVerifiedId,
            SharpProofDiagnostics.MisplacedAttributeId,
            SharpProofDiagnostics.MissingEnforcePureAttributeId,
            SharpProofDiagnostics.ConflictingPurityAttributesId,
            SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeId,
            SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeId,
            SharpProofDiagnostics.RedundantAllowSynchronizationId,
            SharpProofDiagnostics.AllocationInZeroAllocationMethodId,
            SharpProofDiagnostics.MisplacedZeroAllocationsAttributeId,
            SharpProofDiagnostics.CapabilityViolationId,
            SharpProofDiagnostics.CapabilityUnknownId,
            SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeId,
            SharpProofDiagnostics.EnsuresNotProvenId,
            SharpProofDiagnostics.EnsuresUnsupportedId,
            SharpProofDiagnostics.MisplacedEnsuresAttributeId,
            SharpProofDiagnostics.ComplexityExceededId,
            SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId,
            SharpProofDiagnostics.MisplacedExpectedComplexityAttributeId,
            SharpProofDiagnostics.MisplacedRequiresAttributeId,
            SharpProofDiagnostics.SuggestZeroAllocationsId,
            SharpProofDiagnostics.SuggestAllowedCapabilitiesId,
            SharpProofDiagnostics.SuggestExpectedComplexityId,
            SharpProofDiagnostics.SuggestExceptionContractId,
            SharpProofDiagnostics.SuggestEnsuresId,
            SharpProofDiagnostics.SuggestRequiresId,
            SharpProofDiagnostics.SuggestNullableContractId,
            SharpProofDiagnostics.UnnecessaryNullForgivingOperatorId);

        public override FixAllProvider? GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics[0];
            var document = context.Document;
            var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null)
                return;
            var configuration = AnalyzerConfiguration.FromOptions(document.Project.AnalyzerOptions);
            var attributePolicy = SharpProofAttributeIdentityPolicy.Create(configuration.AttributeStubNamespaces);

            if (SimpleRemovalRegistrations.TryGetValue(diagnostic.Id, out var simpleRemoval))
            {
                RegisterSimpleRemovalCodeFix(
                    context,
                    document,
                    root,
                    diagnostic,
                    attributePolicy,
                    simpleRemoval);
                return;
            }

            switch (diagnostic.Id)
            {
                case SharpProofDiagnostics.MissingEnforcePureAttributeId:
                    if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declMissing))
                    {
                        if (declMissing is PropertyDeclarationSyntax or IndexerDeclarationSyntax) break;

                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Add [EnforcePure] attribute",
                                c => AddEnforcePureAttributeAsync(document, root, declMissing, attributePolicy, c),
                                nameof(AddEnforcePureAttributeAsync)),
                            diagnostic);
                    }

                    break;

                case SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeId:
                    if (TryFindPurityTargetDeclaration(root, diagnostic.Location.SourceSpan.Start, out var declAllow))
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Add [EnforcePure] attribute",
                                c => AddEnforcePureAttributeAsync(document, root, declAllow, attributePolicy, c),
                                nameof(AddEnforcePureAttributeAsync) + "SP0006a"),
                            diagnostic);
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove [AllowSynchronization] attribute",
                                c => RemoveAttributesMatchingAsync(document, root, declAllow,
                                    AcceptedAttribute(attributePolicy, "AllowSynchronizationAttribute"), c),
                                nameof(RemoveAttributesMatchingAsync) + "SP0006b"),
                            diagnostic);
                    }

                    break;

                case SharpProofDiagnostics.MisplacedRequiresAttributeId:
                    if (TryFindAttributeSyntax(root, diagnostic.Location.SourceSpan, out var misplacedRequires))
                    {
                        if (CanMoveAttributeToGetter(misplacedRequires))
                            context.RegisterCodeFix(
                                CodeAction.Create(
                                    "Move [Requires] attribute to getter",
                                    c => MoveAttributeToGetterAsync(document, root, misplacedRequires, c),
                                    nameof(MoveAttributeToGetterAsync) + "SP0029"),
                                diagnostic);

                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove misplaced [Requires] attribute",
                                c => RemoveMisplacedAttributeAsync(document, root, misplacedRequires, c),
                                nameof(RemoveMisplacedAttributeAsync) + "SP0029"),
                            diagnostic);
                    }

                    break;

                case SharpProofDiagnostics.SuggestZeroAllocationsId:
                case SharpProofDiagnostics.SuggestAllowedCapabilitiesId:
                case SharpProofDiagnostics.SuggestExpectedComplexityId:
                case SharpProofDiagnostics.SuggestExceptionContractId:
                case SharpProofDiagnostics.SuggestEnsuresId:
                case SharpProofDiagnostics.SuggestRequiresId:
                case SharpProofDiagnostics.SuggestNullableContractId:
                    RegisterInferredContractCodeFix(context, document, root, diagnostic);
                    break;

                case SharpProofDiagnostics.UnnecessaryNullForgivingOperatorId:
                    if (TryFindNullForgivingExpression(root, diagnostic.Location.SourceSpan, out var suppression))
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                "Remove unnecessary null-forgiving operator",
                                _ => Task.FromResult(document.WithSyntaxRoot(
                                    root.ReplaceNode(
                                        suppression,
                                        RemoveNullForgivingOperator(suppression)))),
                                "RemoveUnnecessaryNullForgivingOperator"),
                            diagnostic);
                    break;
            }
        }

        private static bool TryFindNullForgivingExpression(
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

        private static ExpressionSyntax RemoveNullForgivingOperator(PostfixUnaryExpressionSyntax suppression)
        {
            var operand = suppression.Operand;
            return operand.WithTrailingTrivia(
                operand.GetTrailingTrivia().AddRange(suppression.GetTrailingTrivia()));
        }

        private void RegisterInferredContractCodeFix(
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

        private enum SimpleRemovalOperation
        {
            MisplacedAttribute,
            DeclarationAndAccessors,
            DiagnosticContract
        }

        private sealed class SimpleRemovalRegistration
        {
            internal SimpleRemovalRegistration(
                string diagnosticId,
                string title,
                SimpleRemovalOperation operation,
                string equivalenceKey,
                params string[] attributeTypeNames)
            {
                DiagnosticId = diagnosticId;
                Title = title;
                Operation = operation;
                EquivalenceKey = equivalenceKey;
                AttributeTypeNames = attributeTypeNames;
            }

            internal string DiagnosticId { get; }
            internal string Title { get; }
            internal SimpleRemovalOperation Operation { get; }
            internal string EquivalenceKey { get; }
            internal string[] AttributeTypeNames { get; }
        }

        private static bool TryFindPurityTargetDeclaration(SyntaxNode root, int position, out SyntaxNode declaration)
        {
            declaration = null!;
            for (var node = root.FindToken(position).Parent; node != null; node = node.Parent)
                switch (node)
                {
                    case MethodDeclarationSyntax:
                    case ConstructorDeclarationSyntax:
                    case OperatorDeclarationSyntax:
                    case ConversionOperatorDeclarationSyntax:
                    case IndexerDeclarationSyntax:
                    case PropertyDeclarationSyntax:
                    case AccessorDeclarationSyntax:
                    case LocalFunctionStatementSyntax:
                        declaration = node;
                        return true;
                }

            return false;
        }

        private static bool TryFindAttributeSyntax(SyntaxNode root, TextSpan span, out AttributeSyntax attribute)
        {
            attribute = null!;
            var node = root.FindNode(span, false, true);
            attribute = node.FirstAncestorOrSelf<AttributeSyntax>() ?? (node as AttributeSyntax)!;
            return attribute != null;
        }

        private static SyntaxNode? GetHostForAttribute(AttributeSyntax attr)
        {
            if (attr.Parent is not AttributeListSyntax list)
                return null;
            return list.Parent;
        }

        private static bool CanMoveAttributeToGetter(AttributeSyntax attribute)
        {
            return AttributeTargetSyntaxFacts.IsGetterAliasTarget(GetHostForAttribute(attribute));
        }

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

        private static INamedTypeSymbol? GetAttributeClass(SemanticModel model, AttributeSyntax attributeSyntax)
        {
            var sym = model.GetSymbolInfo(attributeSyntax).Symbol;
            if (sym is IMethodSymbol { MethodKind: MethodKind.Constructor } ctor)
                return ctor.ContainingType;
            if (sym is INamedTypeSymbol nt)
                return nt;
            return null;
        }

        private static Func<INamedTypeSymbol?, bool> AcceptedAttribute(
            SharpProofAttributeIdentityPolicy policy,
            params string[] attributeTypeNames)
        {
            return type => attributeTypeNames.Any(name => policy.IsAccepted(type, name));
        }

        private Task<Document> RemoveMisplacedAttributeAsync(Document document, SyntaxNode root, AttributeSyntax attr,
            CancellationToken cancellationToken)
        {
            var host = GetHostForAttribute(attr);
            if (host == null)
                return Task.FromResult(document);
            var newHost = RemoveAttributeFromHost(host, attr);
            if (ReferenceEquals(host, newHost))
                return Task.FromResult(document);
            var newRoot = root.ReplaceNode(host, newHost);
            return Task.FromResult(document.WithSyntaxRoot(newRoot));
        }

        private async Task<Document> MoveAttributeToGetterAsync(
            Document document,
            SyntaxNode root,
            AttributeSyntax attribute,
            CancellationToken cancellationToken)
        {
            var host = GetHostForAttribute(attribute);
            if (host is not PropertyDeclarationSyntax && host is not IndexerDeclarationSyntax) return document;

            var lineEnding = await GetLineEndingAsync(document, cancellationToken).ConfigureAwait(false);
            var hostWithoutAttribute = RemoveAttributeFromHost(host, attribute, preserveLeadingTrivia: false);
            var sourceAttributeList = (AttributeListSyntax)attribute.Parent!;
            var attributeList = sourceAttributeList.Attributes.Count == 1
                ? sourceAttributeList.WithAttributes(SyntaxFactory.SingletonSeparatedList(attribute))
                : SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));
            attributeList = attributeList
                .WithTarget(null)
                .WithAdditionalAnnotations(Formatter.Annotation);
            var updatedHost = AddAttributeToGetter(hostWithoutAttribute, attributeList, lineEnding);
            if (updatedHost == null) return document;

            return document.WithSyntaxRoot(root.ReplaceNode(host, updatedHost));
        }

        private static SyntaxNode? AddAttributeToGetter(
            SyntaxNode host,
            AttributeListSyntax attributeList,
            string lineEnding)
        {
            return host switch
            {
                PropertyDeclarationSyntax property => AddAttributeToGetter(
                    property,
                    property.AccessorList,
                    property.ExpressionBody,
                    property.SemicolonToken,
                    attributeList,
                    lineEnding,
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
                    lineEnding,
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
            string lineEnding,
            Func<TDeclaration, AccessorListSyntax, TDeclaration> withAccessorList,
            Func<TDeclaration, TDeclaration> withoutExpressionBody)
            where TDeclaration : SyntaxNode
        {
            if (accessorList != null)
            {
                var getter = accessorList.Accessors.FirstOrDefault(static accessor =>
                    accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                if (getter == null) return null;

                attributeList = FormatAttributeBeforeExistingGetter(attributeList, getter, lineEnding);
                var updatedGetter = getter.WithAttributeLists(
                    getter.AttributeLists.Insert(0, attributeList));
                return withAccessorList(
                    declaration,
                    accessorList.WithAccessors(accessorList.Accessors.Replace(getter, updatedGetter)));
            }

            if (expressionBody == null) return null;

            var expressionGetter = CreateExpressionBodiedGetter(
                expressionBody,
                semicolonToken,
                attributeList,
                GetIndentation(declaration),
                lineEnding);
            var declarationWithAccessor = RemoveTrailingTriviaFromLastToken(withoutExpressionBody(declaration));
            return withAccessorList(declarationWithAccessor, CreateAccessorList(expressionGetter));
        }

        private static AccessorDeclarationSyntax CreateExpressionBodiedGetter(
            ArrowExpressionClauseSyntax expressionBody,
            SyntaxToken semicolonToken,
            AttributeListSyntax attributeList,
            string hostIndentation,
            string lineEnding)
        {
            var accessorIndentation = hostIndentation + "    ";
            attributeList = attributeList
                .WithLeadingTrivia(FormatMovedLeadingTrivia(
                    attributeList.GetLeadingTrivia(),
                    default,
                    lineEnding))
                .WithTrailingTrivia(FormatMovedTrailingTrivia(
                    attributeList.GetTrailingTrivia(),
                    accessorIndentation,
                    lineEnding));
            expressionBody = expressionBody.WithArrowToken(
                expressionBody.ArrowToken.WithLeadingTrivia(SyntaxFactory.Space));
            var semicolonTrailingTrivia = PreserveInlineTriviaBeforeLineBreak(
                semicolonToken.TrailingTrivia,
                lineEnding,
                hostIndentation);
            return SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                .WithAttributeLists(SyntaxFactory.SingletonList(attributeList))
                .WithExpressionBody(expressionBody)
                .WithSemicolonToken(
                    semicolonToken
                        .WithLeadingTrivia(default(SyntaxTriviaList))
                        .WithTrailingTrivia(semicolonTrailingTrivia));
        }

        private static AccessorListSyntax CreateAccessorList(AccessorDeclarationSyntax getter)
        {
            var semicolonToken = getter.SemicolonToken;
            var trailingTrivia = semicolonToken.TrailingTrivia;
            var indentation = trailingTrivia.LastOrDefault(static trivia =>
                trivia.IsKind(SyntaxKind.WhitespaceTrivia)).ToFullString();
            var lineEnding = trailingTrivia.FirstOrDefault(static trivia =>
                trivia.IsKind(SyntaxKind.EndOfLineTrivia)).ToFullString();
            if (lineEnding.Length == 0) lineEnding = "\n";

            return SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(getter))
                .WithOpenBraceToken(
                    SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                        .WithLeadingTrivia(LineBreakAndIndent(lineEnding, indentation))
                        .WithTrailingTrivia(LineBreakAndIndent(lineEnding, indentation + "    ")))
                .WithCloseBraceToken(
                    SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                        .WithTrailingTrivia(SyntaxFactory.EndOfLine(lineEnding)));
        }

        private static TNode RemoveTrailingTriviaFromLastToken<TNode>(TNode node)
            where TNode : SyntaxNode
        {
            var lastToken = node.GetLastToken();
            return (TNode)node.ReplaceToken(lastToken, lastToken.WithTrailingTrivia(default(SyntaxTriviaList)));
        }

        private static AttributeListSyntax FormatAttributeBeforeExistingGetter(
            AttributeListSyntax attributeList,
            AccessorDeclarationSyntax getter,
            string lineEnding)
        {
            var indentation = SyntaxFactory.TriviaList(
                getter.GetLeadingTrivia()
                    .Reverse()
                    .TakeWhile(static trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                    .Reverse());
            return attributeList
                .WithLeadingTrivia(FormatMovedLeadingTrivia(
                    attributeList.GetLeadingTrivia(),
                    indentation,
                    lineEnding))
                .WithTrailingTrivia(FormatMovedTrailingTrivia(
                    attributeList.GetTrailingTrivia(),
                    string.Empty,
                    lineEnding));
        }

        private static SyntaxTriviaList FormatMovedLeadingTrivia(
            SyntaxTriviaList source,
            SyntaxTriviaList indentation,
            string lineEnding)
        {
            if (!source.Any(static trivia =>
                    !trivia.IsKind(SyntaxKind.WhitespaceTrivia) &&
                    !trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
                return indentation;

            var builder = new List<SyntaxTrivia>();
            builder.AddRange(indentation);
            var afterLineBreak = false;
            foreach (var trivia in source)
            {
                if (trivia.IsKind(SyntaxKind.WhitespaceTrivia)) continue;
                if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                {
                    if (!afterLineBreak) builder.Add(SyntaxFactory.EndOfLine(lineEnding));
                    builder.AddRange(indentation);
                    afterLineBreak = true;
                    continue;
                }

                builder.Add(trivia);
                afterLineBreak = false;
            }

            if (!afterLineBreak)
            {
                builder.Add(SyntaxFactory.EndOfLine(lineEnding));
                builder.AddRange(indentation);
            }

            return SyntaxFactory.TriviaList(builder);
        }

        private static SyntaxTriviaList FormatMovedTrailingTrivia(
            SyntaxTriviaList source,
            string indentation,
            string lineEnding)
        {
            if (!source.Any(static trivia =>
                    !trivia.IsKind(SyntaxKind.WhitespaceTrivia) &&
                    !trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
                return LineBreakAndIndent(lineEnding, indentation);

            var builder = new List<SyntaxTrivia>();
            var atLineStart = false;
            foreach (var trivia in source)
            {
                if (trivia.IsKind(SyntaxKind.WhitespaceTrivia)) continue;
                if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                {
                    if (!atLineStart) builder.Add(SyntaxFactory.EndOfLine(lineEnding));
                    if (indentation.Length != 0) builder.Add(SyntaxFactory.Whitespace(indentation));
                    atLineStart = true;
                    continue;
                }

                if (trivia.HasStructure && trivia.GetStructure() is DirectiveTriviaSyntax && !atLineStart)
                {
                    builder.Add(SyntaxFactory.EndOfLine(lineEnding));
                    if (indentation.Length != 0) builder.Add(SyntaxFactory.Whitespace(indentation));
                    atLineStart = true;
                }
                else if (!atLineStart)
                {
                    builder.Add(SyntaxFactory.Space);
                }

                builder.Add(trivia);
                atLineStart = false;
            }

            if (!atLineStart)
            {
                builder.Add(SyntaxFactory.EndOfLine(lineEnding));
                if (indentation.Length != 0) builder.Add(SyntaxFactory.Whitespace(indentation));
            }

            return SyntaxFactory.TriviaList(builder);
        }

        private static string GetIndentation(SyntaxNode node)
        {
            return string.Concat(
                node.GetLeadingTrivia()
                    .Reverse()
                    .TakeWhile(static trivia => trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                    .Reverse()
                    .Select(static trivia => trivia.ToFullString()));
        }

        private static SyntaxTriviaList LineBreakAndIndent(string lineEnding, string indentation)
        {
            var trivia = new List<SyntaxTrivia> { SyntaxFactory.EndOfLine(lineEnding) };
            if (indentation.Length != 0) trivia.Add(SyntaxFactory.Whitespace(indentation));
            return SyntaxFactory.TriviaList(trivia);
        }

        private static SyntaxTriviaList PreserveInlineTriviaBeforeLineBreak(
            SyntaxTriviaList originalTrivia,
            string lineEnding,
            string indentation)
        {
            var inlineTrivia = originalTrivia
                .TakeWhile(static trivia => !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                .ToList();
            if (!inlineTrivia.Any(static trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia)))
                inlineTrivia.Clear();

            inlineTrivia.AddRange(LineBreakAndIndent(lineEnding, indentation));
            return SyntaxFactory.TriviaList(inlineTrivia);
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
                foreach (var location in GetDiagnosticLocations(diagnostic))
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

        private static IEnumerable<Location> GetDiagnosticLocations(Diagnostic diagnostic)
        {
            yield return diagnostic.Location;
            foreach (var location in diagnostic.AdditionalLocations) yield return location;
        }

        private async Task<Document> RemoveAttributesMatchingAsync(
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

        private async Task<Document> AddEnforcePureAttributeAsync(
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

    private static bool HasUnaliasedSharpProofAttributesUsing(SyntaxNode declaration)
    {
        foreach (var ancestor in declaration.AncestorsAndSelf())
        {
            var usingDirectives = ancestor switch
            {
                CompilationUnitSyntax compilationUnit => compilationUnit.Usings,
                BaseNamespaceDeclarationSyntax namespaceDeclaration => namespaceDeclaration.Usings,
                _ => default
            };
            if (usingDirectives.Any(static directive =>
                    directive.Alias == null &&
                    string.Equals(directive.Name?.ToString(), "SharpProof.Attributes", StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

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
