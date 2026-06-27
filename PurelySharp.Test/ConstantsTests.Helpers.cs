using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using PurelySharp.Analyzer;
using PurelySharp.Analyzer.Engine;

namespace PurelySharp.Test
{
    public partial class ConstantsTests
    {
        private static void AssertCatalogMembership(string signature, bool expectedPure, bool expectedImpure)
        {
            Assert.That(Constants.KnownPureBCLMembers.Contains(signature), Is.EqualTo(expectedPure), signature);
            Assert.That(Constants.KnownImpureMethods.Contains(signature), Is.EqualTo(expectedImpure), signature);
            Assert.That(expectedPure && expectedImpure, Is.False, "Test sample should not intentionally expect a catalog conflict: " + signature);
        }

        private static void AssertNotInManualCatalogs(string signature)
        {
            Assert.That(Constants.KnownPureBCLMembers, Does.Not.Contain(signature), signature);
            Assert.That(Constants.KnownImpureMethods, Does.Not.Contain(signature), signature);
            Assert.That(Constants.KnownFreshOwnedArrayReturningMembers, Does.Not.Contain(signature), signature);
        }

        private static (bool matched, string classification) GetGeneratedPurityClassification(IMethodSymbol methodSymbol, Compilation compilation)
        {
            Assert.Ignore("Generated purity classification remains dormant here until a test supplies synthetic effect summary inputs.");
            return default;
        }

        private static (bool matched, string classification) GetGeneratedPurityClassification(
            IMethodSymbol methodSymbol,
            Compilation compilation,
            ImmutableArray<AdditionalText> additionalFiles)
        {
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(additionalFiles), default(CancellationToken) })!;
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
            params (string AssemblyPath, string FileName, string ActualMethodLookupSymbol, string DisplaySymbol, string Classification, string CategoriesJson)[] entries)
        {
            return GeneratedPurityTestSupport.CreateSyntheticGeneratedPurityAdditionalFiles(entries);
        }

        private static string FormatJsonArray(params string[] values)
        {
            if (values.Length == 0)
            {
                return "[]";
            }

            return "[\"" + string.Join("\", \"", values) + "\"]";
        }

        private static string GetInvocationSignature(Compilation compilation, SyntaxTree syntaxTree, string expressionText)
        {
            var invocations = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node => node.ToString() == expressionText)
                .ToArray();
            Assert.That(invocations, Is.Not.Empty, "Invocation should exist: " + expressionText);
            var invocation = invocations[^1];
            var symbol = compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol;
            Assert.That(symbol, Is.Not.Null, "Invocation should resolve: " + expressionText);
            return symbol!.OriginalDefinition.ToDisplayString();
        }

        private static string GetObjectCreationSignature(Compilation compilation, SyntaxTree syntaxTree, string expressionText)
        {
            var objectCreation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(node => node.ToString() == expressionText);
            var symbol = compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(objectCreation).Symbol;
            Assert.That(symbol, Is.Not.Null, "Object creation should resolve: " + expressionText);
            return symbol!.OriginalDefinition.ToDisplayString();
        }

        private static string GetPropertySignature(Compilation compilation, SyntaxTree syntaxTree, string expressionText, bool preferSetter = false)
        {
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == expressionText);
            var symbol = compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol;
            Assert.That(symbol, Is.Not.Null, "Property should resolve: " + expressionText);

            if (preferSetter && symbol is IPropertySymbol propertySymbol && propertySymbol.SetMethod != null)
            {
                var setterSignature = propertySymbol.SetMethod.OriginalDefinition.ToDisplayString();
                return setterSignature.EndsWith(".set", StringComparison.Ordinal)
                    ? setterSignature
                    : setterSignature + ".set";
            }

            var signature = symbol!.OriginalDefinition.ToDisplayString();
            return signature.EndsWith(".get", StringComparison.Ordinal) || signature.EndsWith(".set", StringComparison.Ordinal)
                ? signature
                : signature + ".get";
        }

        private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences() =>
            AnalyzerTestHost.GetTrustedPlatformReferences();
    }
}
