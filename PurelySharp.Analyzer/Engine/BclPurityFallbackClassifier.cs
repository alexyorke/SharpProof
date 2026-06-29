using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace PurelySharp.Analyzer.Engine
{
    internal static class BclPurityFallbackClassifier
    {
        public const string CatalogSource = "bcl_heuristic_fallback";
        public const string ProbablyPure = "probably_pure";
        public const string ProbablyImpure = "probably_impure";
        public const string Unknown = "unknown";

        public readonly struct Classification
        {
            public Classification(string guess, string confidence, string reason, string category)
            {
                Guess = guess;
                Confidence = confidence;
                Reason = reason;
                Category = category;
            }

            public string Guess { get; }
            public string Confidence { get; }
            public string Reason { get; }
            public string Category { get; }
        }

        public static bool TryClassify(ISymbol? symbol, out Classification classification)
        {
            classification = default;
            if (symbol == null)
            {
                return false;
            }

            var original = symbol.OriginalDefinition;
            if (!IsFrameworkMetadataSymbol(original))
            {
                return false;
            }

            if (original is IMethodSymbol methodSymbol &&
                methodSymbol.AssociatedSymbol is IPropertySymbol associatedProperty)
            {
                return TryClassifyProperty(associatedProperty.OriginalDefinition, out classification);
            }

            if (original is IPropertySymbol propertySymbol)
            {
                return TryClassifyProperty(propertySymbol, out classification);
            }

            if (original is IMethodSymbol method)
            {
                classification = ClassifyMethod(method);
                return true;
            }

            return false;
        }

        private static Classification ClassifyMethod(IMethodSymbol method)
        {
            if (method.Parameters.Any(parameter => parameter.RefKind == RefKind.Ref || parameter.RefKind == RefKind.Out))
            {
                return ProbablyImpureBecause("ref_or_out_parameter");
            }

            if (method.ReturnsByRef || method.ReturnsByRefReadonly)
            {
                return ProbablyImpureBecause("byref_return");
            }

            if (IsAmbientNamespaceOrType(method.ContainingNamespace, method.ContainingType))
            {
                return ProbablyImpureBecause("ambient_namespace_or_type");
            }

            if (method.MethodKind == MethodKind.Constructor)
            {
                return UnknownBecause("metadata_constructor_without_body");
            }

            if (method.ReturnsVoid)
            {
                return ProbablyImpureBecause("void_returning_metadata_method");
            }

            if (HasMutatingName(method.Name))
            {
                return ProbablyImpureBecause("mutating_method_name");
            }

            if (!IsValueLikeType(method.ReturnType) &&
                !method.IsStatic)
            {
                return UnknownBecause("reference_returning_instance_metadata_method");
            }

            if (method.Parameters.All(parameter => IsValueLikeType(parameter.Type) || IsReadOnlyViewType(parameter.Type)))
            {
                return ProbablyPureBecause("value_return_no_ref_or_out");
            }

            return UnknownBecause("metadata_method_shape_ambiguous");
        }

        private static bool TryClassifyProperty(IPropertySymbol property, out Classification classification)
        {
            if (property.SetMethod != null && property.GetMethod == null)
            {
                classification = ProbablyImpureBecause("metadata_property_setter");
                return true;
            }

            if (property.Parameters.Any(parameter => parameter.RefKind == RefKind.Ref || parameter.RefKind == RefKind.Out))
            {
                classification = ProbablyImpureBecause("ref_or_out_indexer_parameter");
                return true;
            }

            if (IsAmbientNamespaceOrType(property.ContainingNamespace, property.ContainingType))
            {
                classification = ProbablyImpureBecause("ambient_namespace_or_type");
                return true;
            }

            if (!IsValueLikeType(property.Type) &&
                property.GetMethod?.IsStatic != true)
            {
                classification = UnknownBecause("reference_returning_instance_metadata_property");
                return true;
            }

            classification = ProbablyPureBecause("metadata_getter_value_like_return");
            return true;
        }

        private static bool IsFrameworkMetadataSymbol(ISymbol symbol)
        {
            if (!PurityAnalysisEngine.IsMetadataSymbol(symbol))
            {
                return false;
            }

            var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (!namespaceName.Equals("System", StringComparison.Ordinal) &&
                !namespaceName.StartsWith("System.", StringComparison.Ordinal))
            {
                return false;
            }

            var assemblyName = symbol.ContainingAssembly?.Identity.Name ?? string.Empty;
            return assemblyName.Equals("mscorlib", StringComparison.Ordinal) ||
                assemblyName.Equals("netstandard", StringComparison.Ordinal) ||
                assemblyName.Equals("System", StringComparison.Ordinal) ||
                assemblyName.Equals("System.Private.CoreLib", StringComparison.Ordinal) ||
                assemblyName.StartsWith("System.", StringComparison.Ordinal);
        }

        private static bool IsAmbientNamespaceOrType(INamespaceSymbol? namespaceSymbol, INamedTypeSymbol? typeSymbol)
        {
            var namespaceName = namespaceSymbol?.ToDisplayString() ?? string.Empty;
            if (Constants.KnownImpureNamespaces.Any(known =>
                    namespaceName.Equals(known, StringComparison.Ordinal) ||
                    namespaceName.StartsWith(known + ".", StringComparison.Ordinal)))
            {
                return true;
            }

            var typeName = typeSymbol?.OriginalDefinition.ToDisplayString() ?? string.Empty;
            return ContainsAny(
                typeName,
                "Console",
                "Environment",
                "Process",
                "Random",
                "File",
                "Directory",
                "Stream",
                "Socket",
                "Timer",
                "Trace",
                "Debug",
                "Registry",
                "Thread");
        }

        private static bool HasMutatingName(string name)
        {
            return StartsWithAny(
                    name,
                    "Add",
                    "Append",
                    "Clear",
                    "Close",
                    "Create",
                    "Delete",
                    "Ensure",
                    "Insert",
                    "Load",
                    "Move",
                    "Open",
                    "Read",
                    "Receive",
                    "Register",
                    "Remove",
                    "Replace",
                    "Reset",
                    "Run",
                    "Save",
                    "Send",
                    "Set",
                    "Sort",
                    "Start",
                    "Stop",
                    "Throw",
                    "Write") ||
                name.Equals("Dispose", StringComparison.Ordinal);
        }

        private static bool IsValueLikeType(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum ||
                type.IsValueType)
            {
                return true;
            }

            if (type.SpecialType == SpecialType.System_String ||
                type.SpecialType == SpecialType.System_Object)
            {
                return true;
            }

            var displayName = type.OriginalDefinition.ToDisplayString();
            return displayName.Equals("System.Type", StringComparison.Ordinal) ||
                displayName.Equals("System.Version", StringComparison.Ordinal) ||
                displayName.Equals("System.Uri", StringComparison.Ordinal) ||
                displayName.Equals("System.Globalization.CultureInfo", StringComparison.Ordinal);
        }

        private static bool IsReadOnlyViewType(ITypeSymbol type)
        {
            var displayName = type.OriginalDefinition.ToDisplayString();
            return displayName.StartsWith("System.ReadOnlySpan<", StringComparison.Ordinal) ||
                displayName.StartsWith("System.ReadOnlyMemory<", StringComparison.Ordinal);
        }

        private static bool StartsWithAny(string value, params string[] prefixes)
        {
            return prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static bool ContainsAny(string value, params string[] fragments)
        {
            return fragments.Any(fragment => value.IndexOf(fragment, StringComparison.Ordinal) >= 0);
        }

        private static Classification ProbablyPureBecause(string reason) =>
            new Classification(ProbablyPure, "low", reason, "bcl_fallback_probably_pure");

        private static Classification ProbablyImpureBecause(string reason) =>
            new Classification(ProbablyImpure, "low", reason, "bcl_fallback_probably_impure");

        private static Classification UnknownBecause(string reason) =>
            new Classification(Unknown, "low", reason, "bcl_fallback_unknown");
    }
}
