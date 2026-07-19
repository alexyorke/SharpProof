using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Test;

public partial class ConstantsTests
{
    private static void AssertCatalogMembership(string signature, bool expectedPure, bool expectedImpure)
    {
        Assert.That(Constants.KnownPureBCLMembers.Contains(signature), Is.EqualTo(expectedPure), signature);
        Assert.That(Constants.KnownImpureMethods.Contains(signature), Is.EqualTo(expectedImpure), signature);
        Assert.That(expectedPure && expectedImpure, Is.False,
            "Test sample should not intentionally expect a catalog conflict: " + signature);
    }

    private static void AssertNotInManualCatalogs(string signature)
    {
        Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(signature), signature);
        Assert.That(Constants.KnownImpureMethods, Does.Not.Contain(signature), signature);
        Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(signature), signature);
    }

    private static (bool matched, string classification) GetGeneratedPurityClassification(IMethodSymbol methodSymbol,
        Compilation compilation)
    {
        _ = methodSymbol;
        _ = compilation;
        Assert.Ignore(
            "Generated purity classification remains dormant here until a test supplies synthetic effect summary inputs.");
        return default;
    }

    private static (bool matched, string classification) GetGeneratedPurityClassification(
        IMethodSymbol methodSymbol,
        Compilation compilation,
        ImmutableArray<AdditionalText> additionalFiles)
    {
        var catalogType =
            typeof(SharpProofAnalyzer).Assembly.GetType("SharpProof.Analyzer.EffectSummaryCatalog", true)!;
        var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
        var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
        var catalog = fromOptions.Invoke(null,
            new object[] { CreateGeneratedPurityAnalyzerOptions(additionalFiles), default(CancellationToken) })!;
        var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
        var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
        var purityEntry = args[2];
        var classification = matched
            ? (string)purityEntry!.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
            : string.Empty;
        return (matched, classification);
    }

    private static AnalyzerOptions CreateGeneratedPurityAnalyzerOptions(ImmutableArray<AdditionalText> additionalFiles)
    {
        return GeneratedPurityTestSupport.CreateAnalyzerOptions(additionalFiles);
    }

    private static ImmutableArray<AdditionalText> CreateSyntheticGeneratedPurityAdditionalFiles(
        params (string AssemblyPath, string FileName, string ActualMethodLookupSymbol, string DisplaySymbol, string
            Classification, string CategoriesJson)[] entries)
    {
        return GeneratedPurityTestSupport.CreateSyntheticGeneratedPurityAdditionalFiles(entries);
    }

    private static string FormatJsonArray(params string[] values)
    {
        return GeneratedPurityTestSupport.FormatJsonArray(values);
    }

    private static string GetInvocationSignature(Compilation compilation, SyntaxTree syntaxTree, string expressionText)
    {
        return ResolveExpressionSymbol<InvocationExpressionSyntax>(
                compilation,
                syntaxTree,
                expressionText,
                "Invocation",
                selectLast: true)
            .ToDisplayString();
    }

    private static string GetObjectCreationSignature(Compilation compilation, SyntaxTree syntaxTree,
        string expressionText)
    {
        return ResolveExpressionSymbol<ObjectCreationExpressionSyntax>(
                compilation,
                syntaxTree,
                expressionText,
                "Object creation")
            .ToDisplayString();
    }

    private static string GetPropertySignature(Compilation compilation, SyntaxTree syntaxTree, string expressionText,
        bool preferSetter = false)
    {
        var symbol = ResolveExpressionSymbol<MemberAccessExpressionSyntax>(
            compilation,
            syntaxTree,
            expressionText,
            "Property");

        if (preferSetter && symbol is IPropertySymbol propertySymbol && propertySymbol.SetMethod != null)
        {
            var setterSignature = propertySymbol.SetMethod.OriginalDefinition.ToDisplayString();
            return setterSignature.EndsWith(".set", StringComparison.Ordinal)
                ? setterSignature
                : setterSignature + ".set";
        }

        var signature = symbol.ToDisplayString();
        return signature.EndsWith(".get", StringComparison.Ordinal) ||
               signature.EndsWith(".set", StringComparison.Ordinal)
            ? signature
            : signature + ".get";
    }

    private static ISymbol ResolveExpressionSymbol<TSyntax>(
        Compilation compilation,
        SyntaxTree syntaxTree,
        string expressionText,
        string description,
        bool selectLast = false)
        where TSyntax : SyntaxNode
    {
        var matches = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TSyntax>()
            .Where(node => string.Equals(node.ToString(), expressionText, StringComparison.Ordinal))
            .ToArray();
        if (selectLast)
            Assert.That(matches, Is.Not.Empty, description + " should exist: " + expressionText);
        else
            Assert.That(matches, Has.Length.EqualTo(1), description + " should be unique: " + expressionText);

        var node = selectLast ? matches[^1] : matches[0];
        var symbol = compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(node).Symbol;
        Assert.That(symbol, Is.Not.Null, description + " should resolve: " + expressionText);
        return symbol!.OriginalDefinition;
    }

    private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
    {
        return AnalyzerTestHost.GetTrustedPlatformReferences();
    }
}
