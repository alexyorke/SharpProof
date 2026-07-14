using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class AssemblyLoadingTests
{
    private const int ExpectedScenarioCount = 70;

    private static readonly (string Name, string Source)[] AssemblyLoadingScenarioData =
    [
        ("Assembly_GetExecutingAssembly_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|SP0002:TestMethod|}()
        {
            // Assembly.GetExecutingAssembly() interacts with runtime state
            return Assembly.GetExecutingAssembly();
        }
    }
}"),
        ("Assembly_GetCallingAssembly_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|SP0002:TestMethod|}()
        {
            return Assembly.GetCallingAssembly();
        }
    }
}"),
        ("Assembly_GetEntryAssembly_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly? {|SP0002:TestMethod|}()
        {
            return Assembly.GetEntryAssembly();
        }
    }
}"),
        ("Assembly_Load_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|SP0002:TestMethod|}(string assemblyString)
        {
            return Assembly.Load(assemblyString);
        }
    }
}"),
        ("Assembly_LoadFrom_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|SP0002:TestMethod|}(string assemblyFile)
        {
            return Assembly.LoadFrom(assemblyFile);
        }
    }
}"),
        ("Assembly_GetTypes_Diagnostic", @"
using System;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Type[] {|SP0002:TestMethod|}(Assembly assembly)
        {
            // Assembly.GetTypes() might load dependent assemblies, potentially impure
            return assembly.GetTypes();
        }
    }
            }"),
        ("Assembly_GetExportedTypes_Diagnostic", @"
using System;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Type[] {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetExportedTypes();
        }
    }
}"),
        ("Assembly_GetReferencedAssemblies_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyName[] {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetReferencedAssemblies();
        }
    }
}"),
        ("AssemblyName_Constructor_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyName {|SP0002:TestMethod|}()
        {
            return new AssemblyName(""SharpProof"");
        }
    }
}"),
        ("DynamicMethod_Constructor_Diagnostic", @"
using System;
using System.Reflection.Emit;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public DynamicMethod {|SP0002:TestMethod|}()
        {
            return new DynamicMethod(""SharpProof"", typeof(void), Type.EmptyTypes);
        }
    }
}"),
        ("AssemblyName_GetAssemblyName_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyName {|SP0002:TestMethod|}(string path)
        {
            return AssemblyName.GetAssemblyName(path);
        }
    }
}"),
        ("AssemblyBuilder_DefineDynamicModule_Diagnostic", @"
using System.Reflection.Emit;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public ModuleBuilder {|SP0002:TestMethod|}(AssemblyBuilder builder)
        {
            return builder.DefineDynamicModule(""DynamicModule"");
        }
    }
}"),
        ("ILGenerator_Emit_Diagnostic", @"
using System.Reflection.Emit;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public void {|SP0002:TestMethod|}(ILGenerator il)
        {
            il.Emit(OpCodes.Ret);
        }
    }
}"),
        ("AssemblyLoadContext_Default_Diagnostic", @"
using System.Runtime.Loader;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyLoadContext {|SP0002:TestMethod|}()
        {
            return AssemblyLoadContext.Default;
        }
    }
}"),
        ("AssemblyLoadContext_DerivedConstructor_Diagnostic", @"
using System.Runtime.Loader;
using SharpProof.Attributes;

namespace TestNamespace
{
    public sealed class DerivedLoadContext : AssemblyLoadContext
    {
        public DerivedLoadContext() : base(""test"", isCollectible: true)
        {
        }
    }

    public class TestClass
    {
        [EnforcePure]
        public DerivedLoadContext {|SP0002:TestMethod|}()
        {
            return new DerivedLoadContext();
        }
    }
}"),
        ("AssemblyLoadContext_All_Diagnostic", @"
using System.Collections.Generic;
using System.Runtime.Loader;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public IEnumerable<AssemblyLoadContext> {|SP0002:TestMethod|}()
        {
            return AssemblyLoadContext.All;
        }
    }
}"),
        ("AssemblyLoadContext_LoadFromAssemblyPath_Diagnostic", @"
using System.Reflection;
using System.Runtime.Loader;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|SP0002:TestMethod|}(AssemblyLoadContext context, string path)
        {
            return context.LoadFromAssemblyPath(path);
        }
    }
}"),
        ("AssemblyLoadContext_LoadFromStream_Diagnostic", @"
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|SP0002:TestMethod|}(AssemblyLoadContext context, Stream stream)
        {
            return context.LoadFromStream(stream);
        }
    }
}"),
        ("AssemblyLoadContext_LoadFromStreamWithSymbols_Diagnostic", @"
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|SP0002:TestMethod|}(AssemblyLoadContext context, Stream assemblyStream, Stream symbolsStream)
        {
            return context.LoadFromStream(assemblyStream, symbolsStream);
        }
    }
}"),
        ("AssemblyLoadContext_LoadFromAssemblyName_Diagnostic", @"
using System.Reflection;
using System.Runtime.Loader;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|SP0002:TestMethod|}(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            return context.LoadFromAssemblyName(assemblyName);
        }
    }
}"),
        ("AssemblyLoadContext_LoadFromNativeImagePath_Diagnostic", @"
using System.Reflection;
using System.Runtime.Loader;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|SP0002:TestMethod|}(AssemblyLoadContext context, string nativeImagePath, string assemblyPath)
        {
            return context.LoadFromNativeImagePath(nativeImagePath, assemblyPath);
        }
    }
}"),
        ("AssemblyLoadContext_GetLoadContext_Diagnostic", @"
#nullable enable
using System.Reflection;
using System.Runtime.Loader;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyLoadContext? {|SP0002:TestMethod|}(Assembly assembly)
        {
            return AssemblyLoadContext.GetLoadContext(assembly);
        }
    }
}"),
        ("AssemblyLoadContext_CurrentContextualReflectionContext_Diagnostic", @"
#nullable enable
using System.Runtime.Loader;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyLoadContext? {|SP0002:TestMethod|}()
        {
            return AssemblyLoadContext.CurrentContextualReflectionContext;
        }
    }
}"),
        ("AssemblyLoadContext_EnterContextualReflection_Diagnostic", @"
using System.Runtime.Loader;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyLoadContext.ContextualReflectionScope {|SP0002:TestMethod|}(AssemblyLoadContext context)
        {
            return context.EnterContextualReflection();
        }
    }
}"),
        ("AssemblyLoadContext_EnterContextualReflectionForAssembly_Diagnostic", @"
using System.Reflection;
using System.Runtime.Loader;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyLoadContext.ContextualReflectionScope {|SP0002:TestMethod|}(Assembly assembly)
        {
            return AssemblyLoadContext.EnterContextualReflection(assembly);
        }
    }
}"),
        ("Assembly_GetManifestResourceNames_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string[] {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetManifestResourceNames();
        }
    }
}"),
        ("Assembly_GetLoadedModules_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Module[] {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetLoadedModules();
        }
    }
}"),
        ("Assembly_GetModules_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Module[] {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetModules();
        }
    }
}"),
        ("Assembly_DefinedTypes_Diagnostic", @"
using System;
using System.Collections.Generic;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public IEnumerable<TypeInfo> {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.DefinedTypes;
        }
    }
}"),
        ("Assembly_ExportedTypes_Diagnostic", @"
using System;
using System.Collections.Generic;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public IEnumerable<Type> {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.ExportedTypes;
        }
    }
}"),
        ("Assembly_Modules_Diagnostic", @"
using System.Collections.Generic;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public IEnumerable<Module> {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.Modules;
        }
    }
}"),
        ("Assembly_ManifestModule_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Module {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.ManifestModule;
        }
    }
}"),
        ("Assembly_EntryPoint_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public MethodInfo? {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.EntryPoint;
        }
    }
}"),
        ("Assembly_CustomAttributes_Diagnostic", @"
using System.Collections.Generic;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public IEnumerable<CustomAttributeData> {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.CustomAttributes;
        }
    }
}"),
        ("Assembly_Location_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.Location;
        }
    }
}"),
        ("Assembly_IsDynamic_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public bool {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.IsDynamic;
        }
    }
}"),
        ("Assembly_IsFullyTrusted_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public bool {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.IsFullyTrusted;
        }
    }
}"),
        ("Assembly_HostContext_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public long {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.HostContext;
        }
    }
}"),
        ("Assembly_GlobalAssemblyCache_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public bool {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GlobalAssemblyCache;
        }
    }
}"),
        ("Assembly_ReflectionOnly_Diagnostic", @"
using System.Reflection;
using System.Security;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public bool {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.ReflectionOnly;
        }
    }
}"),
        ("Assembly_SecurityRuleSet_Diagnostic", @"
using System.Reflection;
using System.Security;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public SecurityRuleSet {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.SecurityRuleSet;
        }
    }
}"),
        ("Assembly_CodeBase_Diagnostic", @"
#pragma warning disable SYSLIB0012
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.CodeBase;
        }
    }
}"),
        ("Assembly_EscapedCodeBase_Diagnostic", @"
#pragma warning disable SYSLIB0012
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.EscapedCodeBase;
        }
    }
}"),
        ("Assembly_GetName_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyName {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetName();
        }
    }
}"),
        ("Assembly_GetFiles_Diagnostic", @"
using System.IO;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public FileStream[] {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetFiles();
        }
    }
}"),
        ("Assembly_GetModule_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Module? {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetModule(""MainModule"");
        }
    }
}"),
        ("Assembly_GetFile_Diagnostic", @"
#nullable enable
using System.IO;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public FileStream? {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetFile(""data.bin"");
        }
    }
}"),
        ("Assembly_GetManifestResourceInfo_Diagnostic", @"
#nullable enable
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public ManifestResourceInfo? {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetManifestResourceInfo(""asset.txt"");
        }
    }
}"),
        ("Assembly_GetManifestResourceStream_Diagnostic", @"
#nullable enable
using System.IO;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Stream? {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetManifestResourceStream(""asset.txt"");
        }
    }
}"),
        ("Assembly_GetModules_Overload_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Module[] {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetModules(true);
        }
    }
}"),
        ("Assembly_GetLoadedModules_Overload_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Module[] {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetLoadedModules(true);
        }
    }
}"),
        ("Assembly_GetSatelliteAssembly_Diagnostic", @"
using System.Globalization;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetSatelliteAssembly(CultureInfo.InvariantCulture);
        }
    }
}"),
        ("Assembly_GetSatelliteAssembly_Overload_Diagnostic", @"
using System;
using System.Globalization;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|SP0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetSatelliteAssembly(CultureInfo.InvariantCulture, new Version(1, 0));
        }
    }
}"),
        ("Module_Assembly_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|SP0002:TestMethod|}(Module module)
        {
            return module.Assembly;
        }
    }
}"),
        ("Module_FullyQualifiedName_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|SP0002:TestMethod|}(Module module)
        {
            return module.FullyQualifiedName;
        }
    }
}"),
        ("Module_Name_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|SP0002:TestMethod|}(Module module)
        {
            return module.Name;
        }
    }
}"),
        ("Module_ScopeName_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|SP0002:TestMethod|}(Module module)
        {
            return module.ScopeName;
        }
    }
}"),
        ("Module_ModuleVersionId_Diagnostic", @"
using System;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Guid {|SP0002:TestMethod|}(Module module)
        {
            return module.ModuleVersionId;
        }
    }
}"),
        ("Module_GetTypes_Diagnostic", @"
using System;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Type[] {|SP0002:TestMethod|}(Module module)
        {
            return module.GetTypes();
        }
    }
}"),
        ("Module_GetType_Diagnostic", @"
#nullable enable
using System;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Type? {|SP0002:TestMethod|}(Module module)
        {
            return module.GetType(""TestNamespace.TestClass"");
        }
    }
}"),
        ("Module_ResolveMethod_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public MethodBase {|SP0002:TestMethod|}(Module module)
        {
            return module.ResolveMethod(0);
        }
    }
}"),
        ("Module_ResolveType_Diagnostic", @"
using System;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Type {|SP0002:TestMethod|}(Module module)
        {
            return module.ResolveType(0);
        }
    }
}"),
        ("Module_ResolveField_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public FieldInfo {|SP0002:TestMethod|}(Module module)
        {
            return module.ResolveField(0);
        }
    }
}"),
        ("Module_ResolveMember_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public MemberInfo {|SP0002:TestMethod|}(Module module)
        {
            return module.ResolveMember(0);
        }
    }
}"),
        ("Module_ResolveString_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|SP0002:TestMethod|}(Module module)
        {
            return module.ResolveString(0);
        }
    }
}"),
        ("Module_ResolveSignature_Diagnostic", @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public byte[] {|SP0002:TestMethod|}(Module module)
        {
            return module.ResolveSignature(0);
        }
    }
}"),
        ("Module_ResolveMethod_Overload_Diagnostic", @"
using System;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public MethodBase {|SP0002:TestMethod|}(Module module)
        {
            return module.ResolveMethod(0, Array.Empty<Type>(), Array.Empty<Type>());
        }
    }
}"),
        ("Module_ResolveType_Overload_Diagnostic", @"
using System;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Type {|SP0002:TestMethod|}(Module module)
        {
            return module.ResolveType(0, Array.Empty<Type>(), Array.Empty<Type>());
        }
    }
}"),
        ("Module_ResolveField_Overload_Diagnostic", @"
using System;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public FieldInfo {|SP0002:TestMethod|}(Module module)
        {
            return module.ResolveField(0, Array.Empty<Type>(), Array.Empty<Type>());
        }
    }
}"),
        ("Module_ResolveMember_Overload_Diagnostic", @"
using System;
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public MemberInfo {|SP0002:TestMethod|}(Module module)
        {
            return module.ResolveMember(0, Array.Empty<Type>(), Array.Empty<Type>());
        }
    }
}"),
    ];

    private static IEnumerable<TestCaseData> AssemblyLoadingScenarios
    {
        get
        {
            if (AssemblyLoadingScenarioData.Length != ExpectedScenarioCount)
            {
                throw new InvalidOperationException(
                    $"Expected {ExpectedScenarioCount} assembly loading scenarios, but found {AssemblyLoadingScenarioData.Length}.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scenario in AssemblyLoadingScenarioData)
            {
                if (!names.Add(scenario.Name))
                {
                    throw new InvalidOperationException($"Duplicate assembly loading scenario name: {scenario.Name}");
                }

                yield return new TestCaseData(scenario.Source).SetName(scenario.Name);
            }
        }
    }

    [TestCaseSource(nameof(AssemblyLoadingScenarios))]
    public Task AssemblyLoadingScenario(string markedSource) =>
        AssertAssemblyLoadingDiagnosticsAsync(markedSource);







































































    [Test]
    public async Task Assembly_LoadFile_Diagnostic()
    {
        var test = @"
using System.Reflection;
using SharpProof.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly TestMethod(string path)
        {
            // Assembly.LoadFile involves IO and is impure
            return Assembly.LoadFile(path);
        }
    }
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(10, 25, 10, 35)
            .WithArguments("TestMethod");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    private static async Task AssertAssemblyLoadingDiagnosticsAsync(string markedSource)
    {
        await AnalyzerTestHost.AssertOptionalSingleSp0002Async(markedSource);
    }
}