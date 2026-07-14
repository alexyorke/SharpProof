using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class ReflectionTests
{
    // NUnit supplies case-level concurrency; nested Roslyn concurrency for these
    // single-tree compilations only oversubscribes the test lane.
    private static readonly ImmutableArray<MetadataReference> ReflectionFrameworkReferences =
        AnalyzerTestHost.GetMinimalFrameworkReferences();

    public sealed record ReflectionScenario(string Name, string MarkedSource);

    private static readonly ReflectionScenario[] ReflectionScenarioData =
    [
        new("FieldInfoGetValue_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class Data
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public object? {|SP0002:TestMethod|}(FieldInfo field, Data data)
    {
        return field.GetValue(data);
    }
}"),
        new("PropertyInfoPropertyType_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(PropertyInfo property)
    {
        return property.PropertyType;
    }
}"),
        new("TypeAssembly_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Assembly {|SP0002:TestMethod|}(System.Type type)
    {
        return type.Assembly;
    }
}"),
        new("TypeModule_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Module {|SP0002:TestMethod|}(System.Type type)
    {
        return type.Module;
    }
}"),
        new("TypeGetMethods_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|SP0002:TestMethod|}(System.Type type)
    {
        return type.GetMethods();
    }
}"),
        new("TypeInfoGetMethods_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMethods();
    }
}"),
        new("TypeInfoGetEvent_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public EventInfo? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetEvent(""Changed"");
    }
}"),
        new("TypeInfoGetConstructorWithTypes_Diagnostic", @"
#nullable enable
using System;
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConstructorInfo? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetConstructor(Type.EmptyTypes);
    }
}"),
        new("TypeInfoGetProperties_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public PropertyInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetProperties();
    }
}"),
        new("TypeInfoGetNestedType_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetNestedType(""Inner"");
    }
}"),
        new("TypeInfoGetInterface_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetInterface(""IDisposable"");
    }
}"),
        new("TypeInfoGetInterfaceWithIgnoreCase_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetInterface(""idisposable"", true);
    }
}"),
        new("TypeInfoGetNestedTypeWithBindingFlags_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetNestedType(""Inner"", BindingFlags.Public);
    }
}"),
        new("TypeInfoGetEventWithBindingFlags_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public EventInfo? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetEvent(""Changed"", BindingFlags.Public);
    }
}"),
        new("TypeFullName_Diagnostic", @"
#nullable enable
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|SP0002:TestMethod|}(System.Type type)
    {
        return type.FullName;
    }
}"),
        new("AssemblyIsDynamic_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(Assembly assembly)
    {
        return assembly.IsDynamic;
    }
}"),
        new("TypeNamespace_Diagnostic", @"
#nullable enable
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|SP0002:TestMethod|}(System.Type type)
    {
        return type.Namespace;
    }
}"),
        new("TypeAssemblyQualifiedName_Diagnostic", @"
#nullable enable
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|SP0002:TestMethod|}(System.Type type)
    {
        return type.AssemblyQualifiedName;
    }
}"),
        new("TypeBaseType_Diagnostic", @"
#nullable enable
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type? {|SP0002:TestMethod|}(System.Type type)
    {
        return type.BaseType;
    }
}"),
        new("TypeUnderlyingSystemType_Diagnostic", @"
#nullable enable
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(System.Type type)
    {
        return type.UnderlyingSystemType;
    }
}"),
        new("TypeGuid_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Guid {|SP0002:TestMethod|}(System.Type type)
    {
        return type.GUID;
    }
}"),
        new("TypeTypeInitializer_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConstructorInfo? {|SP0002:TestMethod|}(System.Type type)
    {
        return type.TypeInitializer;
    }
}"),
        new("TypeTypeHandle_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public RuntimeTypeHandle {|SP0002:TestMethod|}(System.Type type)
    {
        return type.TypeHandle;
    }
}"),
        new("TypeGenericTypeArguments_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type[] {|SP0002:TestMethod|}(System.Type type)
    {
        return type.GenericTypeArguments;
    }
}"),
        new("TypeContainsGenericParameters_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.ContainsGenericParameters;
    }
}"),
        new("TypeAttributes_NoDiagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TypeAttributes TestMethod(System.Type type)
    {
        return type.Attributes;
    }
}"),
        new("TypeIsAbstract_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsAbstract;
    }
}"),
        new("TypeIsClass_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsClass;
    }
}"),
        new("TypeIsEnum_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsEnum;
    }
}"),
        new("TypeIsInterface_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsInterface;
    }
}"),
        new("TypeIsValueType_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsValueType;
    }
}"),
        new("TypeInfoIsValueType_NoDiagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(TypeInfo typeInfo)
    {
        return typeInfo.IsValueType;
    }
}"),
        new("TypeGetTypeInfo_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TypeInfo {|SP0002:TestMethod|}(System.Type type)
    {
        return type.GetTypeInfo();
    }
}"),
        new("TypeGetType_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName);
    }
}"),
        new("TypeGetTypeWithThrowOnError_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName, true);
    }
}"),
        new("TypeGetTypeWithThrowOnErrorAndIgnoreCase_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName, true, true);
    }
}"),
        new("TypeGetTypeWithAssemblyAndTypeResolvers_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName, _ => typeof(object).Assembly, (_, _, _) => typeof(object), false);
    }
}"),
        new("TypeGetTypeWithAssemblyAndTypeResolversIgnoreCase_Diagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName, _ => typeof(object).Assembly, (_, _, _) => typeof(object), false, false);
    }
}"),
        new("TypeIsArray_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsArray;
    }
}"),
        new("TypeIsPrimitive_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsPrimitive;
    }
}"),
        new("TypeIsByRef_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsByRef;
    }
}"),
        new("TypeIsPointer_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsPointer;
    }
}"),
        new("TypeIsSealed_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsSealed;
    }
}"),
        new("TypeIsConstructedGenericType_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsConstructedGenericType;
    }
}"),
        new("TypeIsConstructedGenericType_OnTypeOfClosedGeneric_NoDiagnostic", @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return typeof(List<int>).IsConstructedGenericType;
    }
}"),
        new("TypeIsConstructedGenericType_OnTypeFromHandle_NoDiagnostic", @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(RuntimeTypeHandle handle)
    {
        return Type.GetTypeFromHandle(handle).IsConstructedGenericType;
    }
}"),
        new("TypeIsNested_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNested;
    }
}"),
        new("TypeIsPublic_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsPublic;
    }
}"),
        new("TypeIsNotPublic_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNotPublic;
    }
}"),
        new("TypeIsVisible_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsVisible;
    }
}"),
        new("TypeIsNestedPublic_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNestedPublic;
    }
}"),
        new("TypeIsNestedAssembly_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNestedAssembly;
    }
}"),
        new("TypeIsNestedFamily_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNestedFamily;
    }
}"),
        new("TypeIsNestedPrivate_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNestedPrivate;
    }
}"),
        new("TypeIsNestedFamANDAssem_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNestedFamANDAssem;
    }
}"),
        new("TypeIsNestedFamORAssem_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNestedFamORAssem;
    }
}"),
        new("TypeIsAutoLayout_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsAutoLayout;
    }
}"),
        new("TypeIsLayoutSequential_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsLayoutSequential;
    }
}"),
        new("TypeIsExplicitLayout_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsExplicitLayout;
    }
}"),
        new("TypeIsAnsiClass_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsAnsiClass;
    }
}"),
        new("TypeIsUnicodeClass_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsUnicodeClass;
    }
}"),
        new("TypeIsAutoClass_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsAutoClass;
    }
}"),
        new("TypeIsImport_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsImport;
    }
}"),
        new("TypeIsSerializable_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsSerializable;
    }
}"),
        new("TypeIsSpecialName_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsSpecialName;
    }
}"),
        new("TypeHasElementType_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.HasElementType;
    }
}"),
        new("TypeIsCOMObject_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsCOMObject;
    }
}"),
        new("TypeIsByRefLike_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsByRefLike;
    }
}"),
        new("TypeIsSZArray_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsSZArray;
    }
}"),
        new("TypeIsVariableBoundArray_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsVariableBoundArray;
    }
}"),
        new("TypeIsTypeDefinition_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsTypeDefinition;
    }
}"),
        new("TypeIsSecurityCritical_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsSecurityCritical;
    }
}"),
        new("TypeIsSecuritySafeCritical_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsSecuritySafeCritical;
    }
}"),
        new("TypeIsSecurityTransparent_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsSecurityTransparent;
    }
}"),
        new("TypeGenericParameterPosition_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(System.Type type)
    {
        return type.GenericParameterPosition;
    }
}"),
        new("TypeGenericParameterAttributes_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Reflection.GenericParameterAttributes {|SP0002:TestMethod|}(System.Type type)
    {
        return type.GenericParameterAttributes;
    }
}"),
        new("TypeStructLayoutAttribute_Diagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Runtime.InteropServices.StructLayoutAttribute {|SP0002:TestMethod|}(System.Type type)
    {
        return type.StructLayoutAttribute;
    }
}"),
        new("TypeDefaultBinder_NoDiagnostic", @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Reflection.Binder TestMethod()
    {
        return System.Type.DefaultBinder;
    }
}"),
        new("MethodBaseGetParameters_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public ParameterInfo[] {|SP0002:TestMethod|}(MethodBase method)
    {
        return method.GetParameters();
    }
}"),
        new("MethodBaseIsStatic_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(MethodBase method)
    {
        return method.IsStatic;
    }
}"),
        new("MethodInfoGetBaseDefinition_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(MethodInfo method)
    {
        return method.GetBaseDefinition();
    }
}"),
        new("MethodBaseGetMethodBody_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodBody {|SP0002:TestMethod|}(MethodBase method)
    {
        return method.GetMethodBody();
    }
}"),
        new("MethodInfoGetGenericMethodDefinition_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(MethodInfo method)
    {
        return method.GetGenericMethodDefinition();
    }
}"),
        new("MethodBaseGetGenericArguments_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public Type[] {|SP0002:TestMethod|}(MethodBase method)
    {
        return method.GetGenericArguments();
    }
}"),
        new("MethodBaseGetMethodImplementationFlags_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodImplAttributes {|SP0002:TestMethod|}(MethodBase method)
    {
        return method.GetMethodImplementationFlags();
    }
}"),
        new("MethodBaseGetMethodFromHandle_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodBase {|SP0002:TestMethod|}(RuntimeMethodHandle handle)
    {
        return MethodBase.GetMethodFromHandle(handle);
    }
}"),
        new("MethodBaseInvoke_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}(MethodBase method, object target, object[] arguments)
    {
        return method.Invoke(target, arguments);
    }
}"),
        new("MethodInfoMakeGenericMethod_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(MethodInfo method, Type[] types)
    {
        return method.MakeGenericMethod(types);
    }
}"),
        new("MethodInfoCreateDelegate_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public Delegate {|SP0002:TestMethod|}(MethodInfo method, Type delegateType)
    {
        return method.CreateDelegate(delegateType);
    }
}"),
        new("MethodBaseGetMethodFromHandleWithTypeHandle_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodBase {|SP0002:TestMethod|}(RuntimeMethodHandle handle, RuntimeTypeHandle typeHandle)
    {
        return MethodBase.GetMethodFromHandle(handle, typeHandle);
    }
}"),
        new("MethodInfoCreateDelegateWithTarget_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public Delegate {|SP0002:TestMethod|}(MethodInfo method, Type delegateType, object target)
    {
        return method.CreateDelegate(delegateType, target);
    }
}"),
        new("ConstructorInfoInvoke_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}(ConstructorInfo constructor, object[] arguments)
    {
        return constructor.Invoke(arguments);
    }
}"),
        new("EventInfoGetAddMethod_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetAddMethod();
    }
}"),
        new("EventInfoGetAddMethodOverload_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetAddMethod(true);
    }
}"),
        new("EventInfoGetRemoveMethod_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetRemoveMethod();
    }
}"),
        new("EventInfoGetRemoveMethodOverload_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetRemoveMethod(true);
    }
}"),
        new("EventInfoGetRaiseMethod_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetRaiseMethod();
    }
}"),
        new("EventInfoGetRaiseMethodOverload_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetRaiseMethod(true);
    }
}"),
        new("EventInfoGetOtherMethods_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetOtherMethods();
    }
}"),
        new("EventInfoGetOtherMethodsOverload_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetOtherMethods(true);
    }
}"),
        new("EventInfoIsDefined_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(EventInfo eventInfo, Type attributeType)
    {
        return eventInfo.IsDefined(attributeType, false);
    }
}"),
        new("EventInfoGetCustomAttributesData_Diagnostic", @"
using SharpProof.Attributes;
using System.Collections.Generic;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public IList<CustomAttributeData> {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetCustomAttributesData();
    }
}"),
        new("AttributeIsDefinedOnMemberInfo_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(MemberInfo member, Type attributeType)
    {
        return Attribute.IsDefined(member, attributeType);
    }
}"),
        new("MemberInfoName_NoDiagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(MemberInfo member)
    {
        return member.Name;
    }
}"),
        new("AttributeGetCustomAttributesOnMemberInfo_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object[] {|SP0002:TestMethod|}(MemberInfo member)
    {
        return Attribute.GetCustomAttributes(member);
    }
}"),
        new("EventInfoGetCustomAttributesInherited_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object[] {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetCustomAttributes(false);
    }
}"),
        new("EventInfoAddEventHandler_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(EventInfo eventInfo, object target, Delegate handler)
    {
        eventInfo.AddEventHandler(target, handler);
    }
}"),
        new("EventInfoRemoveEventHandler_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(EventInfo eventInfo, object target, Delegate handler)
    {
        eventInfo.RemoveEventHandler(target, handler);
    }
}"),
        new("FieldInfoSetValue_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(FieldInfo field, object target, object value)
    {
        field.SetValue(target, value);
    }
}"),
        new("FieldInfoGetRawConstantValue_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}(FieldInfo field)
    {
        return field.GetRawConstantValue();
    }
}"),
        new("ParameterInfoGetRequiredCustomModifiers_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public Type[] {|SP0002:TestMethod|}(ParameterInfo parameter)
    {
        return parameter.GetRequiredCustomModifiers();
    }
}"),
        new("ParameterInfoGetOptionalCustomModifiers_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public Type[] {|SP0002:TestMethod|}(ParameterInfo parameter)
    {
        return parameter.GetOptionalCustomModifiers();
    }
}"),
        new("ParameterInfoGetCustomAttributesData_Diagnostic", @"
using SharpProof.Attributes;
using System.Collections.Generic;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public IList<CustomAttributeData> {|SP0002:TestMethod|}(ParameterInfo parameter)
    {
        return parameter.GetCustomAttributesData();
    }
}"),
        new("ParameterInfoIsDefined_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ParameterInfo parameter, Type attributeType)
    {
        return parameter.IsDefined(attributeType, false);
    }
}"),
        new("ParameterInfoGetCustomAttributes_Diagnostic", @"
using SharpProof.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object[] {|SP0002:TestMethod|}(ParameterInfo parameter, Type attributeType)
    {
        return parameter.GetCustomAttributes(attributeType, false);
    }
}"),
        new("ParameterInfoGetCustomAttributesInherited_Diagnostic", @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object[] {|SP0002:TestMethod|}(ParameterInfo parameter)
    {
        return parameter.GetCustomAttributes(false);
    }
}"),
        new("TypeInfoGetFields_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public FieldInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetFields();
    }
}"),
        new("TypeInfoGetConstructors_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConstructorInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetConstructors();
    }
}"),
        new("TypeInfoGetMembers_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMembers();
    }
}"),
        new("TypeInfoGetEvents_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public EventInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetEvents();
    }
}"),
        new("TypeInfoGetInterfaces_Diagnostic", @"
using System;
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetInterfaces();
    }
}"),
        new("TypeInfoGetNestedTypes_Diagnostic", @"
using System;
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetNestedTypes();
    }
}"),
        new("TypeInfoGetField_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public FieldInfo? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetField(""Value"");
    }
}"),
        new("TypeInfoGetProperty_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public PropertyInfo? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetProperty(""Value"");
    }
}"),
        new("TypeInfoGetMethod_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMethod(""ToString"");
    }
}"),
        new("TypeInfoGetFieldsWithBindingFlags_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public FieldInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetFields(BindingFlags.Public);
    }
}"),
        new("TypeInfoGetMethodsWithBindingFlags_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMethods(BindingFlags.Public);
    }
}"),
        new("TypeInfoGetPropertiesWithBindingFlags_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public PropertyInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetProperties(BindingFlags.Public);
    }
}"),
        new("TypeInfoGetMembersWithBindingFlags_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMembers(BindingFlags.Public);
    }
}"),
        new("TypeInfoGetEventsWithBindingFlags_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public EventInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetEvents(BindingFlags.Public);
    }
}"),
        new("TypeInfoGetConstructorsWithBindingFlags_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConstructorInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetConstructors(BindingFlags.Public);
    }
}"),
        new("TypeInfoGetNestedTypesWithBindingFlags_Diagnostic", @"
using System;
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetNestedTypes(BindingFlags.Public);
    }
}"),
        new("TypeInfoGetFieldWithBindingFlags_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public FieldInfo? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetField(""Value"", BindingFlags.Public);
    }
}"),
        new("TypeInfoGetPropertyWithBindingFlags_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public PropertyInfo? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetProperty(""Value"", BindingFlags.Public);
    }
}"),
        new("TypeInfoGetMethodWithBindingFlags_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMethod(""ToString"", BindingFlags.Public);
    }
}"),
        new("TypeInfoGetMember_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMember(""ToString"");
    }
}"),
        new("TypeInfoGetMemberWithBindingFlags_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMember(""ToString"", BindingFlags.Public);
    }
}"),
        new("TypeInfoGetMemberWithMemberTypesAndBindingFlags_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMember(""ToString"", MemberTypes.Method, BindingFlags.Public);
    }
}"),
        new("TypeInfoGetMethodWithTypes_Diagnostic", @"
#nullable enable
using System;
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo? {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMethod(""ToString"", Type.EmptyTypes);
    }
}"),
    ];

    private static IEnumerable<TestCaseData> ReflectionScenarios
    {
        get
        {
            if (ReflectionScenarioData.Length != 139)
                throw new InvalidOperationException("Expected 139 reflection scenarios.");

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scenario in ReflectionScenarioData)
            {
                if (!names.Add(scenario.Name))
                    throw new InvalidOperationException("Duplicate reflection scenario '" + scenario.Name + "'.");
                yield return new TestCaseData(scenario).SetName(scenario.Name);
            }
        }
    }

    [TestCaseSource(nameof(ReflectionScenarios))]
    public Task ReflectionScenarioDiagnostics(ReflectionScenario scenario) =>
        AssertReflectionDiagnosticsAsync(scenario.MarkedSource);

    [Test]
    public async Task PropertyInfoGetValue_Diagnostic()
    {
        var test = @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

public class Data
{
    public int Value { get; set; }
}

public class TestClass
{
    [EnforcePure]
    public object? TestMethod(PropertyInfo property, Data data)
    {
        return property.GetValue(data);
    }
}";

        var expectedGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(8, 16, 8, 21)
            .WithArguments("get_Value");
        var expectedMethod = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(14, 20, 14, 30)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetter, expectedMethod);
    }

    private static async Task AssertReflectionDiagnosticsAsync(string markedSource)
    {
        var (_, diagnostic) = await AnalyzerTestHost.AssertOptionalSingleSp0002Async(
            markedSource,
            ReflectionFrameworkReferences,
            false);
        if (diagnostic != null)
            Assert.That(
                diagnostic.Properties.ContainsKey(SharpProofDiagnostics.ImpuritySymbolProperty),
                Is.True);
    }
}
