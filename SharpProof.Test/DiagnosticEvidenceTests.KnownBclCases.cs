using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using static SharpProof.Test.AnalyzerTestHost;

namespace SharpProof.Test;

public partial class DiagnosticEvidenceTests
{
    [TestCase("using System;", "void", "", "Console.WriteLine(\"impure\");", null, null,
        "generated_purity_summary", "System.Console.WriteLine", false,
        TestName = "Sp0002_ConsoleWriteLine_UsesGeneratedPuritySummarySource")]
    [TestCase("using System;", "void", "", "Console.Clear();", null, null,
        "generated_purity_summary", "System.Console.Clear", false,
        TestName = "Sp0002_ConsoleClear_UsesGeneratedPuritySummarySource")]
    [TestCase("using System;\nusing System.ComponentModel;", "TypeConverter", "Type type",
        "return TypeDescriptor.GetConverter(type);", "global_state_write", "MethodInvocationPurityRule",
        "generated_purity_summary", "System.ComponentModel.TypeDescriptor.GetConverter", false,
        TestName = "Sp0002_TypeDescriptorGetConverter_UsesGeneratedPuritySummarySource")]
    [TestCase("using System.ComponentModel;", "PropertyDescriptorCollection", "object value",
        "return TypeDescriptor.GetProperties(value);", "global_state_write", "MethodInvocationPurityRule",
        "generated_purity_summary", "System.ComponentModel.TypeDescriptor.GetProperties", false,
        TestName = "Sp0002_TypeDescriptorGetProperties_UsesGeneratedPuritySummarySource")]
    [TestCase("using System;", "string", "", "return Activator.CreateInstance<string>();",
        "global_state_write", "MethodInvocationPurityRule", "generated_purity_summary",
        "System.Activator.CreateInstance", false,
        TestName = "Sp0002_ActivatorCreateInstanceOfT_UsesGeneratedPuritySummarySource")]
    [TestCase("using System;", "object", "Type type", "return Activator.CreateInstance(type);",
        "global_state_write", "MethodInvocationPurityRule", "generated_purity_summary",
        "System.Activator.CreateInstance", false,
        TestName = "Sp0002_ActivatorCreateInstanceType_UsesGeneratedPuritySummarySource")]
    [TestCase("using System;\nusing System.Runtime.Remoting;", "ObjectHandle", "",
        "return Activator.CreateInstanceFrom(\"Example.dll\", \"Example.Type\");", "global_state_write",
        "MethodInvocationPurityRule", "generated_purity_summary", "System.Activator.CreateInstanceFrom", false,
        TestName = "Sp0002_ActivatorCreateInstanceFrom_UsesGeneratedPuritySummarySource")]
    [TestCase("using System;", "void", "", "GC.Collect();", "unknown_callee", "MethodInvocationPurityRule",
        "generated_purity_summary", "System.GC.Collect", false,
        TestName = "Sp0002_GCCollect_UsesGeneratedPuritySummarySource")]
    [TestCase("using System;", "long", "", "return GC.GetTotalMemory(false);", "unknown_callee",
        "MethodInvocationPurityRule", "generated_purity_summary", "System.GC.GetTotalMemory", false,
        TestName = "Sp0002_GCGetTotalMemory_UsesGeneratedPuritySummarySource")]
    [TestCase("using System;", "int", "object value", "return GC.GetGeneration(value);",
        "metadata_only_or_external", "MethodInvocationPurityRule", "generated_purity_summary",
        "System.GC.GetGeneration", false,
        TestName = "Sp0002_GCGetGeneration_UsesGeneratedPuritySummarySource")]
    [TestCase("using System;", "Random", "", "return new Random();", "catalog_hit", null,
        "random_semantic_rule", "System.Random.Random", true,
        TestName = "Sp0002_RandomConstructor_UsesRandomSemanticRuleSource")]
    [TestCase("using System;", "int", "Random random", "return random.Next();", "catalog_hit", null,
        "random_semantic_rule", "System.Random.Next", true,
        TestName = "Sp0002_RandomNext_UsesRandomSemanticRuleSource")]
    [TestCase("using System.Text;", "void", "StringBuilder sb", "sb.Append(\"hello\");", "catalog_hit", null,
        "string_builder_semantic_rule", "System.Text.StringBuilder.Append", true,
        TestName = "Sp0002_StringBuilderAppend_UsesStringBuilderSemanticRuleSource")]
    [TestCase("using System;", "void", "int[] values", "Array.Reverse(values);", null,
        "MethodInvocationPurityRule", "array_mutation_semantic_rule", "System.Array.Reverse", true,
        TestName = "Sp0002_ArrayReverse_UsesArrayMutationSemanticRuleSource")]
    [TestCase("using System;", "void", "int[] values, Comparison<int> comparison",
        "Array.Sort(values, comparison);", null, "MethodInvocationPurityRule", "array_mutation_semantic_rule",
        "System.Array.Sort", true,
        TestName = "Sp0002_ArraySortWithComparison_UsesArrayMutationSemanticRuleSource")]
    [TestCase("using System.Xml.Linq;", "XDocument", "", "return XDocument.Parse(\"<root />\");",
        "catalog_hit", "MethodInvocationPurityRule", "xml_linq_semantic_rule",
        "System.Xml.Linq.XDocument.Parse", true,
        TestName = "Sp0002_XDocumentParse_UsesXmlLinqSemanticRuleSource")]
    [TestCase("using System.Xml.Linq;", "string", "XElement element", "return element.Value;",
        "catalog_hit", "PropertyReferencePurityRule", "xml_linq_semantic_rule",
        "System.Xml.Linq.XElement.Value", true,
        TestName = "Sp0002_XElementValue_UsesXmlLinqSemanticRuleSource")]
    [TestCase("using System.Xml.Linq;", "void", "XNode node", "node.Remove();", "catalog_hit",
        "MethodInvocationPurityRule", "xml_linq_semantic_rule", "System.Xml.Linq.XNode.Remove", true,
        TestName = "Sp0002_XNodeRemove_UsesXmlLinqSemanticRuleSource")]
    [TestCase("using System.IO;", "MemoryStream", "", "return new MemoryStream();", "catalog_hit",
        "ObjectCreationPurityRule", "io_stream_text_semantic_rule", "System.IO.MemoryStream.MemoryStream", true,
        TestName = "Sp0002_MemoryStreamConstructor_UsesIoStreamTextSemanticRuleSource")]
    [TestCase("using System.IO;", "StringReader", "", "return new StringReader(\"text\");", "catalog_hit",
        "ObjectCreationPurityRule", "io_stream_text_semantic_rule", "System.IO.StringReader.StringReader", true,
        TestName = "Sp0002_StringReaderConstructor_UsesIoStreamTextSemanticRuleSource")]
    [TestCase("using System.IO;", "string", "StringReader reader", "return reader.ReadToEnd();", "catalog_hit",
        null, "io_stream_text_semantic_rule", "System.IO.StringReader.ReadToEnd", true,
        TestName = "Sp0002_StringReaderReadToEnd_UsesIoStreamTextSemanticRuleSource")]
    [TestCase("using System.IO;", "StreamReader", "Stream stream", "return new StreamReader(stream);",
        "catalog_hit", "ObjectCreationPurityRule", "io_stream_text_semantic_rule",
        "System.IO.StreamReader.StreamReader", true,
        TestName = "Sp0002_StreamReaderConstructor_UsesIoStreamTextSemanticRuleSource")]
    [TestCase("using System.IO;", "void", "StreamWriter writer", "writer.WriteLine(\"line\");", "catalog_hit",
        null, "io_stream_text_semantic_rule", "System.IO.StreamWriter.WriteLine", true,
        TestName = "Sp0002_StreamWriterWriteLine_UsesIoStreamTextSemanticRuleSource")]
    [TestCase("using System.IO;", "void", "StringWriter writer", "writer.Write(\"text\");", "catalog_hit",
        null, "io_stream_text_semantic_rule", "System.IO.StringWriter.Write", true,
        TestName = "Sp0002_StringWriterWrite_UsesIoStreamTextSemanticRuleSource")]
    [TestCase("using System;", "string", "int value", "return string.Format(\"{0:D}\", value);",
        "impure_callee", "MethodInvocationPurityRule", "generated_purity_summary", "Format", false,
        TestName = "Sp0002_StringFormat_UsesGeneratedPuritySummarySource")]
    [TestCase("using System.Diagnostics;", "Process", "", "return Process.GetCurrentProcess();", null, null,
        "generated_purity_summary", "System.Diagnostics.Process.GetCurrentProcess", false,
        TestName = "Sp0002_ProcessGetCurrentProcess_UsesGeneratedPuritySummarySource")]
    [TestCase("using System.Threading;", "void", "object gate", "Monitor.Exit(gate);", "synchronization",
        "MethodInvocationPurityRule", "threading_semantic_rule", "System.Threading.Monitor.Exit", false,
        TestName = "Sp0002_MonitorExit_UsesThreadingSemanticRuleSource")]
    [TestCase("using System.Runtime.Loader;", "AssemblyLoadContext", "", "return AssemblyLoadContext.Default;",
        "reflection_environment_source", "PropertyReferencePurityRule", "assembly_load_context_semantic_rule",
        "System.Runtime.Loader.AssemblyLoadContext.Default", false,
        TestName = "Sp0002_AssemblyLoadContextDefault_UsesSemanticRuleSource")]
    [TestCase("using System.Reflection;\nusing System.Runtime.Loader;", "Assembly",
        "AssemblyLoadContext context, string path", "return context.LoadFromAssemblyPath(path);",
        "reflection_environment_source", "MethodInvocationPurityRule", "assembly_load_context_semantic_rule",
        "System.Runtime.Loader.AssemblyLoadContext.LoadFromAssemblyPath", false,
        TestName = "Sp0002_AssemblyLoadContextLoadFromAssemblyPath_UsesSemanticRuleSource")]
    public async Task Sp0002_KnownBclSemanticEvidence(
        string usings,
        string returnType,
        string parameters,
        string body,
        string? category,
        string? rule,
        string catalogSource,
        string symbol,
        bool clearAdditionalFiles)
    {
        var source = $$"""
            {{usings}}
            using SharpProof.Attributes;

            public class TestClass
            {
                [EnforcePure]
                public {{returnType}} TestMethod({{parameters}})
                {
                    {{body}}
                }
            }
            """;
        var additionalFiles = clearAdditionalFiles
            ? ImmutableArray<AdditionalText>.Empty
            : (ImmutableArray<AdditionalText>?)null;
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source, additionalFiles: additionalFiles);
        var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);

        AssertSp0002Evidence(diagnostic, category, rule, catalogSource, symbol);
    }
}
