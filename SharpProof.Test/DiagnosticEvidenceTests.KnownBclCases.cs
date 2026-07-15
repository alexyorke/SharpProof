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
    [TestCase("using System.Threading;", "void", "object gate", "Monitor.Enter(gate);", "synchronization",
        "MethodInvocationPurityRule", "threading_semantic_rule", "System.Threading.Monitor.Enter", true,
        TestName = "Sp0002_MonitorEnter_UsesThreadingSemanticRuleSource")]
    [TestCase("using System.Threading;", "void", "", "Thread.Sleep(1);", "catalog_hit",
        "MethodInvocationPurityRule", "threading_semantic_rule", "System.Threading.Thread.Sleep", true,
        TestName = "Sp0002_ThreadSleep_UsesThreadingSemanticRuleSource")]
    [TestCase("using System.Threading;", "int", "Thread thread", "return thread.ManagedThreadId;", "catalog_hit",
        "PropertyReferencePurityRule", "threading_semantic_rule", "System.Threading.Thread.ManagedThreadId", true,
        TestName = "Sp0002_ThreadManagedThreadId_UsesThreadingSemanticRuleSource")]
    [TestCase("using System;\nusing System.Threading;", "CancellationTokenRegistration", "CancellationToken token",
        "return token.Register(() => { });", "catalog_hit", "MethodInvocationPurityRule",
        "threading_semantic_rule", "System.Threading.CancellationToken.Register", true,
        TestName = "Sp0002_CancellationTokenRegister_UsesThreadingSemanticRuleSource")]
    [TestCase("using System.Threading;", "int", "AsyncLocal<int> state", "return state.Value;", "catalog_hit",
        "PropertyReferencePurityRule", "threading_semantic_rule", "System.Threading.AsyncLocal", true,
        TestName = "Sp0002_AsyncLocalValue_UsesThreadingSemanticRuleSource")]
    [TestCase("using System.Threading;", "Semaphore", "", "return new Semaphore(0, 1);", "synchronization",
        "ObjectCreationPurityRule", "threading_semantic_rule", "System.Threading.Semaphore.Semaphore", true,
        TestName = "Sp0002_SemaphoreConstructor_UsesThreadingSemanticRuleSource")]
    [TestCase("using System.Threading;", "int", "ThreadLocal<int> state", "return state.Value;", "catalog_hit",
        "PropertyReferencePurityRule", "threading_semantic_rule", "System.Threading.ThreadLocal", true,
        TestName = "Sp0002_ThreadLocalValue_UsesThreadingSemanticRuleSource")]
    [TestCase("using System.Threading.Channels;", "Channel<int>", "", "return Channel.CreateUnbounded<int>();",
        "catalog_hit", "MethodInvocationPurityRule", "threading_semantic_rule",
        "System.Threading.Channels.Channel.CreateUnbounded", true,
        TestName = "Sp0002_ChannelCreateUnbounded_UsesThreadingSemanticRuleSource")]
    [TestCase("using System.Diagnostics;", "ActivitySource", "", "return new ActivitySource(\"test\", \"1.0.0\");",
        "catalog_hit", "ObjectCreationPurityRule", "diagnostics_tracing_semantic_rule",
        "System.Diagnostics.ActivitySource.ActivitySource", true,
        TestName = "Sp0002_ActivitySourceConstructor_UsesDiagnosticsTracingSemanticRuleSource")]
    [TestCase("#nullable enable\nusing System.Diagnostics;", "Activity?", "", "return Activity.Current;",
        "catalog_hit", "PropertyReferencePurityRule", "diagnostics_tracing_semantic_rule",
        "System.Diagnostics.Activity.Current", true,
        TestName = "Sp0002_ActivityCurrent_UsesDiagnosticsTracingSemanticRuleSource")]
    [TestCase("using System.Diagnostics;", "void", "Activity activity", "activity.SetTag(\"key\", \"value\");",
        "catalog_hit", "MethodInvocationPurityRule", "diagnostics_tracing_semantic_rule",
        "System.Diagnostics.Activity.SetTag", true,
        TestName = "Sp0002_ActivitySetTag_UsesDiagnosticsTracingSemanticRuleSource")]
    [TestCase("using System.Diagnostics.Metrics;", "Counter<int>", "Meter meter",
        "return meter.CreateCounter<int>(\"requests\", \"count\", \"Request count\");", "catalog_hit",
        "MethodInvocationPurityRule", "diagnostics_tracing_semantic_rule",
        "System.Diagnostics.Metrics.Meter.CreateCounter", true,
        TestName = "Sp0002_MeterCreateCounter_UsesDiagnosticsTracingSemanticRuleSource")]
    [TestCase("using System.Diagnostics.Metrics;", "void", "Counter<int> counter", "counter.Add(1);",
        "catalog_hit", "MethodInvocationPurityRule", "diagnostics_tracing_semantic_rule",
        "System.Diagnostics.Metrics.Counter", true,
        TestName = "Sp0002_CounterAdd_UsesDiagnosticsTracingSemanticRuleSource")]
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
