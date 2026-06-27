using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Reflection;
using System.Threading.Tasks;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

#nullable enable

namespace PurelySharp.Test
{
    [TestFixture]
    public class AssemblyLoadingTests
    {





        [Test]
        public async Task Assembly_GetExecutingAssembly_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|PS0002:TestMethod|}()
        {
            // Assembly.GetExecutingAssembly() interacts with runtime state
            return Assembly.GetExecutingAssembly();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetCallingAssembly_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|PS0002:TestMethod|}()
        {
            return Assembly.GetCallingAssembly();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetEntryAssembly_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly? {|PS0002:TestMethod|}()
        {
            return Assembly.GetEntryAssembly();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_Load_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|PS0002:TestMethod|}(string assemblyString)
        {
            return Assembly.Load(assemblyString);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_LoadFrom_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|PS0002:TestMethod|}(string assemblyFile)
        {
            return Assembly.LoadFrom(assemblyFile);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }


        [Test]
        public async Task Assembly_GetTypes_Diagnostic()
        {
            var test = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Type[] {|PS0002:TestMethod|}(Assembly assembly)
        {
            // Assembly.GetTypes() might load dependent assemblies, potentially impure
            return assembly.GetTypes();
        }
    }
            }";
            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetExportedTypes_Diagnostic()
        {
            var test = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Type[] {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetExportedTypes();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetReferencedAssemblies_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyName[] {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetReferencedAssemblies();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyName_Constructor_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyName {|PS0002:TestMethod|}()
        {
            return new AssemblyName(""PurelySharp"");
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyName_GetAssemblyName_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyName {|PS0002:TestMethod|}(string path)
        {
            return AssemblyName.GetAssemblyName(path);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_Default_Diagnostic()
        {
            var test = @"
using System.Runtime.Loader;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyLoadContext {|PS0002:TestMethod|}()
        {
            return AssemblyLoadContext.Default;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_DerivedConstructor_Diagnostic()
        {
            var test = @"
using System.Runtime.Loader;
using PurelySharp.Attributes;

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
        public DerivedLoadContext {|PS0002:TestMethod|}()
        {
            return new DerivedLoadContext();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_All_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Runtime.Loader;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public IEnumerable<AssemblyLoadContext> {|PS0002:TestMethod|}()
        {
            return AssemblyLoadContext.All;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_LoadFromAssemblyPath_Diagnostic()
        {
            var test = @"
using System.Reflection;
using System.Runtime.Loader;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|PS0002:TestMethod|}(AssemblyLoadContext context, string path)
        {
            return context.LoadFromAssemblyPath(path);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_LoadFromStream_Diagnostic()
        {
            var test = @"
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|PS0002:TestMethod|}(AssemblyLoadContext context, Stream stream)
        {
            return context.LoadFromStream(stream);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_LoadFromStreamWithSymbols_Diagnostic()
        {
            var test = @"
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|PS0002:TestMethod|}(AssemblyLoadContext context, Stream assemblyStream, Stream symbolsStream)
        {
            return context.LoadFromStream(assemblyStream, symbolsStream);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_LoadFromAssemblyName_Diagnostic()
        {
            var test = @"
using System.Reflection;
using System.Runtime.Loader;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|PS0002:TestMethod|}(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            return context.LoadFromAssemblyName(assemblyName);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_LoadFromNativeImagePath_Diagnostic()
        {
            var test = @"
using System.Reflection;
using System.Runtime.Loader;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|PS0002:TestMethod|}(AssemblyLoadContext context, string nativeImagePath, string assemblyPath)
        {
            return context.LoadFromNativeImagePath(nativeImagePath, assemblyPath);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_GetLoadContext_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using System.Runtime.Loader;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyLoadContext? {|PS0002:TestMethod|}(Assembly assembly)
        {
            return AssemblyLoadContext.GetLoadContext(assembly);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_CurrentContextualReflectionContext_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Runtime.Loader;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyLoadContext? {|PS0002:TestMethod|}()
        {
            return AssemblyLoadContext.CurrentContextualReflectionContext;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_EnterContextualReflection_Diagnostic()
        {
            var test = @"
using System.Runtime.Loader;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyLoadContext.ContextualReflectionScope {|PS0002:TestMethod|}(AssemblyLoadContext context)
        {
            return context.EnterContextualReflection();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task AssemblyLoadContext_EnterContextualReflectionForAssembly_Diagnostic()
        {
            var test = @"
using System.Reflection;
using System.Runtime.Loader;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyLoadContext.ContextualReflectionScope {|PS0002:TestMethod|}(Assembly assembly)
        {
            return AssemblyLoadContext.EnterContextualReflection(assembly);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetManifestResourceNames_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string[] {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetManifestResourceNames();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetLoadedModules_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Module[] {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetLoadedModules();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetModules_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Module[] {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetModules();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_DefinedTypes_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public IEnumerable<TypeInfo> {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.DefinedTypes;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_ExportedTypes_Diagnostic()
        {
            var test = @"
using System;
using System.Collections.Generic;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public IEnumerable<Type> {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.ExportedTypes;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_Modules_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public IEnumerable<Module> {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.Modules;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_ManifestModule_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Module {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.ManifestModule;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_EntryPoint_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public MethodInfo? {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.EntryPoint;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_CustomAttributes_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public IEnumerable<CustomAttributeData> {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.CustomAttributes;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_Location_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.Location;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_IsDynamic_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public bool {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.IsDynamic;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_IsFullyTrusted_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public bool {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.IsFullyTrusted;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_HostContext_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public long {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.HostContext;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GlobalAssemblyCache_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public bool {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GlobalAssemblyCache;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_ReflectionOnly_Diagnostic()
        {
            var test = @"
using System.Reflection;
using System.Security;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public bool {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.ReflectionOnly;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_SecurityRuleSet_Diagnostic()
        {
            var test = @"
using System.Reflection;
using System.Security;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public SecurityRuleSet {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.SecurityRuleSet;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_CodeBase_Diagnostic()
        {
            var test = @"
#pragma warning disable SYSLIB0012
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.CodeBase;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_EscapedCodeBase_Diagnostic()
        {
            var test = @"
#pragma warning disable SYSLIB0012
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.EscapedCodeBase;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetName_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public AssemblyName {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetName();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetFiles_Diagnostic()
        {
            var test = @"
using System.IO;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public FileStream[] {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetFiles();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetModule_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Module? {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetModule(""MainModule"");
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetFile_Diagnostic()
        {
            var test = @"
#nullable enable
using System.IO;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public FileStream? {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetFile(""data.bin"");
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetManifestResourceInfo_Diagnostic()
        {
            var test = @"
#nullable enable
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public ManifestResourceInfo? {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetManifestResourceInfo(""asset.txt"");
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetManifestResourceStream_Diagnostic()
        {
            var test = @"
#nullable enable
using System.IO;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Stream? {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetManifestResourceStream(""asset.txt"");
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetModules_Overload_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Module[] {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetModules(true);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetLoadedModules_Overload_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Module[] {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetLoadedModules(true);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetSatelliteAssembly_Diagnostic()
        {
            var test = @"
using System.Globalization;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetSatelliteAssembly(CultureInfo.InvariantCulture);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_GetSatelliteAssembly_Overload_Diagnostic()
        {
            var test = @"
using System;
using System.Globalization;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|PS0002:TestMethod|}(Assembly assembly)
        {
            return assembly.GetSatelliteAssembly(CultureInfo.InvariantCulture, new Version(1, 0));
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_Assembly_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Assembly {|PS0002:TestMethod|}(Module module)
        {
            return module.Assembly;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_FullyQualifiedName_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|PS0002:TestMethod|}(Module module)
        {
            return module.FullyQualifiedName;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_Name_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|PS0002:TestMethod|}(Module module)
        {
            return module.Name;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_ScopeName_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|PS0002:TestMethod|}(Module module)
        {
            return module.ScopeName;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_ModuleVersionId_Diagnostic()
        {
            var test = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Guid {|PS0002:TestMethod|}(Module module)
        {
            return module.ModuleVersionId;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_GetTypes_Diagnostic()
        {
            var test = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Type[] {|PS0002:TestMethod|}(Module module)
        {
            return module.GetTypes();
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_GetType_Diagnostic()
        {
            var test = @"
#nullable enable
using System;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Type? {|PS0002:TestMethod|}(Module module)
        {
            return module.GetType(""TestNamespace.TestClass"");
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_ResolveMethod_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public MethodBase {|PS0002:TestMethod|}(Module module)
        {
            return module.ResolveMethod(0);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_ResolveType_Diagnostic()
        {
            var test = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Type {|PS0002:TestMethod|}(Module module)
        {
            return module.ResolveType(0);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_ResolveField_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public FieldInfo {|PS0002:TestMethod|}(Module module)
        {
            return module.ResolveField(0);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_ResolveMember_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public MemberInfo {|PS0002:TestMethod|}(Module module)
        {
            return module.ResolveMember(0);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_ResolveString_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public string {|PS0002:TestMethod|}(Module module)
        {
            return module.ResolveString(0);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_ResolveSignature_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public byte[] {|PS0002:TestMethod|}(Module module)
        {
            return module.ResolveSignature(0);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_ResolveMethod_Overload_Diagnostic()
        {
            var test = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public MethodBase {|PS0002:TestMethod|}(Module module)
        {
            return module.ResolveMethod(0, Array.Empty<Type>(), Array.Empty<Type>());
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_ResolveType_Overload_Diagnostic()
        {
            var test = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public Type {|PS0002:TestMethod|}(Module module)
        {
            return module.ResolveType(0, Array.Empty<Type>(), Array.Empty<Type>());
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_ResolveField_Overload_Diagnostic()
        {
            var test = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public FieldInfo {|PS0002:TestMethod|}(Module module)
        {
            return module.ResolveField(0, Array.Empty<Type>(), Array.Empty<Type>());
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Module_ResolveMember_Overload_Diagnostic()
        {
            var test = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

namespace TestNamespace
{
    public class TestClass
    {
        [EnforcePure]
        public MemberInfo {|PS0002:TestMethod|}(Module module)
        {
            return module.ResolveMember(0, Array.Empty<Type>(), Array.Empty<Type>());
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Assembly_LoadFile_Diagnostic()
        {
            var test = @"
using System.Reflection;
using PurelySharp.Attributes;

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

            var expected = VerifyCS.Diagnostic(PurelySharpDiagnostics.PurityNotVerifiedId)
                                   .WithSpan(10, 25, 10, 35)
                                   .WithArguments("TestMethod");
            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }
    }
}
