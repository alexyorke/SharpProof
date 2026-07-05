using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer
{
    internal static class EffectSummarySymbolKeyFactory
    {
        private static readonly SymbolDisplayFormat EffectSummaryContainingTypeFormat = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        private static readonly SymbolDisplayFormat EffectSummaryNonGenericContainingTypeFormat = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.None,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        private static readonly SymbolDisplayFormat EffectSummaryParameterTypeFormat = new SymbolDisplayFormat(
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        public static IEnumerable<string> GetMethodSymbolKeys(IMethodSymbol methodSymbol)
        {
            var keys = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            AddSymbolKey(keys, methodSymbol.OriginalDefinition.ToDisplayString());
            AddSymbolKey(keys, methodSymbol.ToDisplayString());
            AddSymbolKey(keys, CreateEffectSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreateEffectSummaryKey(methodSymbol));
            AddSymbolKey(keys, CreateExactSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreateExactSummaryKey(methodSymbol));
            AddSymbolKey(keys, CreateMetadataEffectSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreateMetadataEffectSummaryKey(methodSymbol));
            AddSymbolKey(keys, CreateMetadataExactSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreateMetadataExactSummaryKey(methodSymbol));
            AddSymbolKey(keys, CreateMetadataDefinitionEffectSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreateMetadataDefinitionEffectSummaryKey(methodSymbol));
            AddSymbolKey(keys, CreateMetadataDefinitionExactSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreateMetadataDefinitionExactSummaryKey(methodSymbol));
            AddSymbolKey(keys, CreatePositionalEffectSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreatePositionalEffectSummaryKey(methodSymbol));
            AddSymbolKey(keys, CreatePositionalExactSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreatePositionalExactSummaryKey(methodSymbol));
            AddSymbolKey(keys, CreateMetadataPositionalEffectSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreateMetadataPositionalEffectSummaryKey(methodSymbol));
            AddSymbolKey(keys, CreateMetadataPositionalExactSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreateMetadataPositionalExactSummaryKey(methodSymbol));

            if (methodSymbol.IsGenericMethod)
            {
                AddSymbolKey(keys, methodSymbol.ConstructedFrom.ToDisplayString());
                AddSymbolKey(keys, CreateEffectSummaryKey(methodSymbol.ConstructedFrom));
                AddSymbolKey(keys, CreateExactSummaryKey(methodSymbol.ConstructedFrom));
                AddSymbolKey(keys, CreateMetadataEffectSummaryKey(methodSymbol.ConstructedFrom));
                AddSymbolKey(keys, CreateMetadataExactSummaryKey(methodSymbol.ConstructedFrom));
                AddSymbolKey(keys, CreateMetadataDefinitionEffectSummaryKey(methodSymbol.ConstructedFrom));
                AddSymbolKey(keys, CreateMetadataDefinitionExactSummaryKey(methodSymbol.ConstructedFrom));
                AddSymbolKey(keys, CreatePositionalEffectSummaryKey(methodSymbol.ConstructedFrom));
                AddSymbolKey(keys, CreatePositionalExactSummaryKey(methodSymbol.ConstructedFrom));
                AddSymbolKey(keys, CreateMetadataPositionalEffectSummaryKey(methodSymbol.ConstructedFrom));
                AddSymbolKey(keys, CreateMetadataPositionalExactSummaryKey(methodSymbol.ConstructedFrom));
            }

            return keys;
        }

        internal static ImmutableArray<string> GetMethodSymbolKeysWithAlternateContainingType(
            IMethodSymbol methodSymbol,
            string alternateContainingType)
        {
            var originalContainingType = methodSymbol.ContainingType?.ToDisplayString(EffectSummaryContainingTypeFormat);
            if (string.IsNullOrWhiteSpace(originalContainingType))
            {
                return ImmutableArray<string>.Empty;
            }

            var originalPrefix = originalContainingType + ".";
            var alternatePrefix = alternateContainingType + ".";
            var keys = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (var key in GetMethodSymbolKeys(methodSymbol))
            {
                if (!key.StartsWith(originalPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                AddSymbolKey(keys, alternatePrefix + key.Substring(originalPrefix.Length));
            }

            return keys.ToImmutableArray();
        }

        internal static string GetMetadataDefinitionExactMethodKey(IMethodSymbol methodSymbol)
        {
            return CreateMetadataDefinitionExactSummaryKey(methodSymbol);
        }

        private static void AddSymbolKey(ImmutableHashSet<string>.Builder keys, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var trimmed = value!.Trim();
                keys.Add(trimmed);

                foreach (var compatibilityKey in GetMetadataRefKindCompatibilityKeys(trimmed))
                {
                    keys.Add(compatibilityKey);
                }
            }
        }

        private static IEnumerable<string> GetMetadataRefKindCompatibilityKeys(string key)
        {
            if (key.Contains("out ", StringComparison.Ordinal))
            {
                yield return key.Replace("out ", "ref ");
            }

            if (key.Contains("ref ", StringComparison.Ordinal) &&
                !key.Contains("ref readonly ", StringComparison.Ordinal))
            {
                yield return key.Replace("ref ", "out ");
            }
        }

        private static string CreateEffectSummaryKey(IMethodSymbol methodSymbol)
        {
            var containingTypeName = methodSymbol.ContainingType.ToDisplayString(EffectSummaryContainingTypeFormat);
            var methodName = methodSymbol.MethodKind == MethodKind.Constructor
                ? ".ctor"
                : methodSymbol.Name;
            var parameterList = string.Join(
                ", ",
                methodSymbol.Parameters.Select(parameter => parameter.Type.ToDisplayString(EffectSummaryParameterTypeFormat)));
            return containingTypeName + "." + methodName + "(" + parameterList + ")";
        }

        private static string CreateExactSummaryKey(IMethodSymbol methodSymbol)
        {
            var containingTypeName = methodSymbol.ContainingType.ToDisplayString(EffectSummaryContainingTypeFormat);
            var methodName = methodSymbol.MethodKind == MethodKind.Constructor
                ? ".ctor"
                : methodSymbol.Name;
            var parameterList = string.Join(
                ", ",
                methodSymbol.Parameters.Select(parameter => parameter.Type.ToDisplayString(EffectSummaryParameterTypeFormat)));
            var returnType = methodSymbol.MethodKind == MethodKind.Constructor
                ? "void"
                : methodSymbol.ReturnType.ToDisplayString(EffectSummaryParameterTypeFormat);
            return containingTypeName + "." + methodName + "(" + parameterList + ")->" + returnType;
        }

        private static string CreateMetadataEffectSummaryKey(IMethodSymbol methodSymbol)
        {
            return CreateMetadataSummaryKey(methodSymbol, includeReturnType: false, useOrdinalGenericParameters: false);
        }

        private static string CreateMetadataExactSummaryKey(IMethodSymbol methodSymbol)
        {
            return CreateMetadataSummaryKey(methodSymbol, includeReturnType: true, useOrdinalGenericParameters: false);
        }

        private static string CreatePositionalEffectSummaryKey(IMethodSymbol methodSymbol)
        {
            return CreatePositionalSummaryKey(methodSymbol, includeReturnType: false);
        }

        private static string CreatePositionalExactSummaryKey(IMethodSymbol methodSymbol)
        {
            return CreatePositionalSummaryKey(methodSymbol, includeReturnType: true);
        }

        private static string CreateMetadataPositionalEffectSummaryKey(IMethodSymbol methodSymbol)
        {
            return CreateMetadataSummaryKey(methodSymbol, includeReturnType: false, useOrdinalGenericParameters: true);
        }

        private static string CreateMetadataPositionalExactSummaryKey(IMethodSymbol methodSymbol)
        {
            return CreateMetadataSummaryKey(methodSymbol, includeReturnType: true, useOrdinalGenericParameters: true);
        }

        private static string CreateMetadataDefinitionEffectSummaryKey(IMethodSymbol methodSymbol)
        {
            return CreateMetadataDefinitionSummaryKey(methodSymbol, includeReturnType: false);
        }

        private static string CreateMetadataDefinitionExactSummaryKey(IMethodSymbol methodSymbol)
        {
            return CreateMetadataDefinitionSummaryKey(methodSymbol, includeReturnType: true);
        }

        private static string CreatePositionalSummaryKey(IMethodSymbol methodSymbol, bool includeReturnType)
        {
            var containingTypeName = FormatSummaryType(methodSymbol.ContainingType, useOrdinalGenericParameters: true);
            var methodName = methodSymbol.MethodKind == MethodKind.Constructor
                ? ".ctor"
                : methodSymbol.Name;
            var parameterList = string.Join(
                ", ",
                methodSymbol.Parameters.Select(parameter => FormatSummaryParameter(parameter, useOrdinalGenericParameters: true)));
            if (!includeReturnType)
            {
                return containingTypeName + "." + methodName + "(" + parameterList + ")";
            }

            var returnType = methodSymbol.MethodKind == MethodKind.Constructor
                ? "void"
                : FormatSummaryReturnType(methodSymbol, useOrdinalGenericParameters: true);
            return containingTypeName + "." + methodName + "(" + parameterList + ")->" + returnType;
        }

        private static string CreateMetadataSummaryKey(
            IMethodSymbol methodSymbol,
            bool includeReturnType,
            bool useOrdinalGenericParameters)
        {
            var containingTypeName = FormatSummaryType(
                methodSymbol.ContainingType,
                useOrdinalGenericParameters,
                useMetadataTypeNames: true);
            var methodName = methodSymbol.MethodKind == MethodKind.Constructor
                ? ".ctor"
                : methodSymbol.Name;
            var parameterList = string.Join(
                ", ",
                methodSymbol.Parameters.Select(parameter => FormatSummaryParameter(
                    parameter,
                    useOrdinalGenericParameters,
                    useMetadataTypeNames: true)));
            if (!includeReturnType)
            {
                return containingTypeName + "." + methodName + "(" + parameterList + ")";
            }

            var returnType = methodSymbol.MethodKind == MethodKind.Constructor
                ? "void"
                : FormatSummaryReturnType(
                    methodSymbol,
                    useOrdinalGenericParameters,
                    useMetadataTypeNames: true);
            return containingTypeName + "." + methodName + "(" + parameterList + ")->" + returnType;
        }

        private static string CreateMetadataDefinitionSummaryKey(IMethodSymbol methodSymbol, bool includeReturnType)
        {
            var containingTypeName = GetMetadataGenericDefinitionName(methodSymbol.ContainingType);
            var methodName = methodSymbol.MethodKind == MethodKind.Constructor
                ? ".ctor"
                : methodSymbol.Name;
            var parameterList = string.Join(
                ", ",
                methodSymbol.Parameters.Select(parameter => FormatSummaryParameter(
                    parameter,
                    useOrdinalGenericParameters: true,
                    useMetadataTypeNames: true)));
            if (!includeReturnType)
            {
                return containingTypeName + "." + methodName + "(" + parameterList + ")";
            }

            var returnType = methodSymbol.MethodKind == MethodKind.Constructor
                ? "void"
                : FormatSummaryReturnType(
                    methodSymbol,
                    useOrdinalGenericParameters: true,
                    useMetadataTypeNames: true);
            return containingTypeName + "." + methodName + "(" + parameterList + ")->" + returnType;
        }

        private static string FormatSummaryReturnType(
            IMethodSymbol methodSymbol,
            bool useOrdinalGenericParameters,
            bool useMetadataTypeNames = false)
        {
            var returnType = FormatSummaryType(methodSymbol.ReturnType, useOrdinalGenericParameters, useMetadataTypeNames);
            return PrefixRefKind(methodSymbol.ReturnsByRefReadonly ? RefKind.RefReadOnlyParameter :
                methodSymbol.ReturnsByRef ? RefKind.Ref : RefKind.None) + returnType;
        }

        private static string FormatSummaryParameter(
            IParameterSymbol parameter,
            bool useOrdinalGenericParameters,
            bool useMetadataTypeNames = false)
        {
            return PrefixRefKind(parameter.RefKind) +
                FormatSummaryType(parameter.Type, useOrdinalGenericParameters, useMetadataTypeNames);
        }

        private static string PrefixRefKind(RefKind refKind)
        {
            return refKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                RefKind.RefReadOnlyParameter => "ref readonly ",
                _ => string.Empty,
            };
        }

        private static string FormatSummaryType(
            ITypeSymbol typeSymbol,
            bool useOrdinalGenericParameters,
            bool useMetadataTypeNames = false)
        {
            switch (typeSymbol)
            {
                case IArrayTypeSymbol arrayType:
                    return FormatSummaryType(arrayType.ElementType, useOrdinalGenericParameters, useMetadataTypeNames) +
                        "[" + new string(',', Math.Max(arrayType.Rank, 1) - 1) + "]";
                case IPointerTypeSymbol pointerType:
                    return FormatSummaryType(pointerType.PointedAtType, useOrdinalGenericParameters, useMetadataTypeNames) + "*";
                case ITypeParameterSymbol typeParameter:
                    if (!useOrdinalGenericParameters)
                    {
                        return typeParameter.Name;
                    }

                    return typeParameter.TypeParameterKind == TypeParameterKind.Method
                        ? "!!" + typeParameter.Ordinal
                        : "!" + typeParameter.Ordinal;
                case INamedTypeSymbol namedType when useMetadataTypeNames && namedType.SpecialType != SpecialType.None:
                    return namedType.ToDisplayString(EffectSummaryParameterTypeFormat);
                case INamedTypeSymbol namedType when namedType.IsTupleType && !useMetadataTypeNames:
                    return namedType.ToDisplayString(EffectSummaryParameterTypeFormat);
                case INamedTypeSymbol namedType:
                    var typeName = useMetadataTypeNames
                        ? GetMetadataGenericDefinitionName(namedType)
                        : namedType.ConstructedFrom.ToDisplayString(EffectSummaryNonGenericContainingTypeFormat);
                    var typeArguments = useMetadataTypeNames
                        ? GetFlattenedTypeArguments(namedType)
                        : namedType.TypeArguments;
                    if (typeArguments.Length == 0)
                    {
                        return typeName;
                    }

                    var formattedTypeArguments = string.Join(
                        ", ",
                        typeArguments.Select(argument => FormatSummaryType(argument, useOrdinalGenericParameters, useMetadataTypeNames)));
                    return typeName + "<" + formattedTypeArguments + ">";
                default:
                    return typeSymbol.ToDisplayString(EffectSummaryParameterTypeFormat);
            }
        }

        private static string GetMetadataGenericDefinitionName(INamedTypeSymbol namedType)
        {
            var definition = namedType.ConstructedFrom;
            if (definition.ContainingType != null)
            {
                return GetMetadataGenericDefinitionName(definition.ContainingType) + "+" + definition.MetadataName;
            }

            var containingNamespace = definition.ContainingNamespace?.ToDisplayString();
            return string.IsNullOrWhiteSpace(containingNamespace)
                ? definition.MetadataName
                : containingNamespace + "." + definition.MetadataName;
        }

        private static ImmutableArray<ITypeSymbol> GetFlattenedTypeArguments(INamedTypeSymbol namedType)
        {
            var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
            AppendFlattenedTypeArguments(namedType, builder);
            return builder.ToImmutable();
        }

        private static void AppendFlattenedTypeArguments(INamedTypeSymbol namedType, ImmutableArray<ITypeSymbol>.Builder builder)
        {
            if (namedType.ContainingType != null)
            {
                AppendFlattenedTypeArguments(namedType.ContainingType, builder);
            }

            foreach (var typeArgument in namedType.TypeArguments)
            {
                builder.Add(typeArgument);
            }
        }
    }
}
