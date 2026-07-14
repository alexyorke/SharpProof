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

    [Test]
    public async Task FieldInfoGetValue_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

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

    [Test]
    public async Task PropertyInfoPropertyType_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(PropertyInfo property)
    {
        return property.PropertyType;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeAssembly_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Assembly {|SP0002:TestMethod|}(System.Type type)
    {
        return type.Assembly;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeModule_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Module {|SP0002:TestMethod|}(System.Type type)
    {
        return type.Module;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeGetMethods_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|SP0002:TestMethod|}(System.Type type)
    {
        return type.GetMethods();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetMethods_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMethods();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetEvent_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetConstructorWithTypes_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetProperties_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public PropertyInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetProperties();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetNestedType_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetInterface_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetInterfaceWithIgnoreCase_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetNestedTypeWithBindingFlags_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetEventWithBindingFlags_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeFullName_Diagnostic()
    {
        var test = @"
#nullable enable
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|SP0002:TestMethod|}(System.Type type)
    {
        return type.FullName;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task AssemblyIsDynamic_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(Assembly assembly)
    {
        return assembly.IsDynamic;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeNamespace_Diagnostic()
    {
        var test = @"
#nullable enable
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|SP0002:TestMethod|}(System.Type type)
    {
        return type.Namespace;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeAssemblyQualifiedName_Diagnostic()
    {
        var test = @"
#nullable enable
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|SP0002:TestMethod|}(System.Type type)
    {
        return type.AssemblyQualifiedName;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeBaseType_Diagnostic()
    {
        var test = @"
#nullable enable
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type? {|SP0002:TestMethod|}(System.Type type)
    {
        return type.BaseType;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeUnderlyingSystemType_Diagnostic()
    {
        var test = @"
#nullable enable
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(System.Type type)
    {
        return type.UnderlyingSystemType;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeGuid_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Guid {|SP0002:TestMethod|}(System.Type type)
    {
        return type.GUID;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeTypeInitializer_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeTypeHandle_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public RuntimeTypeHandle {|SP0002:TestMethod|}(System.Type type)
    {
        return type.TypeHandle;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeGenericTypeArguments_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type[] {|SP0002:TestMethod|}(System.Type type)
    {
        return type.GenericTypeArguments;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeContainsGenericParameters_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.ContainsGenericParameters;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeAttributes_NoDiagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TypeAttributes TestMethod(System.Type type)
    {
        return type.Attributes;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsAbstract_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsAbstract;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsClass_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsClass;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsEnum_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsEnum;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsInterface_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsInterface;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsValueType_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsValueType;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoIsValueType_NoDiagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(TypeInfo typeInfo)
    {
        return typeInfo.IsValueType;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeGetTypeInfo_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public TypeInfo {|SP0002:TestMethod|}(System.Type type)
    {
        return type.GetTypeInfo();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeGetType_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeGetTypeWithThrowOnError_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName, true);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeGetTypeWithThrowOnErrorAndIgnoreCase_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName, true, true);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeGetTypeWithAssemblyAndTypeResolvers_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName, _ => typeof(object).Assembly, (_, _, _) => typeof(object), false);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeGetTypeWithAssemblyAndTypeResolversIgnoreCase_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|SP0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName, _ => typeof(object).Assembly, (_, _, _) => typeof(object), false, false);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsArray_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsArray;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsPrimitive_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsPrimitive;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsByRef_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsByRef;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsPointer_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsPointer;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsSealed_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsSealed;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsConstructedGenericType_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsConstructedGenericType;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsConstructedGenericType_OnTypeOfClosedGeneric_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return typeof(List<int>).IsConstructedGenericType;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsConstructedGenericType_OnTypeFromHandle_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(RuntimeTypeHandle handle)
    {
        return Type.GetTypeFromHandle(handle).IsConstructedGenericType;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsNested_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNested;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsPublic_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsPublic;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsNotPublic_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNotPublic;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsVisible_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsVisible;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsNestedPublic_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNestedPublic;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsNestedAssembly_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNestedAssembly;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsNestedFamily_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNestedFamily;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsNestedPrivate_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNestedPrivate;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsNestedFamANDAssem_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNestedFamANDAssem;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsNestedFamORAssem_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsNestedFamORAssem;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsAutoLayout_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsAutoLayout;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsLayoutSequential_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsLayoutSequential;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsExplicitLayout_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsExplicitLayout;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsAnsiClass_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsAnsiClass;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsUnicodeClass_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsUnicodeClass;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsAutoClass_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsAutoClass;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsImport_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsImport;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsSerializable_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsSerializable;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsSpecialName_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsSpecialName;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeHasElementType_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.HasElementType;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsCOMObject_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsCOMObject;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsByRefLike_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsByRefLike;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsSZArray_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsSZArray;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsVariableBoundArray_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsVariableBoundArray;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsTypeDefinition_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsTypeDefinition;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsSecurityCritical_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsSecurityCritical;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsSecuritySafeCritical_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsSecuritySafeCritical;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeIsSecurityTransparent_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(System.Type type)
    {
        return type.IsSecurityTransparent;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeGenericParameterPosition_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(System.Type type)
    {
        return type.GenericParameterPosition;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeGenericParameterAttributes_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Reflection.GenericParameterAttributes {|SP0002:TestMethod|}(System.Type type)
    {
        return type.GenericParameterAttributes;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeStructLayoutAttribute_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Runtime.InteropServices.StructLayoutAttribute {|SP0002:TestMethod|}(System.Type type)
    {
        return type.StructLayoutAttribute;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeDefaultBinder_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Reflection.Binder TestMethod()
    {
        return System.Type.DefaultBinder;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodBaseGetParameters_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public ParameterInfo[] {|SP0002:TestMethod|}(MethodBase method)
    {
        return method.GetParameters();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodBaseIsStatic_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(MethodBase method)
    {
        return method.IsStatic;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodInfoGetBaseDefinition_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(MethodInfo method)
    {
        return method.GetBaseDefinition();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodBaseGetMethodBody_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodBody {|SP0002:TestMethod|}(MethodBase method)
    {
        return method.GetMethodBody();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodInfoGetGenericMethodDefinition_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(MethodInfo method)
    {
        return method.GetGenericMethodDefinition();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodBaseGetGenericArguments_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodBaseGetMethodImplementationFlags_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodImplAttributes {|SP0002:TestMethod|}(MethodBase method)
    {
        return method.GetMethodImplementationFlags();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodBaseGetMethodFromHandle_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodBaseInvoke_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}(MethodBase method, object target, object[] arguments)
    {
        return method.Invoke(target, arguments);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodInfoMakeGenericMethod_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodInfoCreateDelegate_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodBaseGetMethodFromHandleWithTypeHandle_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MethodInfoCreateDelegateWithTarget_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task ConstructorInfoInvoke_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}(ConstructorInfo constructor, object[] arguments)
    {
        return constructor.Invoke(arguments);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoGetAddMethod_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetAddMethod();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoGetAddMethodOverload_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetAddMethod(true);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoGetRemoveMethod_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetRemoveMethod();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoGetRemoveMethodOverload_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetRemoveMethod(true);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoGetRaiseMethod_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetRaiseMethod();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoGetRaiseMethodOverload_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetRaiseMethod(true);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoGetOtherMethods_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetOtherMethods();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoGetOtherMethodsOverload_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetOtherMethods(true);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoIsDefined_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoGetCustomAttributesData_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task AttributeIsDefinedOnMemberInfo_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task MemberInfoName_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(MemberInfo member)
    {
        return member.Name;
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task AttributeGetCustomAttributesOnMemberInfo_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoGetCustomAttributesInherited_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object[] {|SP0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetCustomAttributes(false);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoAddEventHandler_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task EventInfoRemoveEventHandler_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task FieldInfoSetValue_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(FieldInfo field, object target, object value)
    {
        field.SetValue(target, value);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task FieldInfoGetRawConstantValue_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}(FieldInfo field)
    {
        return field.GetRawConstantValue();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task ParameterInfoGetRequiredCustomModifiers_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task ParameterInfoGetOptionalCustomModifiers_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task ParameterInfoGetCustomAttributesData_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task ParameterInfoIsDefined_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task ParameterInfoGetCustomAttributes_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task ParameterInfoGetCustomAttributesInherited_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object[] {|SP0002:TestMethod|}(ParameterInfo parameter)
    {
        return parameter.GetCustomAttributes(false);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetFields_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public FieldInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetFields();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetConstructors_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConstructorInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetConstructors();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetMembers_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMembers();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetEvents_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public EventInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetEvents();
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetInterfaces_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetNestedTypes_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetField_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetProperty_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetMethod_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetFieldsWithBindingFlags_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public FieldInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetFields(BindingFlags.Public);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetMethodsWithBindingFlags_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMethods(BindingFlags.Public);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetPropertiesWithBindingFlags_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public PropertyInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetProperties(BindingFlags.Public);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetMembersWithBindingFlags_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMembers(BindingFlags.Public);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetEventsWithBindingFlags_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public EventInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetEvents(BindingFlags.Public);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetConstructorsWithBindingFlags_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConstructorInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetConstructors(BindingFlags.Public);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetNestedTypesWithBindingFlags_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetFieldWithBindingFlags_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetPropertyWithBindingFlags_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetMethodWithBindingFlags_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetMember_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMember(""ToString"");
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetMemberWithBindingFlags_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMember(""ToString"", BindingFlags.Public);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetMemberWithMemberTypesAndBindingFlags_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|SP0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMember(""ToString"", MemberTypes.Method, BindingFlags.Public);
    }
}";

        await AssertReflectionDiagnosticsAsync(test);
    }

    [Test]
    public async Task TypeInfoGetMethodWithTypes_Diagnostic()
    {
        var test = @"
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
}";

        await AssertReflectionDiagnosticsAsync(test);
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