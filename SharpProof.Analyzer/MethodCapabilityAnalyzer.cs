using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Analyzer.Configuration;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static class MethodCapabilityAnalyzer
{
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

    internal static void AnalyzeSymbolForCapabilities(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var methodSymbol = context.MethodSymbol;

        if (!TryGetAllowedCapabilities(
                methodSymbol,
                attributePolicy,
                context.CancellationToken,
                out var allowedCapabilities,
                out var invalidContract))
            return;

        if (invalidContract != null)
        {
            var diagnostic = InvalidContractArgumentDiagnostics.Create(
                "[AllowedCapabilities]",
                invalidContract.Argument,
                invalidContract.Reason,
                invalidContract.Location ?? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node),
                methodSymbol,
                context.Node.SyntaxTree);
            if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);

            return;
        }

        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) return;

        var outcome = context.State.GetCapabilityOutcome(context.CancellationToken);
        if (!outcome.IsSuccess)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var queryFailure = CreateQueryFailureDiagnostic(
                methodSymbol,
                outcome.Error!,
                context.Node.SyntaxTree);
            if (!baseline.IsSuppressed(queryFailure)) context.ReportDiagnostic(queryFailure);
            return;
        }

        var result = outcome.Value!;

        foreach (var site in result.Sites)
        {
            if (site.IsUnknown)
            {
                var unknownSiteDiagnostic = CreateUnknownDiagnostic(methodSymbol, site, context.Node.SyntaxTree);
                if (!baseline.IsSuppressed(unknownSiteDiagnostic)) context.ReportDiagnostic(unknownSiteDiagnostic);

                continue;
            }

            var disallowedCapabilities =
                NormalizeCapabilities((CapabilityFlags)(long)site.Capabilities) & ~allowedCapabilities;
            if (disallowedCapabilities == CapabilityFlags.None) continue;

            var diagnostic =
                CreateViolationDiagnostic(methodSymbol, site, disallowedCapabilities, context.Node.SyntaxTree);
            if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
        }

        if (result.Sites.Count == 0 && result.UnknownReasons.Count > 0)
        {
            var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);
            var properties = BaselineDiagnosticProperties.Add(
                ImmutableDictionary<string, string?>.Empty
                    .Add(SharpProofDiagnostics.CapabilityUnknownReasonProperty, result.UnknownReasons[0].ToString()),
                methodSymbol,
                context.Node.SyntaxTree,
                "CapabilityUnknown",
                evidenceKey: "unknown:" + result.UnknownReasons[0]);
            var unknownReasonInfo = result.UnknownReasonDetails.FirstOrDefault() ??
                                    SymbolicUnknownReasonTaxonomy.ForCapability(result.UnknownReasons[0]);
            properties = UnknownReasonDiagnosticProperties.Add(properties, unknownReasonInfo);
            properties = ExplainDiagnosticProperties.Add(
                properties,
                location,
                "[AllowedCapabilities]",
                "unknown",
                unknownReasonInfo.Code);
            var diagnostic = Diagnostic.Create(
                SharpProofDiagnostics.CapabilityUnknownRule,
                location,
                null,
                properties,
                new object[]
                {
                    "<method body>",
                    methodSymbol.Name,
                    result.UnknownReasons[0].ToString()
                });
            if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
        }
    }

    private static Diagnostic CreateQueryFailureDiagnostic(
        IMethodSymbol methodSymbol,
        SymbolicError error,
        SyntaxTree syntaxTree)
    {
        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, CancellationToken.None);
        var unknownReasonInfo = SymbolicUnknownReasonTaxonomy.ForCapabilityFailure(
            error.Code + ": " + error.Message);
        var properties = BaselineDiagnosticProperties.Add(
            ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.CapabilityUnknownReasonProperty, error.Code),
            methodSymbol,
            syntaxTree,
            "CapabilityQueryFailure",
            evidenceKey: "query-failure:" + error.Code);
        properties = UnknownReasonDiagnosticProperties.Add(properties, unknownReasonInfo);
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            "[AllowedCapabilities]",
            "unknown",
            unknownReasonInfo.Code);
        return Diagnostic.Create(
            SharpProofDiagnostics.CapabilityUnknownRule,
            location,
            null,
            properties,
            new object[] { "<method body>", methodSymbol.Name, error.Code });
    }

    private static Diagnostic CreateViolationDiagnostic(
        IMethodSymbol methodSymbol,
        SymbolicCapabilitySite site,
        CapabilityFlags disallowedCapabilities,
        SyntaxTree syntaxTree)
    {
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.CapabilityProperty, FormatCapabilities(disallowedCapabilities))
            .Add(SharpProofDiagnostics.CapabilityOperationKindProperty, site.OperationKind);
        var location = Location.Create(
            syntaxTree,
            new TextSpan(site.SourceSpanStart, site.SourceSpanLength));

        if (!string.IsNullOrWhiteSpace(site.SymbolDisplayName))
            properties = properties.Add(SharpProofDiagnostics.CapabilitySymbolProperty, site.SymbolDisplayName);

        properties = BaselineDiagnosticProperties.Add(
            properties,
            methodSymbol,
            syntaxTree,
            site.OperationKind,
            evidenceKey: CreateCapabilityEvidenceKey(site, FormatCapabilities(disallowedCapabilities)));
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            "[AllowedCapabilities]",
            "violated");

        return Diagnostic.Create(
            SharpProofDiagnostics.CapabilityViolationRule,
            location,
            null,
            properties, site.OperationText, methodSymbol.Name, FormatCapabilities(disallowedCapabilities));
    }

    private static Diagnostic CreateUnknownDiagnostic(
        IMethodSymbol methodSymbol,
        SymbolicCapabilitySite site,
        SyntaxTree syntaxTree)
    {
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.CapabilityUnknownReasonProperty, site.UnknownReason.ToString())
            .Add(SharpProofDiagnostics.CapabilityOperationKindProperty, site.OperationKind);
        properties = UnknownReasonDiagnosticProperties.Add(properties, site.UnknownReasonInfo);
        var location = Location.Create(
            syntaxTree,
            new TextSpan(site.SourceSpanStart, site.SourceSpanLength));

        if (!string.IsNullOrWhiteSpace(site.SymbolDisplayName))
            properties = properties.Add(SharpProofDiagnostics.CapabilitySymbolProperty, site.SymbolDisplayName);

        properties = BaselineDiagnosticProperties.Add(
            properties,
            methodSymbol,
            syntaxTree,
            site.OperationKind,
            evidenceKey: CreateCapabilityEvidenceKey(site, site.UnknownReason.ToString()));
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            "[AllowedCapabilities]",
            "unknown",
            site.UnknownReasonInfo.Code);

        return Diagnostic.Create(
            SharpProofDiagnostics.CapabilityUnknownRule,
            location,
            null,
            properties, site.OperationText, methodSymbol.Name, site.UnknownReason.ToString());
    }

    private static string CreateCapabilityEvidenceKey(
        SymbolicCapabilitySite site,
        string outcome)
    {
        return site.OperationKind +
               "@" +
               site.SourceSpanStart.ToString(CultureInfo.InvariantCulture) +
               ":" +
               site.SourceSpanLength.ToString(CultureInfo.InvariantCulture) +
               "|" +
               outcome +
               "|" +
               (site.SymbolDisplayName ?? string.Empty);
    }

    private static bool TryGetAllowedCapabilities(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken,
        out CapabilityFlags capabilities,
        out InvalidContractArgument? invalidContract)
    {
        capabilities = CapabilityFlags.None;
        invalidContract = null;
        var found = false;

        foreach (var source in MethodContractHierarchy.EnumerateSources(methodSymbol, cancellationToken))
        foreach (var attribute in attributePolicy.GetAcceptedAttributes(
                     source,
                     "AllowedCapabilitiesAttribute"))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                    rawValue.ToString(CultureInfo.InvariantCulture),
                    "unknown SharpProofCapability bits are set",
                    location);
                return true;
            }

            var declaredCapabilities = ExpandAllowedCapabilities((CapabilityFlags)rawValue);
            capabilities = found ? capabilities & declaredCapabilities : declaredCapabilities;
            found = true;
        }

        return found;
    }

    private static bool TryGetCapabilityArgumentValue(AttributeData attribute, out long value)
    {
        value = 0;
        if (attribute.ConstructorArguments.Length != 1) return false;

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

    private static string GetAttributeArgumentText(AttributeData attribute, CancellationToken cancellationToken)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is AttributeSyntax attributeSyntax)
            return attributeSyntax.ArgumentList?.Arguments.FirstOrDefault()?.ToString() ?? "<missing>";

        return "<missing>";
    }

    private static CapabilityFlags NormalizeCapabilities(CapabilityFlags capabilities)
    {
        if ((capabilities & (CapabilityFlags.FileRead |
                             CapabilityFlags.FileWrite |
                             CapabilityFlags.Network |
                             CapabilityFlags.Console |
                             CapabilityFlags.Registry)) != 0)
            capabilities |= CapabilityFlags.IO;

        return capabilities;
    }

    private static CapabilityFlags ExpandAllowedCapabilities(CapabilityFlags capabilities)
    {
        var explicitlyAllowsIo = (capabilities & CapabilityFlags.IO) != 0;
        if (explicitlyAllowsIo)
            capabilities |= CapabilityFlags.FileRead |
                            CapabilityFlags.FileWrite |
                            CapabilityFlags.Network |
                            CapabilityFlags.Console |
                            CapabilityFlags.Registry;

        return NormalizeCapabilities(capabilities);
    }

    private static string FormatCapabilities(CapabilityFlags capabilities)
    {
        if (capabilities == CapabilityFlags.None) return "None";

        var values = Enum.GetValues(typeof(CapabilityFlags))
            .Cast<CapabilityFlags>()
            .Where(value => value != CapabilityFlags.None && capabilities.HasFlag(value))
            .Select(static value => value.ToString())
            .ToArray();
        return values.Length == 0 ? "None" : string.Join(", ", values);
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
        NativeInterop = 1 << 12
    }

    private sealed record InvalidContractArgument(
        string Argument,
        string Reason,
        Location? Location);
}
