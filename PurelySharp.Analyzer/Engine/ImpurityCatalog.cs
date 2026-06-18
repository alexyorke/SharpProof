using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using PurelySharp.Analyzer.Configuration;

namespace PurelySharp.Analyzer.Engine
{
	internal static class ImpurityCatalog
	{
		private static readonly AsyncLocal<AnalyzerConfiguration?> _configuredOverrides = new AsyncLocal<AnalyzerConfiguration?>();

		private static ImmutableHashSet<string> ExtraImpureMethods =>
			_configuredOverrides.Value?.ExtraKnownImpureMethods ?? ImmutableHashSet<string>.Empty;

		private static ImmutableHashSet<string> ExtraPureMethods =>
			_configuredOverrides.Value?.ExtraKnownPureMethods ?? ImmutableHashSet<string>.Empty;

		private static ImmutableHashSet<string> ExtraImpureNamespaces =>
			_configuredOverrides.Value?.ExtraKnownImpureNamespaces ?? ImmutableHashSet<string>.Empty;

		private static ImmutableHashSet<string> ExtraImpureTypes =>
			_configuredOverrides.Value?.ExtraKnownImpureTypes ?? ImmutableHashSet<string>.Empty;

		internal static bool IsStrictPurityProfile =>
			string.Equals(_configuredOverrides.Value?.PurityProfile, "strict", StringComparison.OrdinalIgnoreCase);

		internal static IDisposable UseConfiguredOverrides(AnalyzerConfiguration config)
		{
			var previous = _configuredOverrides.Value;
			_configuredOverrides.Value = config;
			return new ConfiguredOverrideScope(previous);
		}

		private sealed class ConfiguredOverrideScope : IDisposable
		{
			private readonly AnalyzerConfiguration? _previous;
			private bool _disposed;

			public ConfiguredOverrideScope(AnalyzerConfiguration? previous)
			{
				_previous = previous;
			}

			public void Dispose()
			{
				if (_disposed)
				{
					return;
				}

				_configuredOverrides.Value = _previous;
				_disposed = true;
			}
		}

		public static bool IsKnownPureBCLMember(ISymbol symbol)
		{
			if (symbol == null) return false;

			if (IsInConfiguredImpureNamespaceOrType(symbol) && !IsConfiguredKnownPureMember(symbol))
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownPureBCLMember: Configured impure namespace/type suppresses known-pure catalog for {symbol.ToDisplayString()}");
				return false;
			}

			if (IsMutableImmutableBuilderMember(symbol))
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownPureBCLMember: Skipping mutable immutable-builder member: {symbol.ToDisplayString()}");
				return false;
			}

			if (IsImmutableInterlockedMember(symbol))
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownPureBCLMember: Skipping ImmutableInterlocked member: {symbol.ToDisplayString()}");
				return false;
			}

			if (IsKnownPureWebUtilityMethod(symbol))
			{
				return true;
			}

			string signature = symbol.OriginalDefinition.ToDisplayString();
			if (symbol.Kind == SymbolKind.Property)
			{
				if (!signature.EndsWith(".get") && !signature.EndsWith(".set"))
				{
					signature += ".get";
					PurityAnalysisEngine.LogDebug($"    [IsKnownPure] Appended .get to property signature: \"{signature}\"");
				}
			}

			PurityAnalysisEngine.LogDebug($"    [IsKnownPure] Checking HashSet.Contains for signature: \"{signature}\"");
			bool isKnownPure = MatchesKnownPureSignature(signature);
			PurityAnalysisEngine.LogDebug($"    [IsKnownPure] HashSet.Contains result: {isKnownPure}");

			if (!isKnownPure && symbol is IMethodSymbol methodSymbol && methodSymbol.IsGenericMethod)
			{
				signature = methodSymbol.ConstructedFrom.ToDisplayString();
				isKnownPure = MatchesKnownPureSignature(signature);
			}
			else if (!isKnownPure && symbol is IPropertySymbol propertySymbol && propertySymbol.ContainingType.IsGenericType)
			{
				if (propertySymbol.IsIndexer)
				{
					signature = propertySymbol.OriginalDefinition.ToDisplayString();
				}
				else
				{
					signature = $"{propertySymbol.ContainingType.ConstructedFrom.ToDisplayString()}.{propertySymbol.Name}.get";
				}
				isKnownPure = MatchesKnownPureSignature(signature);
			}

			if (isKnownPure)
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownPureBCLMember: Match found for {symbol.ToDisplayString()} using signature '{signature}'");
			}

			return isKnownPure;
		}

		private static bool IsConfiguredKnownPureMember(ISymbol symbol)
		{
			string signature = symbol.OriginalDefinition.ToDisplayString();
			if (symbol.Kind == SymbolKind.Property &&
				!signature.EndsWith(".get", StringComparison.Ordinal) &&
				!signature.EndsWith(".set", StringComparison.Ordinal))
			{
				signature += ".get";
			}

			if (MatchesConfiguredKnownPureSignature(signature))
			{
				return true;
			}

			if (symbol is IMethodSymbol methodSymbol && methodSymbol.IsGenericMethod)
			{
				return MatchesConfiguredKnownPureSignature(methodSymbol.ConstructedFrom.ToDisplayString());
			}

			if (symbol is IPropertySymbol propertySymbol && propertySymbol.ContainingType.IsGenericType)
			{
				signature = propertySymbol.IsIndexer
					? propertySymbol.OriginalDefinition.ToDisplayString()
					: $"{propertySymbol.ContainingType.ConstructedFrom.ToDisplayString()}.{propertySymbol.Name}.get";

				return MatchesConfiguredKnownPureSignature(signature);
			}

			return false;
		}

		private static bool MatchesKnownPureSignature(string signature)
		{
			return MatchesSignature(Constants.KnownPureBCLMembers, signature) ||
				MatchesSignature(ExtraPureMethods, signature);
		}

		private static bool MatchesConfiguredKnownPureSignature(string signature)
		{
			return MatchesSignature(ExtraPureMethods, signature);
		}

		private static bool MatchesSignature(IEnumerable<string> signatures, string signature)
		{
			if (signatures.Contains(signature))
			{
				return true;
			}

			return signature.IndexOf('?') >= 0 &&
				signatures.Contains(signature.Replace("?", string.Empty));
		}

		public static bool IsKnownImpure(ISymbol symbol)
		{
			if (symbol == null) return false;

			if (GetKnownImpureMemberSource(symbol) != null)
			{
				return true;
			}

			if (symbol is IPropertySymbol property && IsInImpureNamespaceOrType(property.ContainingType))
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Property access {symbol.ToDisplayString()} on known impure type {property.ContainingType.ToDisplayString()}.");
			}

			return false;
		}

		public static string? GetKnownImpureMemberSource(ISymbol symbol)
		{
			if (symbol == null) return null;

			if (IsMutableImmutableBuilderMember(symbol))
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Mutable immutable-builder member detected: {symbol.ToDisplayString()}");
				return "known_impure";
			}

			if (IsImmutableInterlockedMember(symbol))
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: ImmutableInterlocked member detected: {symbol.ToDisplayString()}");
				return "known_impure";
			}

			if (symbol is IMethodSymbol objectEqualsMethodSymbol &&
				objectEqualsMethodSymbol.ContainingType?.SpecialType == SpecialType.System_Object &&
				objectEqualsMethodSymbol.Name == nameof(object.Equals) &&
				objectEqualsMethodSymbol.Parameters.Length == 1)
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Virtual System.Object.Equals dispatch is considered impure: {symbol.ToDisplayString()}");
				return "known_impure";
			}

			if (symbol is IMethodSymbol staticObjectEqualsSymbol &&
				staticObjectEqualsSymbol.ContainingType?.SpecialType == SpecialType.System_Object &&
				staticObjectEqualsSymbol.Name == nameof(object.Equals) &&
				staticObjectEqualsSymbol.IsStatic &&
				staticObjectEqualsSymbol.Parameters.Length == 2)
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Static System.Object.Equals is considered impure due dispatch to virtual instance Equals: {symbol.ToDisplayString()}");
				return "known_impure";
			}

			if (symbol is IMethodSymbol staticTypeGetTypeSymbol &&
				staticTypeGetTypeSymbol.IsStatic &&
				staticTypeGetTypeSymbol.ContainingType?.ToDisplayString().Equals("System.Type", StringComparison.Ordinal) == true &&
				staticTypeGetTypeSymbol.Name == nameof(Type.GetType) &&
				staticTypeGetTypeSymbol.Parameters.Length >= 1 &&
				staticTypeGetTypeSymbol.Parameters[0].Type.SpecialType == SpecialType.System_String)
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Static Type.GetType overload detected as impure: {symbol.ToDisplayString()}");
				return "known_impure";
			}

			string signature = symbol.OriginalDefinition.ToDisplayString();
			if (symbol.Kind == SymbolKind.Property)
			{
				if (!signature.EndsWith(".get") && !signature.EndsWith(".set"))
				{
					signature += ".get";
					PurityAnalysisEngine.LogDebug($"    [IsKnownImpure] Appended .get to property signature: \"{signature}\"");
				}
			}

			PurityAnalysisEngine.LogDebug($"    [IsKnownImpure] Checking HashSet.Contains for signature: \"{signature}\"");
			if (ExtraImpureMethods.Contains(signature))
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Match found for {symbol.ToDisplayString()} using configured full signature '{signature}'");
				return "config_known_impure";
			}

			if (Constants.KnownImpureMethods.Contains(signature))
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Match found for {symbol.ToDisplayString()} using full signature '{signature}'");
				return "known_impure";
			}



			if (symbol.ContainingType != null)
			{
				string simplifiedName = $"{symbol.ContainingType.Name}.{symbol.Name}";
				PurityAnalysisEngine.LogDebug($"    [IsKnownImpure] Checking HashSet.Contains for simplified name: \"{simplifiedName}\"");
				if (ExtraImpureMethods.Contains(simplifiedName))
				{
					PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Match found for {symbol.ToDisplayString()} using configured simplified name '{simplifiedName}'");
					return "config_known_impure";
				}

				if (Constants.KnownImpureMethods.Contains(simplifiedName))
				{
					PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Match found for {symbol.ToDisplayString()} using simplified name '{simplifiedName}'");
					return "known_impure";
				}
			}

			if (symbol is IMethodSymbol methodSymbol && methodSymbol.IsGenericMethod)
			{
				signature = methodSymbol.ConstructedFrom.ToDisplayString();
				if (ExtraImpureMethods.Contains(signature))
				{
					PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Generic match found for {symbol.ToDisplayString()} using configured signature '{signature}'");
					return "config_known_impure";
				}

				if (Constants.KnownImpureMethods.Contains(signature))
				{
					PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Generic match found for {symbol.ToDisplayString()} using signature '{signature}'");
					return "known_impure";
				}
			}

			if (symbol.ContainingType?.ToString().Equals("System.Threading.Interlocked", StringComparison.Ordinal) ?? false)
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Member {symbol.ToDisplayString()} belongs to System.Threading.Interlocked.");
				return "known_impure";
			}

			if (symbol.ContainingType?.ToString().Equals("System.Threading.Volatile", StringComparison.Ordinal) ?? false)
			{
				PurityAnalysisEngine.LogDebug($"Helper IsKnownImpure: Member {symbol.ToDisplayString()} belongs to System.Threading.Volatile and is considered impure.");
				return "known_impure";
			}

			return null;
		}

		private static bool IsKnownPureWebUtilityMethod(ISymbol symbol)
		{
			if (symbol is not IMethodSymbol methodSymbol || !methodSymbol.IsStatic)
			{
				return false;
			}

			if (!string.Equals(methodSymbol.ContainingType?.ToDisplayString(), "System.Net.WebUtility", StringComparison.Ordinal))
			{
				return false;
			}

			if (methodSymbol.Parameters.Length == 1 &&
				methodSymbol.Parameters[0].Type.SpecialType == SpecialType.System_String)
			{
				return methodSymbol.Name is "HtmlEncode" or "HtmlDecode" or "UrlEncode" or "UrlDecode";
			}

			if (methodSymbol.Parameters.Length == 3 &&
				methodSymbol.Parameters[0].Type is IArrayTypeSymbol arrayType &&
				arrayType.ElementType.SpecialType == SpecialType.System_Byte &&
				methodSymbol.Parameters[1].Type.SpecialType == SpecialType.System_Int32 &&
				methodSymbol.Parameters[2].Type.SpecialType == SpecialType.System_Int32)
			{
				return methodSymbol.Name is "UrlEncodeToBytes" or "UrlDecodeToBytes";
			}

			return false;
		}

		public static bool IsInImpureNamespaceOrType(ISymbol symbol)
		{
			if (symbol == null) return false;

			PurityAnalysisEngine.LogDebug($"    [INOT] Checking symbol: {symbol.ToDisplayString()}");
			INamedTypeSymbol? containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
			while (containingType != null)
			{
				string typeName = containingType.OriginalDefinition.ToDisplayString();
				PurityAnalysisEngine.LogDebug($"    [INOT] Checking type: {typeName}");
				PurityAnalysisEngine.LogDebug($"    [INOT] Comparing '{typeName}' against KnownImpureTypeNames...");
				if (Constants.KnownImpureTypeNames.Contains(typeName) || ExtraImpureTypes.Contains(typeName))
				{
					PurityAnalysisEngine.LogDebug($"    [INOT] --> Match found for impure type: {typeName}");
					return true;
				}

				INamespaceSymbol? ns = containingType.ContainingNamespace;
				while (ns != null && !ns.IsGlobalNamespace)
				{
					string namespaceName = ns.ToDisplayString();
					PurityAnalysisEngine.LogDebug($"    [INOT] Checking namespace: {namespaceName}");
					if (Constants.KnownImpureNamespaces.Contains(namespaceName) || ExtraImpureNamespaces.Contains(namespaceName))
					{
						PurityAnalysisEngine.LogDebug($"    [INOT] --> Match found for impure namespace: {namespaceName}");
						return true;
					}
					ns = ns.ContainingNamespace;
				}

				PurityAnalysisEngine.LogDebug($"    [INOT] Checking containing type of {containingType.Name}");
				containingType = containingType.ContainingType;
			}

			PurityAnalysisEngine.LogDebug($"    [INOT] No impure type or namespace match found for: {symbol.ToDisplayString()}");
			return false;
		}

		public static bool IsInConfiguredImpureNamespaceOrType(ISymbol symbol)
		{
			if (symbol == null) return false;

			INamedTypeSymbol? containingType = symbol as INamedTypeSymbol ?? symbol.ContainingType;
			while (containingType != null)
			{
				string typeName = containingType.OriginalDefinition.ToDisplayString();
				if (ExtraImpureTypes.Contains(typeName))
				{
					return true;
				}

				INamespaceSymbol? ns = containingType.ContainingNamespace;
				while (ns != null && !ns.IsGlobalNamespace)
				{
					if (ExtraImpureNamespaces.Contains(ns.ToDisplayString()))
					{
						return true;
					}

					ns = ns.ContainingNamespace;
				}

				containingType = containingType.ContainingType;
			}

			return false;
		}

		private static bool IsMutableImmutableBuilderMember(ISymbol symbol)
		{
			if (!IsImmutableBuilderType(symbol.ContainingType))
			{
				return false;
			}

			if (symbol is IMethodSymbol methodSymbol)
			{
				if (methodSymbol.MethodKind == MethodKind.PropertySet ||
					methodSymbol.MethodKind == MethodKind.EventAdd ||
					methodSymbol.MethodKind == MethodKind.EventRemove)
				{
					return true;
				}

				return methodSymbol.Name is "Add"
					or "AddRange"
					or "Clear"
					or "Insert"
					or "InsertRange"
					or "Remove"
					or "RemoveAll"
					or "RemoveAt"
					or "RemoveRange"
					or "Reverse"
					or "Sort"
					or "UnionWith"
					or "IntersectWith"
					or "ExceptWith"
					or "SymmetricExceptWith";
			}

			if (symbol is IPropertySymbol propertySymbol)
			{
				return propertySymbol.SetMethod != null;
			}

			return false;
		}

		private static bool IsImmutableBuilderType(INamedTypeSymbol? typeSymbol)
		{
			if (typeSymbol == null || !string.Equals(typeSymbol.Name, "Builder", StringComparison.Ordinal))
			{
				return false;
			}

			return typeSymbol.ContainingNamespace?.ToString().StartsWith("System.Collections.Immutable", StringComparison.Ordinal) == true;
		}

		private static bool IsImmutableInterlockedMember(ISymbol symbol)
		{
			return string.Equals(symbol.ContainingType?.ToDisplayString(), "System.Collections.Immutable.ImmutableInterlocked", StringComparison.Ordinal);
		}
	}
}

