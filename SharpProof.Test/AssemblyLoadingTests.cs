using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

#nullable enable

namespace SharpProof.Test
{
    [TestFixture]
    public class AssemblyLoadingTests
    {





        [Test]
        public async Task Assembly_GetExecutingAssembly_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetCallingAssembly_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetEntryAssembly_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_Load_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_LoadFrom_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }


        [Test]
        public async Task Assembly_GetTypes_Diagnostic()
        {
            var test = @"
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
            }";
            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetExportedTypes_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetReferencedAssemblies_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyName_Constructor_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task DynamicMethod_Constructor_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyName_GetAssemblyName_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyBuilder_DefineDynamicModule_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task ILGenerator_Emit_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_Default_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_DerivedConstructor_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_All_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_LoadFromAssemblyPath_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_LoadFromStream_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_LoadFromStreamWithSymbols_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_LoadFromAssemblyName_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_LoadFromNativeImagePath_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_GetLoadContext_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_CurrentContextualReflectionContext_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_EnterContextualReflection_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_EnterContextualReflectionForAssembly_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetManifestResourceNames_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetLoadedModules_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetModules_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_DefinedTypes_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_ExportedTypes_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_Modules_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_ManifestModule_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_EntryPoint_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_CustomAttributes_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_Location_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_IsDynamic_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_IsFullyTrusted_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_HostContext_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GlobalAssemblyCache_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_ReflectionOnly_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_SecurityRuleSet_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_CodeBase_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_EscapedCodeBase_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetName_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetFiles_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetModule_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetFile_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetManifestResourceInfo_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetManifestResourceStream_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetModules_Overload_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetLoadedModules_Overload_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetSatelliteAssembly_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Assembly_GetSatelliteAssembly_Overload_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_Assembly_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_FullyQualifiedName_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_Name_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_ScopeName_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_ModuleVersionId_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_GetTypes_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_GetType_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_ResolveMethod_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_ResolveType_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_ResolveField_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_ResolveMember_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_ResolveString_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_ResolveSignature_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_ResolveMethod_Overload_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_ResolveType_Overload_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_ResolveField_Overload_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

        [Test]
        public async Task Module_ResolveMember_Overload_Diagnostic()
        {
            var test = @"
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
}";

            await AssertAssemblyLoadingDiagnosticsAsync(test);
        }

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
            var (source, expectedSpanText) = StripSp0002Markup(markedSource);
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
                .ToArray();

            if (expectedSpanText == null)
            {
                Assert.That(purityDiagnostics, Is.Empty);
                Assert.That(diagnostics, Is.Empty);
                return;
            }

            Assert.That(purityDiagnostics, Has.Length.EqualTo(1));
            Assert.That(diagnostics, Has.Length.EqualTo(1));

            var diagnostic = purityDiagnostics[0];
            var actualSpanText = source.Substring(
                diagnostic.Location.SourceSpan.Start,
                diagnostic.Location.SourceSpan.Length);
            Assert.That(actualSpanText, Is.EqualTo(expectedSpanText));
        }

        private static (string Source, string? ExpectedSpanText) StripSp0002Markup(string markedSource)
        {
            const string prefix = "{|SP0002:";
            const string suffix = "|}";
            var start = markedSource.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return (markedSource, null);
            }

            var contentStart = start + prefix.Length;
            var end = markedSource.IndexOf(suffix, contentStart, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThanOrEqualTo(0), "Expected SP0002 markup end.");

            var expectedSpanText = markedSource.Substring(contentStart, end - contentStart);
            var source = markedSource.Remove(end, suffix.Length).Remove(start, prefix.Length);
            return (source, expectedSpanText);
        }
    }
}
