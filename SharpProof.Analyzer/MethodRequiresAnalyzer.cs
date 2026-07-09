using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer
{
    internal static class MethodRequiresAnalyzer
    {
        internal static void AnalyzeSymbolForRequires(
            SyntaxNodeAnalysisContext context,
            DiagnosticBaseline baseline,
            SharpProofAttributeIdentityPolicy attributePolicy)
        {
            if (context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is not IMethodSymbol methodSymbol)
            {
                return;
            }

            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true)
            {
                return;
            }

            var contracts = RequiresContractHelpers.CollectContracts(methodSymbol, attributePolicy, context.CancellationToken);
            if (contracts.Length == 0)
            {
                return;
            }

            foreach (var contract in contracts)
            {
                if (contract.InvalidReason != null)
                {
                    var invalidDiagnostic = InvalidContractArgumentDiagnostics.Create(
                        RequiresContractHelpers.AttributeDisplayName,
                        contract.Argument,
                        contract.InvalidReason,
                        contract.Location ?? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node),
                        methodSymbol,
                        context.Node.SyntaxTree);
                    ReportIfNotSuppressed(context, baseline, invalidDiagnostic);
                    continue;
                }

                if (!RequiresContractHelpers.TryParseCondition(contract.Condition, out _, out var conditionExpression))
                {
                    ReportIfNotSuppressed(
                        context,
                        baseline,
                        CreateUnsupportedDiagnostic(
                            methodSymbol,
                            contract.Condition,
                            contract.Location,
                            "condition parse failure",
                            additionalLocations: null));
                    continue;
                }

                if (RequiresContractHelpers.ContainsResultReference(conditionExpression))
                {
                    ReportIfNotSuppressed(
                        context,
                        baseline,
                        CreateUnsupportedDiagnostic(
                            methodSymbol,
                            contract.Condition,
                            contract.Location,
                            "result placeholder is not supported in [Requires] conditions",
                            additionalLocations: null));
                }
            }
        }

        internal static void AnalyzeCallSiteForRequires(
            SyntaxNodeAnalysisContext context,
            CompilationPurityService purityService,
            DiagnosticBaseline baseline,
            SharpProofAttributeIdentityPolicy attributePolicy)
        {
            var operation = context.SemanticModel.GetOperation(context.Node, context.CancellationToken);
            var callSite = TryCreateCallSite(operation);
            if (callSite == null)
            {
                return;
            }

            var contracts = RequiresContractHelpers.ValidContracts(callSite.Value.Method, attributePolicy, context.CancellationToken);
            if (contracts.Length == 0)
            {
                return;
            }

            var queryService = new SymbolicQueryService();
            var source = SymbolicSourceInput.FromSyntaxTree(context.Node.SyntaxTree, context.SemanticModel.Compilation);
            var options = new SymbolicQueryOptions(smtAnalysis: purityService.SmtAnalysis);
            var location = callSite.Value.Syntax.GetLocation();
            var lineSpan = location.GetLineSpan();
            var line = lineSpan.StartLinePosition.Line + 1;
            var column = lineSpan.StartLinePosition.Character + 1;
            var seen = ImmutableHashSet.CreateBuilder<string>();

            foreach (var contract in contracts)
            {
                if (!RequiresContractHelpers.TryRewriteForArguments(
                        contract.Condition,
                        callSite.Value.Method,
                        callSite.Value.Arguments,
                        out var rewrittenCondition))
                {
                    ReportIfNotSuppressed(
                        context,
                        baseline,
                        CreateUnsupportedDiagnostic(
                            callSite.Value.Method,
                            contract.Condition,
                            location,
                            "condition rewrite failure",
                            AdditionalLocations(contract.Location)));
                    continue;
                }

                if (!purityService.SmtAnalysis.Options.IsEnabled)
                {
                    ReportIfNotSuppressed(
                        context,
                        baseline,
                        CreateUnsupportedDiagnostic(
                            callSite.Value.Method,
                            contract.Condition,
                            location,
                            "SMT is disabled for [Requires] verification",
                            AdditionalLocations(contract.Location)));
                    continue;
                }

                var proof = queryService.Prove(
                    new SymbolicConditionProofRequest(
                        source,
                        SymbolicQueryTarget.Point(line, column),
                        rewrittenCondition,
                        options),
                    context.CancellationToken);

                if (proof.TruthValue == SymbolicTruthValue.ProvenTrue ||
                    proof.TruthValue == SymbolicTruthValue.Unreachable)
                {
                    continue;
                }

                var key = contract.Condition + ":" + line + ":" + column + ":" + proof.TruthValue + ":" + proof.Reason;
                if (!seen.Add(key))
                {
                    continue;
                }

                if (proof.TruthValue == SymbolicTruthValue.ProvenFalse)
                {
                    ReportIfNotSuppressed(
                        context,
                        baseline,
                        CreateNotProvenDiagnostic(callSite.Value.Method, contract.Condition, location, contract.Location, proof));
                    continue;
                }

                ReportIfNotSuppressed(
                    context,
                    baseline,
                    CreateUnsupportedDiagnostic(
                        callSite.Value.Method,
                        contract.Condition,
                        location,
                        FormatUnknownReason(proof),
                        AdditionalLocations(contract.Location)));
            }
        }

        private static RequiresCallSite? TryCreateCallSite(IOperation? operation)
        {
            return operation switch
            {
                IInvocationOperation invocation => new RequiresCallSite(
                    invocation.TargetMethod,
                    invocation.Arguments,
                    invocation.Syntax),
                IObjectCreationOperation objectCreation when objectCreation.Constructor != null => new RequiresCallSite(
                    objectCreation.Constructor,
                    objectCreation.Arguments,
                    objectCreation.Syntax),
                _ => null,
            };
        }

        private static void ReportIfNotSuppressed(
            SyntaxNodeAnalysisContext context,
            DiagnosticBaseline baseline,
            Diagnostic diagnostic)
        {
            if (!baseline.IsSuppressed(diagnostic))
            {
                context.ReportDiagnostic(diagnostic);
            }
        }

        private static Diagnostic CreateNotProvenDiagnostic(
            IMethodSymbol methodSymbol,
            string condition,
            Location location,
            Location? contractLocation,
            SymbolicConditionProofResult proof)
        {
            var callee = methodSymbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            var properties = AddBaselineProperties(
                ImmutableDictionary<string, string?>.Empty
                    .Add(SharpProofDiagnostics.RequiresConditionProperty, condition)
                    .Add(SharpProofDiagnostics.RequiresProofStatusProperty, proof.Proof.Status.ToString())
                    .Add(SharpProofDiagnostics.RequiresFailureReasonProperty, proof.Reason)
                    .Add(SharpProofDiagnostics.RequiresCalleeProperty, callee),
                methodSymbol,
                "RequiresCallSite",
                condition,
                RequiresContractHelpers.CreateEvidenceKey("not_proven", condition, location, proof.Reason));
            properties = ExplainDiagnosticProperties.Add(
                properties,
                location,
                condition,
                proof.Proof.Status.ToString(),
                FormatUnknownReason(proof),
                impliedConditionText: condition);

            return Diagnostic.Create(
                SharpProofDiagnostics.RequiresNotProvenRule,
                location,
                AdditionalLocations(contractLocation),
                properties,
                callee,
                condition);
        }

        internal static Diagnostic CreateUnsupportedDiagnostic(
            IMethodSymbol methodSymbol,
            string condition,
            Location? location,
            string reason,
            IEnumerable<Location>? additionalLocations)
        {
            var callee = methodSymbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            var properties = AddBaselineProperties(
                ImmutableDictionary<string, string?>.Empty
                    .Add(SharpProofDiagnostics.RequiresConditionProperty, condition)
                    .Add(SharpProofDiagnostics.RequiresProofStatusProperty, SymbolicProofStatus.Unknown.ToString())
                    .Add(SharpProofDiagnostics.RequiresUnknownReasonProperty, reason)
                    .Add(SharpProofDiagnostics.RequiresFailureReasonProperty, reason)
                    .Add(SharpProofDiagnostics.RequiresCalleeProperty, callee),
                methodSymbol,
                "RequiresUnsupported",
                condition,
                RequiresContractHelpers.CreateEvidenceKey("unsupported", condition, location, reason));
            properties = ExplainDiagnosticProperties.Add(
                properties,
                location,
                condition,
                SymbolicProofStatus.Unknown.ToString(),
                reason,
                impliedConditionText: condition);

            return Diagnostic.Create(
                SharpProofDiagnostics.RequiresUnsupportedRule,
                location,
                additionalLocations,
                properties,
                callee,
                condition,
                reason);
        }

        private static IEnumerable<Location>? AdditionalLocations(Location? location)
        {
            return location == null ? null : new[] { location };
        }

        private static ImmutableDictionary<string, string?> AddBaselineProperties(
            ImmutableDictionary<string, string?> properties,
            IMethodSymbol methodSymbol,
            string operationKind,
            string contractText,
            string evidenceKey)
        {
            var syntaxTree = methodSymbol.Locations.FirstOrDefault(location => location.SourceTree != null)?.SourceTree;
            return syntaxTree == null
                ? properties
                : BaselineDiagnosticProperties.Add(
                    properties,
                    methodSymbol,
                    syntaxTree,
                    operationKind,
                    contractText,
                    evidenceKey);
        }

        private static string FormatUnknownReason(SymbolicConditionProofResult proof)
        {
            if (proof.Proof.UnknownReason != SymbolicUnknownReason.None &&
                proof.Proof.UnknownReason != SymbolicUnknownReason.Unknown)
            {
                return proof.Proof.UnknownReason.ToString();
            }

            return proof.Reason switch
            {
                "condition_parse_failure" => "condition parse failure",
                "condition_binding_failure" => "condition binding failure",
                "condition_not_supported" => "condition is not supported by the current bounded proof engine",
                "smt_required" => "SMT is required for [Requires] verification",
                _ when string.IsNullOrWhiteSpace(proof.Reason) => "unknown",
                _ => proof.Reason.Replace('_', ' '),
            };
        }

        private readonly record struct RequiresCallSite(
            IMethodSymbol Method,
            ImmutableArray<IArgumentOperation> Arguments,
            SyntaxNode Syntax);
    }
}
