using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ReflectionTests
    {
        [Test]
        public async Task FieldInfoGetValue_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class Data
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public object? {|PS0002:TestMethod|}(FieldInfo field, Data data)
    {
        return field.GetValue(data);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task PropertyInfoGetValue_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

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

            var expectedGetter = VerifyCS.Diagnostic(PurelySharpDiagnostics.MissingEnforcePureAttributeId)
                .WithSpan(8, 16, 8, 21)
                .WithArguments("get_Value");
            var expectedMethod = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                .WithSpan(14, 20, 14, 30)
                .WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedGetter, expectedMethod);
        }

        [Test]
        public async Task PropertyInfoPropertyType_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|PS0002:TestMethod|}(PropertyInfo property)
    {
        return property.PropertyType;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeAssembly_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Assembly {|PS0002:TestMethod|}(System.Type type)
    {
        return type.Assembly;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeModule_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Module {|PS0002:TestMethod|}(System.Type type)
    {
        return type.Module;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetMethods_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMethods();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetEvent_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public EventInfo? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetEvent(""Changed"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetConstructorWithTypes_Diagnostic()
        {
            var test = @"
#nullable enable
using System;
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConstructorInfo? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetConstructor(Type.EmptyTypes);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetProperties_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public PropertyInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetProperties();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetNestedType_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetNestedType(""Inner"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetInterface_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetInterface(""IDisposable"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetInterfaceWithIgnoreCase_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetInterface(""idisposable"", true);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetNestedTypeWithBindingFlags_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetNestedType(""Inner"", BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetEventWithBindingFlags_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public EventInfo? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetEvent(""Changed"", BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeFullName_Diagnostic()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|PS0002:TestMethod|}(System.Type type)
    {
        return type.FullName;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeNamespace_Diagnostic()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|PS0002:TestMethod|}(System.Type type)
    {
        return type.Namespace;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeToString_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(System.Type type)
    {
        return type.ToString();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeAssemblyQualifiedName_Diagnostic()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? {|PS0002:TestMethod|}(System.Type type)
    {
        return type.AssemblyQualifiedName;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeBaseType_Diagnostic()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type? {|PS0002:TestMethod|}(System.Type type)
    {
        return type.BaseType;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeUnderlyingSystemType_Diagnostic()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|PS0002:TestMethod|}(System.Type type)
    {
        return type.UnderlyingSystemType;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeGuid_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Guid {|PS0002:TestMethod|}(System.Type type)
    {
        return type.GUID;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeTypeInitializer_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConstructorInfo? {|PS0002:TestMethod|}(System.Type type)
    {
        return type.TypeInitializer;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeTypeHandle_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public RuntimeTypeHandle {|PS0002:TestMethod|}(System.Type type)
    {
        return type.TypeHandle;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeGenericTypeArguments_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type[] {|PS0002:TestMethod|}(System.Type type)
    {
        return type.GenericTypeArguments;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeContainsGenericParameters_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.ContainsGenericParameters;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeAttributes_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TypeAttributes {|PS0002:TestMethod|}(System.Type type)
    {
        return type.Attributes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsAbstract_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsAbstract;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsClass_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsClass;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsEnum_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsEnum;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsInterface_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsInterface;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsGenericType_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsGenericType;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsValueType_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsValueType;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoIsValueType_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.IsValueType;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeGetTypeInfo_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TypeInfo {|PS0002:TestMethod|}(System.Type type)
    {
        return type.GetTypeInfo();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeGetTypeFromHandle_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type TestMethod(RuntimeTypeHandle handle)
    {
        return Type.GetTypeFromHandle(handle);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeGetType_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|PS0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeGetTypeWithThrowOnError_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|PS0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName, true);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeGetTypeWithThrowOnErrorAndIgnoreCase_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|PS0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName, true, true);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeGetTypeWithAssemblyAndTypeResolvers_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|PS0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName, _ => typeof(object).Assembly, (_, _, _) => typeof(object), false);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeGetTypeWithAssemblyAndTypeResolversIgnoreCase_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type {|PS0002:TestMethod|}(string typeName)
    {
        return Type.GetType(typeName, _ => typeof(object).Assembly, (_, _, _) => typeof(object), false, false);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsArray_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsArray;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsPrimitive_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsPrimitive;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsByRef_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsByRef;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsPointer_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsPointer;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsSealed_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsSealed;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsGenericTypeDefinition_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsGenericTypeDefinition;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsConstructedGenericType_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsConstructedGenericType;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsGenericParameter_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsGenericParameter;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsNested_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsNested;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsPublic_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsPublic;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsNotPublic_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsNotPublic;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsVisible_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsVisible;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsNestedPublic_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsNestedPublic;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsNestedAssembly_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsNestedAssembly;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsNestedFamily_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsNestedFamily;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsNestedPrivate_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsNestedPrivate;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsNestedFamANDAssem_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsNestedFamANDAssem;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsNestedFamORAssem_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsNestedFamORAssem;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsAutoLayout_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsAutoLayout;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsLayoutSequential_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsLayoutSequential;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsExplicitLayout_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsExplicitLayout;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsAnsiClass_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsAnsiClass;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsUnicodeClass_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsUnicodeClass;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsAutoClass_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsAutoClass;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsImport_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsImport;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsMarshalByRef_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsMarshalByRef;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsSerializable_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsSerializable;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsSpecialName_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsSpecialName;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeHasElementType_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.HasElementType;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsCOMObject_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsCOMObject;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsByRefLike_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsByRefLike;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsSZArray_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsSZArray;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsVariableBoundArray_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsVariableBoundArray;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsTypeDefinition_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsTypeDefinition;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsContextful_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(System.Type type)
    {
        return type.IsContextful;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsSecurityCritical_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsSecurityCritical;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsSecuritySafeCritical_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsSecuritySafeCritical;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeIsSecurityTransparent_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(System.Type type)
    {
        return type.IsSecurityTransparent;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeGenericParameterPosition_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(System.Type type)
    {
        return type.GenericParameterPosition;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeGenericParameterAttributes_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Reflection.GenericParameterAttributes {|PS0002:TestMethod|}(System.Type type)
    {
        return type.GenericParameterAttributes;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeDeclaringMethod_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Reflection.MethodBase TestMethod(System.Type type)
    {
        return type.DeclaringMethod;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeStructLayoutAttribute_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Runtime.InteropServices.StructLayoutAttribute {|PS0002:TestMethod|}(System.Type type)
    {
        return type.StructLayoutAttribute;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeDefaultBinder_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Reflection.Binder {|PS0002:TestMethod|}()
    {
        return System.Type.DefaultBinder;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeDeclaringType_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type TestMethod(System.Type type)
    {
        return type.DeclaringType;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeReflectedType_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Type TestMethod(System.Type type)
    {
        return type.ReflectedType;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeMemberType_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MemberTypes TestMethod(System.Type type)
    {
        return type.MemberType;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodBaseGetParameters_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public ParameterInfo[] {|PS0002:TestMethod|}(MethodBase method)
    {
        return method.GetParameters();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodBaseIsStatic_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(MethodBase method)
    {
        return method.IsStatic;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodInfoGetBaseDefinition_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|PS0002:TestMethod|}(MethodInfo method)
    {
        return method.GetBaseDefinition();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodBaseGetMethodBody_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodBody {|PS0002:TestMethod|}(MethodBase method)
    {
        return method.GetMethodBody();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodInfoGetGenericMethodDefinition_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|PS0002:TestMethod|}(MethodInfo method)
    {
        return method.GetGenericMethodDefinition();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodBaseGetGenericArguments_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public Type[] {|PS0002:TestMethod|}(MethodBase method)
    {
        return method.GetGenericArguments();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodBaseGetMethodImplementationFlags_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodImplAttributes {|PS0002:TestMethod|}(MethodBase method)
    {
        return method.GetMethodImplementationFlags();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodBaseGetMethodFromHandle_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodBase {|PS0002:TestMethod|}(RuntimeMethodHandle handle)
    {
        return MethodBase.GetMethodFromHandle(handle);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodBaseInvoke_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object {|PS0002:TestMethod|}(MethodBase method, object target, object[] arguments)
    {
        return method.Invoke(target, arguments);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodInfoMakeGenericMethod_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|PS0002:TestMethod|}(MethodInfo method, Type[] types)
    {
        return method.MakeGenericMethod(types);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodInfoCreateDelegate_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public Delegate {|PS0002:TestMethod|}(MethodInfo method, Type delegateType)
    {
        return method.CreateDelegate(delegateType);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodBaseGetMethodFromHandleWithTypeHandle_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodBase {|PS0002:TestMethod|}(RuntimeMethodHandle handle, RuntimeTypeHandle typeHandle)
    {
        return MethodBase.GetMethodFromHandle(handle, typeHandle);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MethodInfoCreateDelegateWithTarget_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public Delegate {|PS0002:TestMethod|}(MethodInfo method, Type delegateType, object target)
    {
        return method.CreateDelegate(delegateType, target);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConstructorInfoInvoke_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object {|PS0002:TestMethod|}(ConstructorInfo constructor, object[] arguments)
    {
        return constructor.Invoke(arguments);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoGetAddMethod_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|PS0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetAddMethod();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoGetAddMethodOverload_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|PS0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetAddMethod(true);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoGetRemoveMethod_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|PS0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetRemoveMethod();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoGetRemoveMethodOverload_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|PS0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetRemoveMethod(true);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoGetRaiseMethod_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|PS0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetRaiseMethod();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoGetRaiseMethodOverload_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo {|PS0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetRaiseMethod(true);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoGetOtherMethods_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|PS0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetOtherMethods();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoGetOtherMethodsOverload_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|PS0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetOtherMethods(true);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoIsDefined_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(EventInfo eventInfo, Type attributeType)
    {
        return eventInfo.IsDefined(attributeType, false);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoGetCustomAttributesData_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Collections.Generic;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public IList<CustomAttributeData> {|PS0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetCustomAttributesData();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AttributeIsDefinedOnMemberInfo_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(MemberInfo member, Type attributeType)
    {
        return Attribute.IsDefined(member, attributeType);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task MemberInfoName_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public string {|PS0002:TestMethod|}(MemberInfo member)
    {
        return member.Name;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AttributeGetCustomAttributesOnMemberInfo_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object[] {|PS0002:TestMethod|}(MemberInfo member)
    {
        return Attribute.GetCustomAttributes(member);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AttributeGetCustomAttributeOnMemberInfo_Diagnostic()
        {
            var test = @"
#nullable enable
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public Attribute? {|PS0002:TestMethod|}(MemberInfo member, Type attributeType)
    {
        return Attribute.GetCustomAttribute(member, attributeType);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task CustomAttributeDataGetCustomAttributesOnMemberInfo_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Collections.Generic;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public IList<CustomAttributeData> {|PS0002:TestMethod|}(MemberInfo member)
    {
        return CustomAttributeData.GetCustomAttributes(member);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoGetCustomAttributesInherited_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object[] {|PS0002:TestMethod|}(EventInfo eventInfo)
    {
        return eventInfo.GetCustomAttributes(false);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoAddEventHandler_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(EventInfo eventInfo, object target, Delegate handler)
    {
        eventInfo.AddEventHandler(target, handler);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task EventInfoRemoveEventHandler_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(EventInfo eventInfo, object target, Delegate handler)
    {
        eventInfo.RemoveEventHandler(target, handler);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task FieldInfoSetValue_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(FieldInfo field, object target, object value)
    {
        field.SetValue(target, value);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task FieldInfoGetRawConstantValue_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object {|PS0002:TestMethod|}(FieldInfo field)
    {
        return field.GetRawConstantValue();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ParameterInfoGetRequiredCustomModifiers_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public Type[] {|PS0002:TestMethod|}(ParameterInfo parameter)
    {
        return parameter.GetRequiredCustomModifiers();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ParameterInfoGetOptionalCustomModifiers_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public Type[] {|PS0002:TestMethod|}(ParameterInfo parameter)
    {
        return parameter.GetOptionalCustomModifiers();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ParameterInfoGetCustomAttributesData_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Collections.Generic;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public IList<CustomAttributeData> {|PS0002:TestMethod|}(ParameterInfo parameter)
    {
        return parameter.GetCustomAttributesData();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ParameterInfoIsDefined_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(ParameterInfo parameter, Type attributeType)
    {
        return parameter.IsDefined(attributeType, false);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ParameterInfoGetCustomAttributes_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object[] {|PS0002:TestMethod|}(ParameterInfo parameter, Type attributeType)
    {
        return parameter.GetCustomAttributes(attributeType, false);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ParameterInfoGetCustomAttributesInherited_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;
using System.Reflection;

public class TestClass
{
    [EnforcePure]
    public object[] {|PS0002:TestMethod|}(ParameterInfo parameter)
    {
        return parameter.GetCustomAttributes(false);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetFields_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public FieldInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetFields();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetConstructors_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConstructorInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetConstructors();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetMembers_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMembers();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetEvents_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public EventInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetEvents();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetInterfaces_Diagnostic()
        {
            var test = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetInterfaces();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetNestedTypes_Diagnostic()
        {
            var test = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetNestedTypes();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetField_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public FieldInfo? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetField(""Value"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetProperty_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public PropertyInfo? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetProperty(""Value"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetMethod_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMethod(""ToString"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetFieldsWithBindingFlags_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public FieldInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetFields(BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetMethodsWithBindingFlags_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMethods(BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetPropertiesWithBindingFlags_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public PropertyInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetProperties(BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetMembersWithBindingFlags_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMembers(BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetEventsWithBindingFlags_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public EventInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetEvents(BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetConstructorsWithBindingFlags_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConstructorInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetConstructors(BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetNestedTypesWithBindingFlags_Diagnostic()
        {
            var test = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetNestedTypes(BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetFieldWithBindingFlags_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public FieldInfo? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetField(""Value"", BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetPropertyWithBindingFlags_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public PropertyInfo? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetProperty(""Value"", BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetMethodWithBindingFlags_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMethod(""ToString"", BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetMember_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMember(""ToString"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetMemberWithBindingFlags_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMember(""ToString"", BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetMemberWithMemberTypesAndBindingFlags_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemberInfo[] {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMember(""ToString"", MemberTypes.Method, BindingFlags.Public);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TypeInfoGetMethodWithTypes_Diagnostic()
        {
            var test = @"
#nullable enable
using System;
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public MethodInfo? {|PS0002:TestMethod|}(TypeInfo typeInfo)
    {
        return typeInfo.GetMethod(""ToString"", Type.EmptyTypes);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
