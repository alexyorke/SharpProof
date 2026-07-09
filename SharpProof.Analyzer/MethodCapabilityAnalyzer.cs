using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Analyzer.Configuration;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer
{
    internal static class MethodCapabilityAnalyzer
    {
        internal static void AnalyzeSymbolForCapabilities(
            SyntaxNodeAnalysisContext context,
            DiagnosticBaseline baseline)
        {
            if (context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is not IMethodSymbol methodSymbol)
            {
                return;
            }

            if (!TryGetAllowedCapabilities(
                    methodSymbol,
                    context.SemanticModel.Compilation,
                    context.CancellationToken,
                    out var allowedCapabilities,
                    out var invalidContract))
            {
                return;
            }

            if (invalidContract != null)
            {
                if (!baseline.IsSuppressed(SharpProofDiagnostics.InvalidContractArgumentId, methodSymbol, context.Node.SyntaxTree))
                {
                    context.ReportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                        "[AllowedCapabilities]",
                        invalidContract.Argument,
                        invalidContract.Reason,
                        invalidContract.Location ?? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node)));
                }

                return;
            }

            var queryService = new SymbolicQueryService();
            var result = queryService.QueryCapabilities(
                new SymbolicCapabilityRequest(
                    SymbolicSourceInput.FromNode(context.Node, context.SemanticModel),
                    SymbolicQueryTarget.Node()),
                context.CancellationToken);

            foreach (var site in result.Sites)
            {
                if (site.IsUnknown)
                {
                    if (baseline.IsSuppressed(SharpProofDiagnostics.CapabilityUnknownId, methodSymbol, context.Node.SyntaxTree))
                    {
                        continue;
                    }

                    context.ReportDiagnostic(CreateUnknownDiagnostic(methodSymbol, site));
                    continue;
                }

                var disallowedCapabilities = NormalizeCapabilities((CapabilityFlags)(long)site.Capabilities & ~allowedCapabilities);
                if (disallowedCapabilities == CapabilityFlags.None ||
                    baseline.IsSuppressed(SharpProofDiagnostics.CapabilityViolationId, methodSymbol, context.Node.SyntaxTree))
                {
                    continue;
                }

                context.ReportDiagnostic(CreateViolationDiagnostic(methodSymbol, site, disallowedCapabilities));
            }

            if (result.Sites.Count == 0 && result.UnknownReasons.Count > 0 &&
                !baseline.IsSuppressed(SharpProofDiagnostics.CapabilityUnknownId, methodSymbol, context.Node.SyntaxTree))
            {
                var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);
                var properties = ImmutableDictionary<string, string?>.Empty
                    .Add(SharpProofDiagnostics.CapabilityUnknownReasonProperty, result.UnknownReasons[0].ToString());
                context.ReportDiagnostic(Diagnostic.Create(
                    SharpProofDiagnostics.CapabilityUnknownRule,
                    location,
                    additionalLocations: null,
                    properties: properties,
                    messageArgs: new object[]
                    {
                        methodSymbol.Name,
                        methodSymbol.Name,
                        result.UnknownReasons[0].ToString(),
                    }));
            }
        }

        [Flags]
        private enum CapabilityFlags : long
        {
            None = 0,
            IO = 1 << 0,
            FileRead = 1 << 1,
            FileWrite = 1 << 2,
            Network = 1 << 3,
            Console = 1 << 4,
            Process = 1 << 5,
            Environment = 1 << 6,
            Registry = 1 << 7,
            Clock = 1 << 8,
            Randomness = 1 << 9,
            Reflection = 1 << 10,
            Synchronization = 1 << 11,
            NativeInterop = 1 << 12,
        }

        private const CapabilityFlags AllKnownCapabilityFlags =
            CapabilityFlags.IO |
            CapabilityFlags.FileRead |
            CapabilityFlags.FileWrite |
            CapabilityFlags.Network |
            CapabilityFlags.Console |
            CapabilityFlags.Process |
            CapabilityFlags.Environment |
            CapabilityFlags.Registry |
            CapabilityFlags.Clock |
            CapabilityFlags.Randomness |
            CapabilityFlags.Reflection |
            CapabilityFlags.Synchronization |
            CapabilityFlags.NativeInterop;

        private static Diagnostic CreateViolationDiagnostic(
            IMethodSymbol methodSymbol,
            SymbolicCapabilitySite site,
            CapabilityFlags disallowedCapabilities)
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.CapabilityProperty, FormatCapabilities(disallowedCapabilities))
                .Add(SharpProofDiagnostics.CapabilityOperationKindProperty, site.OperationKind);

            if (!string.IsNullOrWhiteSpace(site.SymbolDisplayName))
            {
                properties = properties.Add(SharpProofDiagnostics.CapabilitySymbolProperty, site.SymbolDisplayName);
            }

            return Diagnostic.Create(
                SharpProofDiagnostics.CapabilityViolationRule,
                Location.Create(
                    methodSymbol.Locations.First().SourceTree!,
                    new TextSpan(site.SourceSpanStart, site.SourceSpanLength)),
                additionalLocations: null,
                properties: properties,
                messageArgs: new object[]
                {
                    site.OperationText,
                    methodSymbol.Name,
                    FormatCapabilities(disallowedCapabilities),
                });
        }

        private static Diagnostic CreateUnknownDiagnostic(
            IMethodSymbol methodSymbol,
            SymbolicCapabilitySite site)
        {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.CapabilityUnknownReasonProperty, site.UnknownReason.ToString())
                .Add(SharpProofDiagnostics.CapabilityOperationKindProperty, site.OperationKind);

            if (!string.IsNullOrWhiteSpace(site.SymbolDisplayName))
            {
                properties = properties.Add(SharpProofDiagnostics.CapabilitySymbolProperty, site.SymbolDisplayName);
            }

            return Diagnostic.Create(
                SharpProofDiagnostics.CapabilityUnknownRule,
                Location.Create(
                    methodSymbol.Locations.First().SourceTree!,
                    new TextSpan(site.SourceSpanStart, site.SourceSpanLength)),
                additionalLocations: null,
                properties: properties,
                messageArgs: new object[]
                {
                    site.OperationText,
                    methodSymbol.Name,
                    site.UnknownReason.ToString(),
                });
        }

        private static bool TryGetAllowedCapabilities(
            IMethodSymbol methodSymbol,
            Compilation compilation,
            System.Threading.CancellationToken cancellationToken,
            out CapabilityFlags capabilities,
            out InvalidContractArgument? invalidContract)
        {
            capabilities = CapabilityFlags.None;
            invalidContract = null;
            var attributeSymbol =
                compilation.GetTypeByMetadataName("SharpProof.Attributes.AllowedCapabilitiesAttribute") ??
                compilation.GetTypeByMetadataName("AllowedCapabilitiesAttribute");

            foreach (var attribute in methodSymbol.GetAttributes())
            {
                if (!AnalyzerSyntaxHelpers.MatchesAttribute(attribute, attributeSymbol, "AllowedCapabilitiesAttribute"))
                {
                    continue;
                }

                var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
                var argumentText = GetAttributeArgumentText(attribute, cancellationToken);
                if (!TryGetCapabilityArgumentValue(attribute, out var rawValue))
                {
                    invalidContract = new InvalidContractArgument(
                        argumentText,
                        "expected a SharpProofCapability enum value",
                        location);
                    return true;
                }

                if (rawValue < 0 ||
                    ((CapabilityFlags)rawValue & ~AllKnownCapabilityFlags) != 0)
                {
                    invalidContract = new InvalidContractArgument(
                        rawValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "unknown SharpProofCapability bits are set",
                        location);
                    return true;
                }

                capabilities = NormalizeCapabilities((CapabilityFlags)rawValue);
                return true;
            }

            return false;
        }

        private static bool TryGetCapabilityArgumentValue(AttributeData attribute, out long value)
        {
            value = 0;
            if (attribute.ConstructorArguments.Length != 1)
            {
                return false;
            }

            switch (attribute.ConstructorArguments[0].Value)
            {
                case int intValue:
                    value = intValue;
                    return true;
                case uint uintValue:
                    value = uintValue;
                    return true;
                case long longValue:
                    value = longValue;
                    return true;
                default:
                    return false;
            }
        }

        private static string GetAttributeArgumentText(AttributeData attribute, System.Threading.CancellationToken cancellationToken)
        {
            if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is AttributeSyntax attributeSyntax)
            {
                return attributeSyntax.ArgumentList?.Arguments.FirstOrDefault()?.ToString() ?? "<missing>";
            }

            return "<missing>";
        }

        private static CapabilityFlags NormalizeCapabilities(CapabilityFlags capabilities)
        {
            if ((capabilities & (CapabilityFlags.FileRead |
                                 CapabilityFlags.FileWrite |
                                 CapabilityFlags.Network |
                                 CapabilityFlags.Console |
                                 CapabilityFlags.Registry)) != 0)
            {
                capabilities |= CapabilityFlags.IO;
            }

            return capabilities;
        }

        private static string FormatCapabilities(CapabilityFlags capabilities)
        {
            capabilities = NormalizeCapabilities(capabilities);
            if (capabilities == CapabilityFlags.None)
            {
                return "None";
            }

            var values = Enum.GetValues(typeof(CapabilityFlags))
                .Cast<CapabilityFlags>()
                .Where(value => value != CapabilityFlags.None && capabilities.HasFlag(value))
                .Select(static value => value.ToString())
                .ToArray();
            return values.Length == 0 ? "None" : string.Join(", ", values);
        }

        private sealed record InvalidContractArgument(
            string Argument,
            string Reason,
            Location? Location);
    }
}
