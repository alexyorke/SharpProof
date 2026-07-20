namespace SharpProof.Analyzer.Engine.Rules;

internal partial class PropertyReferencePurityRule {
    private static bool IsTrustedGeneratedMetadataGetter(IMethodSymbol getterSymbol) {
        var containingType = getterSymbol.ContainingType?.OriginalDefinition.ToDisplayString();
        if (containingType == "System.Type")
            return getterSymbol.Name is
                "get_Attributes" or
                "get_DeclaringMethod" or
                "get_DeclaringType" or
                "get_IsAbstract" or
                "get_IsAnsiClass" or
                "get_IsArray" or
                "get_IsAutoClass" or
                "get_IsAutoLayout" or
                "get_IsByRef" or
                "get_IsClass" or
                "get_IsCOMObject" or
                "get_IsContextful" or
                "get_IsExplicitLayout" or
                "get_IsGenericParameter" or
                "get_IsGenericType" or
                "get_IsGenericTypeDefinition" or
                "get_IsImport" or
                "get_IsInterface" or
                "get_IsLayoutSequential" or
                "get_IsMarshalByRef" or
                "get_IsNested" or
                "get_IsNestedAssembly" or
                "get_IsNestedFamANDAssem" or
                "get_IsNestedFamORAssem" or
                "get_IsNestedFamily" or
                "get_IsNestedPrivate" or
                "get_IsNestedPublic" or
                "get_IsNotPublic" or
                "get_IsPointer" or
                "get_IsPrimitive" or
                "get_IsPublic" or
                "get_IsSealed" or
                "get_IsSpecialName" or
                "get_IsUnicodeClass" or
                "get_IsValueType" or
                "get_MemberType" or
                "get_ReflectedType";

        return containingType == "System.RuntimeType" ||
               containingType == "System.Reflection.MemberInfo" ||
               containingType?.StartsWith("System.Reflection.", StringComparison.Ordinal) == true;
    }
}