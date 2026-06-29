using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using PurelySharp.Analyzer;
using static PurelySharp.Test.AnalyzerTestHost;
using InMemoryAdditionalText = PurelySharp.Test.AnalyzerTestHost.InMemoryAdditionalText;

namespace PurelySharp.Test
{
    [TestFixture]
    public partial class DiagnosticEvidenceTests
    {
        private const string BclFallbackFixtureSource = @"
namespace System.Experimental
{
    public static class NumericFacts
    {
        public static int Normalize(int value)
        {
            return value < 0 ? -value : value;
        }
    }

    public static class MutatingSink
    {
        public static void WriteMetric(int value)
        {
        }
    }

    public sealed class StatefulBox
    {
        public StatefulBox Next()
        {
            return this;
        }
    }

    public sealed class NumericBox
    {
        public int Value
        {
            get { return 42; }
        }
    }
}
";

        [Test]
        public async Task Ps0002_KnownImpureCatalogHit_IncludesStructuredEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Debug.WriteLine(""impure"");
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("Invocation"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("known_impure_namespace_or_type"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Debug.WriteLine"));
        }

        [Test]
        public async Task Ps0002_ConsoleWriteLine_UsesGeneratedPuritySummarySource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Console.WriteLine(""impure"");
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_ConsoleClear_UsesGeneratedPuritySummarySource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Console.Clear();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Clear"));
        }

        [Test]
        public async Task Ps0002_TypeDescriptorGetConverter_UsesGeneratedPuritySummarySource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.ComponentModel;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TypeConverter TestMethod(Type type)
    {
        return TypeDescriptor.GetConverter(type);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.ComponentModel.TypeDescriptor.GetConverter"));
        }

        [Test]
        public async Task Ps0002_TypeDescriptorGetProperties_UsesGeneratedPuritySummarySource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.ComponentModel;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public PropertyDescriptorCollection TestMethod(object value)
    {
        return TypeDescriptor.GetProperties(value);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.ComponentModel.TypeDescriptor.GetProperties"));
        }

        [Test]
        public async Task Ps0002_ActivatorCreateInstanceOfT_UsesGeneratedPuritySummarySource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Activator.CreateInstance<string>();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Activator.CreateInstance"));
        }

        [Test]
        public async Task Ps0002_ActivatorCreateInstanceType_UsesGeneratedPuritySummarySource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public object TestMethod(Type type)
    {
        return Activator.CreateInstance(type);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Activator.CreateInstance"));
        }

        [Test]
        public async Task Ps0002_ActivatorCreateInstanceFrom_UsesGeneratedPuritySummarySource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.Runtime.Remoting;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ObjectHandle TestMethod()
    {
        return Activator.CreateInstanceFrom(""Example.dll"", ""Example.Type"");
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Activator.CreateInstanceFrom"));
        }

        [Test]
        public async Task Ps0002_GCCollect_UsesGeneratedPuritySummarySource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        GC.Collect();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unknown_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.GC.Collect"));
        }

        [Test]
        public async Task Ps0002_GCGetTotalMemory_UsesGeneratedPuritySummarySource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod()
    {
        return GC.GetTotalMemory(false);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unknown_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.GC.GetTotalMemory"));
        }

        [Test]
        public async Task Ps0002_GCGetGeneration_UsesGeneratedPuritySummarySource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(object value)
    {
        return GC.GetGeneration(value);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("metadata_only_or_external"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.GC.GetGeneration"));
        }

        [Test]
        public async Task Ps0002_RandomConstructor_UsesRandomSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Random TestMethod()
    {
        return new Random();
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("random_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Random.Random"));
        }

        [Test]
        public async Task Ps0002_RandomNext_UsesRandomSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Random random)
    {
        return random.Next();
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("random_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Random.Next"));
        }

        [Test]
        public async Task Ps0002_StringBuilderAppend_UsesStringBuilderSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Text;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(StringBuilder sb)
    {
        sb.Append(""hello"");
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("string_builder_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Text.StringBuilder.Append"));
        }

        [Test]
        public async Task Ps0002_ArrayReverse_UsesArrayMutationSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        Array.Reverse(values);
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("array_mutation_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.Reverse"));
        }

        [Test]
        public async Task Ps0002_ArraySortWithComparison_UsesArrayMutationSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values, Comparison<int> comparison)
    {
        Array.Sort(values, comparison);
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("array_mutation_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.Sort"));
        }

        [TestCaseSource(nameof(GetThreadingSemanticRuleCases))]
        public async Task Ps0002_ThreadingSemanticRules_UseThreadingSemanticRuleSource(
            string source,
            string category,
            string rule,
            string symbolSubstring)
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                source,
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo(category));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo(rule));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("threading_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain(symbolSubstring));
        }

        [Test]
        public async Task Ps0002_XDocumentParse_UsesXmlLinqSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public XDocument TestMethod()
    {
        return XDocument.Parse(""<root />"");
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("xml_linq_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Xml.Linq.XDocument.Parse"));
        }

        [Test]
        public async Task Ps0002_XElementValue_UsesXmlLinqSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(XElement element)
    {
        return element.Value;
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("xml_linq_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Xml.Linq.XElement.Value"));
        }

        [Test]
        public async Task Ps0002_XNodeRemove_UsesXmlLinqSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Xml.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(XNode node)
    {
        node.Remove();
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("xml_linq_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Xml.Linq.XNode.Remove"));
        }

        [Test]
        public async Task Ps0002_ActivitySourceConstructor_UsesDiagnosticsTracingSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ActivitySource TestMethod()
    {
        return new ActivitySource(""test"", ""1.0.0"");
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("diagnostics_tracing_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.ActivitySource.ActivitySource"));
        }

        [Test]
        public async Task Ps0002_ActivityCurrent_UsesDiagnosticsTracingSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
#nullable enable
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Activity? TestMethod()
    {
        return Activity.Current;
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("diagnostics_tracing_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Activity.Current"));
        }

        [Test]
        public async Task Ps0002_ActivitySetTag_UsesDiagnosticsTracingSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Activity activity)
    {
        activity.SetTag(""key"", ""value"");
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("diagnostics_tracing_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Activity.SetTag"));
        }

        [Test]
        public async Task Ps0002_MeterCreateCounter_UsesDiagnosticsTracingSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics.Metrics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Counter<int> TestMethod(Meter meter)
    {
        return meter.CreateCounter<int>(""requests"", ""count"", ""Request count"");
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("diagnostics_tracing_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Metrics.Meter.CreateCounter"));
        }

        [Test]
        public async Task Ps0002_CounterAdd_UsesDiagnosticsTracingSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics.Metrics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Counter<int> counter)
    {
        counter.Add(1);
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("diagnostics_tracing_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Metrics.Counter"));
        }

        [Test]
        public async Task Ps0002_MemoryStreamConstructor_UsesIoStreamTextSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public MemoryStream TestMethod()
    {
        return new MemoryStream();
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("io_stream_text_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.MemoryStream.MemoryStream"));
        }

        [Test]
        public async Task Ps0002_StringReaderConstructor_UsesIoStreamTextSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public StringReader TestMethod()
    {
        return new StringReader(""text"");
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("io_stream_text_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.StringReader.StringReader"));
        }

        [Test]
        public async Task Ps0002_StringReaderReadToEnd_UsesIoStreamTextSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(StringReader reader)
    {
        return reader.ReadToEnd();
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("io_stream_text_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.StringReader.ReadToEnd"));
        }

        [Test]
        public async Task Ps0002_StreamReaderConstructor_UsesIoStreamTextSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public StreamReader TestMethod(Stream stream)
    {
        return new StreamReader(stream);
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("io_stream_text_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.StreamReader.StreamReader"));
        }

        [Test]
        public async Task Ps0002_StreamWriterWriteLine_UsesIoStreamTextSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(StreamWriter writer)
    {
        writer.WriteLine(""line"");
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("io_stream_text_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.StreamWriter.WriteLine"));
        }

        [Test]
        public async Task Ps0002_StringWriterWrite_UsesIoStreamTextSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(StringWriter writer)
    {
        writer.Write(""text"");
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("io_stream_text_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.StringWriter.Write"));
        }

        [Test]
        public async Task Ps0002_StringFormat_UsesGeneratedPuritySummarySource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int value)
    {
        return string.Format(""{0:D}"", value);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("Format"));
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownImpureMethod_IncludesConfigCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return CustomApi();
    }

    private int CustomApi()
    {
        return 42;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add(
                    "purelysharp_known_impure_methods",
                    "TestClass.CustomApi()"));

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("config_known_impure"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass.CustomApi"));
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownImpureTargetMethod_IncludesConfigCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return 42;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add(
                    "purelysharp_known_impure_methods",
                    "TestClass.TestMethod()"));

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("KnownImpureMethod"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("config_known_impure"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass.TestMethod"));
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownImpureTypeProperty_IncludesNamespaceOrTypeCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class Boundary
{
    public int Value => 1;
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Boundary boundary)
    {
        return boundary.Value;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add(
                    "purelysharp_known_impure_types",
                    "Boundary"));

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("known_impure_namespace_or_type"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("Boundary.Value"));
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownImpureTypeOverridesKnownPureBclHeuristic()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        return Math.Abs(value);
    }
}",
                ImmutableDictionary<string, string>.Empty.Add(
                    "purelysharp_known_impure_types",
                    "System.Math"));

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("known_impure_namespace_or_type"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Math.Abs"));
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownPureMethodOverridesConfiguredImpureType()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        return Math.Abs(value);
    }
}",
                ImmutableDictionary<string, string>.Empty
                    .Add("purelysharp_known_impure_types", "System.Math")
                    .Add("purelysharp_known_pure_methods", "System.Math.Abs(int)"));

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Configured pure member should override a configured impure type for the same member.");
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownPurePropertyOverridesConfiguredImpureNamespace()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Net;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IPAddress TestMethod()
    {
        return IPAddress.Loopback;
    }
}",
                ImmutableDictionary<string, string>.Empty
                    .Add("purelysharp_known_impure_namespaces", "System.Net")
                    .Add("purelysharp_known_pure_methods", "System.Net.IPAddress.Loopback.get"));

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Configured pure property should override a configured impure namespace for the same member.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_Sha256HashDataReadOnlySpan()
        {
            const string source = @"
using System;
using System.Security.Cryptography;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(ReadOnlySpan<byte> data)
    {
        return SHA256.HashData(data);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var tryGetPurityArgs = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, tryGetPurityArgs)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should keep SHA256.HashData(ReadOnlySpan<byte>) out of the impure cryptography namespace fallback.");
            Assert.That(matched, Is.True, "Generated purity catalog should trust the exact SHA256.ReadOnlySpan<byte> overload.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_UriIsWellFormedUriString_FromRuntimeImplementationAssembly()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string value)
    {
        return Uri.IsWellFormedUriString(value, UriKind.Absolute);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var tryGetPurityArgs = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, tryGetPurityArgs)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Uri.IsWellFormedUriString even when the symbol resolves through a facade assembly.");
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Uri.IsWellFormedUriString to its runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_UriEscapeAndUnescapeDataString_FromRuntimeImplementationAssembly()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        return Uri.UnescapeDataString(Uri.EscapeDataString(value));
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocations = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Select(invocation => (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!)
                .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = invocations.Select(symbol =>
            {
                var args = new object?[] { symbol.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Uri.EscapeDataString and Uri.UnescapeDataString when the symbol resolves through a facade assembly.");
            Assert.That(matched, Is.EqualTo(new[] { true, true }),
                "Generated purity catalog should resolve both Uri.EscapeDataString and Uri.UnescapeDataString to their runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_UriToStringAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(Uri value)
    {
        return value.ToString();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "value.ToString()");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve System.Uri.ToString().");
            Assert.That(classification, Is.EqualTo("impure"),
                "System.Uri.ToString() mutates cached instance state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_UriToStringInsideInterpolatedStringAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(Uri value)
    {
        return $""{value}"";
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName("System.Uri")!
                .GetMembers(nameof(Uri.ToString))
                .OfType<IMethodSymbol>()
                .Single(method => !method.IsStatic && method.Parameters.Length == 0);
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { symbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("InterpolatedStringPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve System.Uri.ToString() for interpolation too.");
            Assert.That(classification, Is.EqualTo("impure"),
                "System.Uri.ToString() should remain generated impure when used through interpolation.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_OperatingSystemIsWindows_FromRuntimeImplementationAssembly()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return OperatingSystem.IsWindows();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var tryGetPurityArgs = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, tryGetPurityArgs)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow OperatingSystem.IsWindows.");
            Assert.That(matched, Is.True, "Generated purity catalog should resolve OperatingSystem.IsWindows to its runtime implementation assembly.");
        }

        [Test]
        public async Task Ps0002_AppContextTargetFrameworkName_UsesGeneratedPurityCatalogSource()
        {
            const string source = @"
#nullable enable
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? TestMethod()
    {
        return AppContext.TargetFrameworkName;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "AppContext.TargetFrameworkName");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var getter = propertySymbol.GetMethod!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var tryGetPurityArgs = new object?[] { getter.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, tryGetPurityArgs)!;
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "Generated runtime evidence should treat AppContext.TargetFrameworkName as an impure ambient read.");
            Assert.That(matched, Is.True, "Generated purity catalog should resolve AppContext.TargetFrameworkName.get to its runtime implementation assembly.");
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.AppContext.TargetFrameworkName"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentStablePureGetters()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.Is64BitProcess && Environment.Is64BitOperatingSystem
            ? Environment.NewLine
            : string.Empty;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccesses = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "Environment.Is64BitProcess" ||
                    node.ToString() == "Environment.Is64BitOperatingSystem" ||
                    node.ToString() == "Environment.NewLine")
                .Select(node => (IPropertySymbol)semanticModel.GetSymbolInfo(node).Symbol!)
                .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = memberAccesses.Select(symbol =>
            {
                var args = new object?[] { symbol.GetMethod!.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Environment.Is64BitProcess, Environment.Is64BitOperatingSystem, and Environment.NewLine.");
            Assert.That(matched, Is.EqualTo(new[] { true, true, true }),
                "Generated purity catalog should resolve the stable Environment getters to their runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_StaticCachePureGetters()
        {
            const string source = @"
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Text;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        _ = Comparer<int>.Default;
        _ = EqualityComparer<int>.Default;
        _ = StringComparer.Ordinal;
        _ = StringComparer.OrdinalIgnoreCase;
        _ = Task.CompletedTask;
        _ = Encoding.ASCII;
        _ = CultureInfo.InvariantCulture;
        return 5;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccesses = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "Comparer<int>.Default" ||
                    node.ToString() == "EqualityComparer<int>.Default" ||
                    node.ToString() == "StringComparer.Ordinal" ||
                    node.ToString() == "StringComparer.OrdinalIgnoreCase" ||
                    node.ToString() == "Task.CompletedTask" ||
                    node.ToString() == "Encoding.ASCII" ||
                    node.ToString() == "CultureInfo.InvariantCulture")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = memberAccesses.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });

            Assert.That(diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
            Assert.That(classifications["Comparer<int>.Default"].matched, Is.True);
            Assert.That(classifications["Comparer<int>.Default"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["EqualityComparer<int>.Default"].matched, Is.True);
            Assert.That(classifications["EqualityComparer<int>.Default"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["StringComparer.Ordinal"].matched, Is.True);
            Assert.That(classifications["StringComparer.Ordinal"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["StringComparer.OrdinalIgnoreCase"].matched, Is.True);
            Assert.That(classifications["StringComparer.OrdinalIgnoreCase"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["Task.CompletedTask"].matched, Is.True);
            Assert.That(classifications["Task.CompletedTask"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["Encoding.ASCII"].matched, Is.True);
            Assert.That(classifications["Encoding.ASCII"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["CultureInfo.InvariantCulture"].matched, Is.True);
            Assert.That(classifications["CultureInfo.InvariantCulture"].classification, Is.EqualTo("pure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_CancellationTokenNoneAsPureEvidence()
        {
            const string source = @"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        _ = CancellationToken.None;
        return true;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "CancellationToken.None");
            var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = matched
                ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                : string.Empty;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow CancellationToken.None even under the System.Threading namespace fallback.");
            Assert.That(matched, Is.True);
            Assert.That(classification, Is.EqualTo("pure"));
        }

        [Test]
        public async Task Ps0002_TaskDelay_UsesGeneratedPuritySummaryEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Threading.Tasks;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public async Task TestMethod()
    {
        await Task.Delay(100);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Threading.Tasks.Task.Delay"));
        }

        [Test]
        public async Task Ps0002_TaskRun_UsesGeneratedPuritySummaryEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Threading.Tasks;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public async Task TestMethod()
    {
        await Task.Run(static () => { });
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("caller_visible_memory_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Threading.Tasks.Task.Run"));
        }

        [Test]
        public async Task Ps0002_CancellationTokenIsCancellationRequested_UsesGeneratedPuritySummaryEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(CancellationToken token)
    {
        return token.IsCancellationRequested;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Threading.CancellationToken.IsCancellationRequested.get"));
        }

        [Test]
        public async Task Ps0002_TaskIsCompleted_UsesGeneratedPuritySummaryEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Threading.Tasks;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Task task)
    {
        return task.IsCompleted;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Threading.Tasks.Task.IsCompleted.get"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_IPAddressIsLoopbackAsPureEvidence()
        {
            const string source = @"
using System.Net;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(IPAddress address)
    {
        return IPAddress.IsLoopback(address);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "IPAddress.IsLoopback(address)");
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var method = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!;
            var args = new object?[] { method.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = matched
                ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                : string.Empty;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow IPAddress.IsLoopback(System.Net.IPAddress).");
            Assert.That(matched, Is.True, "Generated purity catalog should resolve IPAddress.IsLoopback(System.Net.IPAddress).");
            Assert.That(classification, Is.EqualTo("pure"),
                "IPAddress.IsLoopback should classify pure once loopback singleton reads are treated as safe cache reads.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_IPEndPointConstructorAsImpureEvidence()
        {
            const string source = @"
using System.Net;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IPEndPoint TestMethod(IPAddress address)
    {
        return new IPEndPoint(address, 80);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var objectCreation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(node => node.ToString() == "new IPEndPoint(address, 80)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(objectCreation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve System.Net.IPEndPoint..ctor(System.Net.IPAddress, int).");
            Assert.That(classification, Is.EqualTo("impure"),
                "System.Net.IPEndPoint..ctor(System.Net.IPAddress, int) writes caller-visible object state and validates the port.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_FrameworkNameConstructorAsImpureEvidence()
        {
            const string source = @"
using System.Runtime.Versioning;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public FrameworkName TestMethod()
    {
        return new FrameworkName("".NETCoreApp,Version=v8.0"");
    }
}";

            var additionalFiles = CreateSyntheticGeneratedPurityAdditionalFiles(
                typeof(System.Runtime.Versioning.FrameworkName).Assembly.Location,
                (
                    "Synthetic.FrameworkName.Constructor.PurelySharp.EffectSummary.json",
                    "System.Runtime.Versioning.FrameworkName..ctor(string)",
                    "System.Runtime.Versioning.FrameworkName..ctor(string)",
                    "impure",
                    FormatJsonArray("object_state_write", "throw")));

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source, additionalFiles: additionalFiles);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var objectCreation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(node => node.ToString() == "new FrameworkName(\".NETCoreApp,Version=v8.0\")");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(objectCreation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateAnalyzerOptions(additionalFiles: additionalFiles), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve System.Runtime.Versioning.FrameworkName..ctor(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "System.Runtime.Versioning.FrameworkName..ctor(string) parses framework names, can throw, and writes caller-visible object state.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_AggregateExceptionMembersAsImpureEvidence()
        {
            const string source = @"
using System;
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public AggregateException Create(IEnumerable<Exception> values)
    {
        return new AggregateException(values);
    }

    [EnforcePure]
    public AggregateException Flatten(AggregateException value)
    {
        return value.Flatten();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .Where(node =>
                    node is ObjectCreationExpressionSyntax objectCreation &&
                    objectCreation.ToString() == "new AggregateException(values)" ||
                    node is InvocationExpressionSyntax invocation &&
                    invocation.ToString() == "value.Flatten()")
                .Select(node => node switch
                {
                    ObjectCreationExpressionSyntax objectCreation => semanticModel.GetSymbolInfo(objectCreation).Symbol as IMethodSymbol,
                    InvocationExpressionSyntax invocation => semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol,
                    _ => null,
                })
                .Where(method => method is not null)
                .Select(method => method!)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedMethods
                .ToDictionary(
                    method => method.ToDisplayString(),
                    method =>
                    {
                        var args = new object?[] { method.OriginalDefinition, compilation, null };
                        var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                        var purityEntry = args[2]!;
                        var classification = matched
                            ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                            : string.Empty;
                        return (matched, classification);
                    });

            Assert.That(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId).Select(candidate => candidate.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }),
                "AggregateException constructor and Flatten should now diagnose via reviewed generated runtime evidence.");
            Assert.That(classifications["System.AggregateException.AggregateException(System.Collections.Generic.IEnumerable<System.Exception>)"].matched, Is.True,
                "Generated purity catalog should resolve AggregateException(IEnumerable<Exception>).");
            Assert.That(classifications["System.AggregateException.AggregateException(System.Collections.Generic.IEnumerable<System.Exception>)"].classification, Is.EqualTo("impure"),
                "AggregateException(IEnumerable<Exception>) should classify impure from reviewed runtime evidence.");
            Assert.That(classifications["System.AggregateException.Flatten()"].matched, Is.True,
                "Generated purity catalog should resolve AggregateException.Flatten().");
            Assert.That(classifications["System.AggregateException.Flatten()"].classification, Is.EqualTo("impure"),
                "AggregateException.Flatten() should classify impure from reviewed runtime evidence.");
        }

        [Test]
        public void GeneratedPurityCatalog_Resolves_NullableComparisonAsConservativeUnknown()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(int? left, int? right)
    {
        _ = Nullable.Compare(left, right);
        return Nullable.Equals(left, right);
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocations = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "Nullable.Compare(left, right)" ||
                    node.ToString() == "Nullable.Equals(left, right)")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = invocations.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var method = (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { method.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });

            Assert.That(classifications["Nullable.Compare(left, right)"].matched, Is.True);
            Assert.That(classifications["Nullable.Compare(left, right)"].classification, Is.EqualTo("conservative_unknown"));
            Assert.That(classifications["Nullable.Equals(left, right)"].matched, Is.True);
            Assert.That(classifications["Nullable.Equals(left, right)"].classification, Is.EqualTo("conservative_unknown"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_NullableGetValueOrDefaultAsPureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int? value, int fallback)
    {
        return value.GetValueOrDefault() + value.GetValueOrDefault(fallback);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocations = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "value.GetValueOrDefault()" ||
                    node.ToString() == "value.GetValueOrDefault(fallback)")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = invocations.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var method = (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { method.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Nullable<T>.GetValueOrDefault overloads.");
            Assert.That(classifications["value.GetValueOrDefault()"].matched, Is.True);
            Assert.That(classifications["value.GetValueOrDefault()"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["value.GetValueOrDefault(fallback)"].matched, Is.True);
            Assert.That(classifications["value.GetValueOrDefault(fallback)"].classification, Is.EqualTo("pure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ExceptionStateAccessors_WithMessageAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Exception error)
    {
        _ = error.Message;
        _ = error.InnerException;
        return error.HResult;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccesses = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "error.Message" ||
                    node.ToString() == "error.InnerException" ||
                    node.ToString() == "error.HResult")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var useCurrent = catalogType.GetMethod("UseCurrent", BindingFlags.Public | BindingFlags.Static)!;
            var currentProperty = catalogType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            using var currentScope = (IDisposable)useCurrent.Invoke(null, new[] { catalog })!;
            var currentCatalog = currentProperty.GetValue(null)!;
            var classifications = memberAccesses.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var property = (IPropertySymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { property.GetMethod!.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    var currentArgs = new object?[] { property.GetMethod!.OriginalDefinition, compilation, null };
                    var currentMatched = (bool)tryGetPurity.Invoke(currentCatalog, currentArgs)!;
                    var currentPurityEntry = currentArgs[2]!;
                    var currentClassification = currentMatched
                        ? (string)currentPurityEntry.GetType().GetProperty("Classification")!.GetValue(currentPurityEntry)!
                        : string.Empty;
                    return (matched, classification, currentMatched, currentClassification);
                });

            Assert.That(classifications["error.Message"].matched, Is.True);
            Assert.That(classifications["error.Message"].classification, Is.EqualTo("impure"));
            Assert.That(classifications["error.Message"].currentMatched, Is.True);
            Assert.That(classifications["error.Message"].currentClassification, Is.EqualTo("impure"));
            Assert.That(classifications["error.InnerException"].matched, Is.True);
            Assert.That(classifications["error.InnerException"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["error.InnerException"].currentMatched, Is.True);
            Assert.That(classifications["error.InnerException"].currentClassification, Is.EqualTo("pure"));
            Assert.That(classifications["error.HResult"].matched, Is.True);
            Assert.That(classifications["error.HResult"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["error.HResult"].currentMatched, Is.True);
            Assert.That(classifications["error.HResult"].currentClassification, Is.EqualTo("pure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArgumentGuardHelpersAsPureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text, int number, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentOutOfRangeException.ThrowIfNegative(number);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "ArgumentNullException.ThrowIfNull(value)" ||
                    node.ToString() == "ArgumentException.ThrowIfNullOrEmpty(text)" ||
                    node.ToString() == "ArgumentOutOfRangeException.ThrowIfNegative(number)")
                .Select(node => (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!)
                .Select(symbol => symbol.OriginalDefinition)
                .OrderBy(symbol => symbol.ToDisplayString(), StringComparer.Ordinal)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the argument guard helpers backed by runtime summaries.");
            Assert.That(matched, Is.EqualTo(new[] { true, true, true }),
                "Generated purity catalog should resolve the tracked argument guard helpers to their runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentCurrentDirectoryAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.CurrentDirectory;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.CurrentDirectory");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.CurrentDirectory.get.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.CurrentDirectory depends on process/OS state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DirectoryGetCurrentDirectoryAsImpureEvidence()
        {
            const string source = @"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Directory.GetCurrentDirectory();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Directory.GetCurrentDirectory()");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.Directory.GetCurrentDirectory"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Directory.GetCurrentDirectory().");
            Assert.That(classification, Is.EqualTo("impure"),
                "Directory.GetCurrentDirectory depends on process/OS state through Environment.CurrentDirectory and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentCurrentDirectorySetterAsImpureWriteEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string path)
    {
        Environment.CurrentDirectory = path;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var assignment = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.CurrentDirectory = path");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(assignment.Left).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.SetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
            var primaryCategory = (string)purityEntry.GetType().GetProperty("PrimaryCategory")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Environment.CurrentDirectory.set"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.CurrentDirectory.set.");
            Assert.That(classification, Is.EqualTo("impure"));
            Assert.That(primaryCategory, Is.EqualTo("global_state_write"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DirectorySetCurrentDirectoryAsImpureWriteEvidence()
        {
            const string source = @"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string path)
    {
        Directory.SetCurrentDirectory(path);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Directory.SetCurrentDirectory(path)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
            var primaryCategory = (string)purityEntry.GetType().GetProperty("PrimaryCategory")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.Directory.SetCurrentDirectory"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Directory.SetCurrentDirectory(string).");
            Assert.That(classification, Is.EqualTo("impure"));
            Assert.That(primaryCategory, Is.EqualTo("global_state_write"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DirectoryExistsAsImpureEvidence()
        {
            const string source = @"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string path)
    {
        return Directory.Exists(path);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Directory.Exists(path)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.Directory.Exists"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Directory.Exists(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Directory.Exists depends on ambient file-system and path state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_FileExistsAsImpureEvidence()
        {
            const string source = @"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string path)
    {
        return File.Exists(path);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "File.Exists(path)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.File.Exists"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve File.Exists(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "File.Exists depends on ambient file-system state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DirectoryCreateDirectoryAsImpureEvidence()
        {
            const string source = @"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DirectoryInfo TestMethod(string path)
    {
        return Directory.CreateDirectory(path);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Directory.CreateDirectory(path)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.Directory.CreateDirectory"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Directory.CreateDirectory(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Directory.CreateDirectory mutates ambient file-system state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DirectoryCreateTempSubdirectoryAsImpureEvidence()
        {
            const string source = @"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DirectoryInfo TestMethod()
    {
        return Directory.CreateTempSubdirectory(""purelysharp-test-"");
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Directory.CreateTempSubdirectory(\"purelysharp-test-\")");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.Directory.CreateTempSubdirectory"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Directory.CreateTempSubdirectory(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Directory.CreateTempSubdirectory(string) depends on OS temp-path state and mutates the ambient file system, so it should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_FileSystemPathGettersAsMixedEvidence()
        {
            const string source = @"
#nullable enable
using System.IO;
using System.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DirectoryInfo? ParentMethod(DirectoryInfo directory)
    {
        return directory.Parent;
    }

    [EnforcePure]
    public string? DirectoryNameMethod(FileInfo file)
    {
        return file.DirectoryName;
    }

    [EnforcePure]
    public string NameMethod(DirectoryInfo directory)
    {
        return directory.Name;
    }

    [EnforcePure]
    public string FileNameMethod(FileInfo file)
    {
        return file.Name;
    }

    [EnforcePure]
    public string ExtensionMethod(FileInfo file)
    {
        return file.Extension;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedProperties = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "directory.Parent" ||
                    node.ToString() == "file.DirectoryName" ||
                    node.ToString() == "directory.Name" ||
                    node.ToString() == "file.Name" ||
                    node.ToString() == "file.Extension")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var resolutions = trackedProperties.Select(property =>
            {
                var getter = ((IPropertySymbol)semanticModel.GetSymbolInfo(property).Symbol!).GetMethod!;
                var args = new object?[] { getter.OriginalDefinition, compilation, null };
                var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                var entry = args[2]!;
                var classification = matched
                    ? (string)entry.GetType().GetProperty("Classification")!.GetValue(entry)!
                    : string.Empty;
                return (property: property.ToString(), matched, classification);
            }).ToDictionary(result => result.property, result => (result.matched, result.classification), StringComparer.Ordinal);

            Assert.That(purityDiagnostics, Has.Length.EqualTo(3));
            Assert.That(
                purityDiagnostics.All(diagnostic =>
                    string.Equals(
                        diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty],
                        "generated_purity_summary",
                        StringComparison.Ordinal)),
                Is.True);

            Assert.That(resolutions["directory.Parent"].matched, Is.True);
            Assert.That(resolutions["directory.Parent"].classification, Is.EqualTo("pure"));
            Assert.That(resolutions["file.DirectoryName"].matched, Is.True);
            Assert.That(resolutions["file.DirectoryName"].classification, Is.EqualTo("pure"));
            Assert.That(resolutions["directory.Name"].matched, Is.True);
            Assert.That(resolutions["directory.Name"].classification, Is.EqualTo("impure"));
            Assert.That(resolutions["file.Name"].matched, Is.True);
            Assert.That(resolutions["file.Name"].classification, Is.EqualTo("impure"));
            Assert.That(resolutions["file.Extension"].matched, Is.True);
            Assert.That(resolutions["file.Extension"].classification, Is.EqualTo("impure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentGetFolderPathAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.None);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.GetFolderPath.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.GetFolderPath depends on OS profile/folder state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentGetEnvironmentVariableAsImpureEvidence()
        {
            const string source = @"
#nullable enable
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? TestMethod()
    {
        return Environment.GetEnvironmentVariable(""PATH"");
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.GetEnvironmentVariable(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.GetEnvironmentVariable depends on ambient environment state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentGetEnvironmentVariablesAsImpureEvidence()
        {
            const string source = @"
using System;
using System.Collections;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IDictionary TestMethod()
    {
        return Environment.GetEnvironmentVariables();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.GetEnvironmentVariables().");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.GetEnvironmentVariables() depends on ambient environment state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentGetEnvironmentVariablesWithTargetAsImpureEvidence()
        {
            const string source = @"
using System;
using System.Collections;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IDictionary TestMethod()
    {
        return Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.GetEnvironmentVariables(EnvironmentVariableTarget).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.GetEnvironmentVariables(EnvironmentVariableTarget) depends on ambient environment state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentSetEnvironmentVariableAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Environment.SetEnvironmentVariable(""PATH"", ""value"");
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.SetEnvironmentVariable(string, string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.SetEnvironmentVariable(string, string) mutates ambient environment state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentSetEnvironmentVariableWithTargetAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Environment.SetEnvironmentVariable(""PATH"", ""value"", EnvironmentVariableTarget.Process);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.SetEnvironmentVariable(string, string, EnvironmentVariableTarget).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.SetEnvironmentVariable(string, string, EnvironmentVariableTarget) mutates ambient environment state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentExpandEnvironmentVariablesAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.ExpandEnvironmentVariables(""%PATH%"");
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.ExpandEnvironmentVariables(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.ExpandEnvironmentVariables(string) depends on ambient environment state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_CultureAndRegionAmbientStateAsImpureEvidence()
        {
            const string source = @"
using System.Globalization;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public object CurrentCulture()
    {
        return CultureInfo.CurrentCulture;
    }

    [EnforcePure]
    public object CurrentUICulture()
    {
        return CultureInfo.CurrentUICulture;
    }

    [EnforcePure]
    public object DefaultThreadCurrentCulture()
    {
        return CultureInfo.DefaultThreadCurrentCulture;
    }

    [EnforcePure]
    public object DefaultThreadCurrentUICulture()
    {
        return CultureInfo.DefaultThreadCurrentUICulture;
    }

    [EnforcePure]
    public object CurrentNumberFormat()
    {
        return NumberFormatInfo.CurrentInfo;
    }

    [EnforcePure]
    public object CurrentDateTimeFormat()
    {
        return DateTimeFormatInfo.CurrentInfo;
    }

    [EnforcePure]
    public object InstalledUICulture()
    {
        return CultureInfo.InstalledUICulture;
    }

    [EnforcePure]
    public object CurrentRegion()
    {
        return RegionInfo.CurrentRegion;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedGetters = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Globalization.CultureInfo.CurrentCulture.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "CultureInfo.CurrentCulture"))
                        .Symbol!).GetMethod!),
                (
                    "System.Globalization.CultureInfo.CurrentUICulture.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "CultureInfo.CurrentUICulture"))
                        .Symbol!).GetMethod!),
                (
                    "System.Globalization.CultureInfo.DefaultThreadCurrentCulture.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "CultureInfo.DefaultThreadCurrentCulture"))
                        .Symbol!).GetMethod!),
                (
                    "System.Globalization.CultureInfo.DefaultThreadCurrentUICulture.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "CultureInfo.DefaultThreadCurrentUICulture"))
                        .Symbol!).GetMethod!),
                (
                    "System.Globalization.NumberFormatInfo.CurrentInfo.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "NumberFormatInfo.CurrentInfo"))
                        .Symbol!).GetMethod!),
                (
                    "System.Globalization.DateTimeFormatInfo.CurrentInfo.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "DateTimeFormatInfo.CurrentInfo"))
                        .Symbol!).GetMethod!),
                (
                    "System.Globalization.CultureInfo.InstalledUICulture.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "CultureInfo.InstalledUICulture"))
                        .Symbol!).GetMethod!),
                (
                    "System.Globalization.RegionInfo.CurrentRegion.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "RegionInfo.CurrentRegion"))
                        .Symbol!).GetMethod!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedGetters.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(8));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            foreach (var label in classifications.Keys)
            {
                Assert.That(classifications[label].matched, Is.True,
                    "Generated purity catalog should resolve " + label + ".");
                Assert.That(classifications[label].classification, Is.EqualTo("impure"),
                    "Generated purity catalog should classify " + label + " as impure.");
            }
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_CultureInfoNameAsImpureEvidence()
        {
            const string source = @"
using System.Globalization;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(CultureInfo culture)
    {
        return culture.Name;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var methodSymbol = ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "culture.Name"))
                .Symbol!).GetMethod!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Id, Is.EqualTo(PurelySharpDiagnostics.PurityNotVerifiedId));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve System.Globalization.CultureInfo.Name.get.");
            Assert.That(classification, Is.EqualTo("impure"),
                "CultureInfo.Name caches instance state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ConsoleAmbientStateAsImpureEvidence()
        {
            const string source = @"
using System;
using System.IO;
using System.Text;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? ReadLine()
    {
        return Console.ReadLine();
    }

    [EnforcePure]
    public TextWriter Error()
    {
        return Console.Error;
    }

    [EnforcePure]
    public TextReader Input()
    {
        return Console.In;
    }

    [EnforcePure]
    public TextWriter Output()
    {
        return Console.Out;
    }

    [EnforcePure]
    public Encoding InputEncoding()
    {
        return Console.InputEncoding;
    }

    [EnforcePure]
    public bool IsErrorRedirected()
    {
        return Console.IsErrorRedirected;
    }

    [EnforcePure]
    public bool IsInputRedirected()
    {
        return Console.IsInputRedirected;
    }

    [EnforcePure]
    public bool IsOutputRedirected()
    {
        return Console.IsOutputRedirected;
    }

    [EnforcePure]
    public Encoding OutputEncoding()
    {
        return Console.OutputEncoding;
    }

    [EnforcePure]
    public Stream StandardError()
    {
        return Console.OpenStandardError();
    }

    [EnforcePure]
    public Stream StandardInput()
    {
        return Console.OpenStandardInput();
    }

    [EnforcePure]
    public Stream StandardOutput()
    {
        return Console.OpenStandardOutput();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Console.ReadLine()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.ReadLine()"))
                        .Symbol!),
                (
                    "System.Console.Error.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.Error"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.In.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.In"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.Out.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.Out"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.InputEncoding.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.InputEncoding"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.IsErrorRedirected.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.IsErrorRedirected"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.IsInputRedirected.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.IsInputRedirected"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.IsOutputRedirected.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.IsOutputRedirected"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.OutputEncoding.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.OutputEncoding"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.OpenStandardError()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.OpenStandardError()"))
                        .Symbol!),
                (
                    "System.Console.OpenStandardInput()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.OpenStandardInput()"))
                        .Symbol!),
                (
                    "System.Console.OpenStandardOutput()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.OpenStandardOutput()"))
                        .Symbol!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(12));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            foreach (var label in classifications.Keys)
            {
                Assert.That(classifications[label].matched, Is.True,
                    "Generated purity catalog should resolve " + label + ".");
                Assert.That(classifications[label].classification, Is.EqualTo("impure"),
                    "Generated purity catalog should classify " + label + " as impure.");
            }
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ConsoleObservableGettersAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConsoleColor BackgroundColor()
    {
        return Console.BackgroundColor;
    }

    [EnforcePure]
    public int BufferHeight()
    {
        return Console.BufferHeight;
    }

    [EnforcePure]
    public int BufferWidth()
    {
        return Console.BufferWidth;
    }

    [EnforcePure]
    public bool CapsLock()
    {
        return Console.CapsLock;
    }

    [EnforcePure]
    public int CursorLeft()
    {
        return Console.CursorLeft;
    }

    [EnforcePure]
    public int CursorSize()
    {
        return Console.CursorSize;
    }

    [EnforcePure]
    public int CursorTop()
    {
        return Console.CursorTop;
    }

    [EnforcePure]
    public bool CursorVisible()
    {
        return Console.CursorVisible;
    }

    [EnforcePure]
    public ConsoleColor ForegroundColor()
    {
        return Console.ForegroundColor;
    }

    [EnforcePure]
    public int LargestWindowHeight()
    {
        return Console.LargestWindowHeight;
    }

    [EnforcePure]
    public int LargestWindowWidth()
    {
        return Console.LargestWindowWidth;
    }

    [EnforcePure]
    public bool NumberLock()
    {
        return Console.NumberLock;
    }

    [EnforcePure]
    public string Title()
    {
        return Console.Title;
    }

    [EnforcePure]
    public bool TreatControlCAsInput()
    {
        return Console.TreatControlCAsInput;
    }

    [EnforcePure]
    public int WindowHeight()
    {
        return Console.WindowHeight;
    }

    [EnforcePure]
    public int WindowLeft()
    {
        return Console.WindowLeft;
    }

    [EnforcePure]
    public int WindowTop()
    {
        return Console.WindowTop;
    }

    [EnforcePure]
    public int WindowWidth()
    {
        return Console.WindowWidth;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Console.BackgroundColor.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.BackgroundColor"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.BufferHeight.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.BufferHeight"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.BufferWidth.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.BufferWidth"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.CapsLock.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.CapsLock"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.CursorLeft.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.CursorLeft"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.CursorSize.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.CursorSize"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.CursorTop.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.CursorTop"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.CursorVisible.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.CursorVisible"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.ForegroundColor.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.ForegroundColor"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.LargestWindowHeight.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.LargestWindowHeight"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.LargestWindowWidth.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.LargestWindowWidth"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.NumberLock.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.NumberLock"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.Title.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.Title"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.TreatControlCAsInput.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.TreatControlCAsInput"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.WindowHeight.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.WindowHeight"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.WindowLeft.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.WindowLeft"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.WindowTop.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.WindowTop"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.WindowWidth.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.WindowWidth"))
                        .Symbol!).GetMethod!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(18));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            foreach (var label in classifications.Keys)
            {
                Assert.That(classifications[label].matched, Is.True,
                    "Generated purity catalog should resolve " + label + ".");
                Assert.That(classifications[label].classification, Is.EqualTo("impure"),
                    "Generated purity catalog should classify " + label + " as impure.");
            }
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ConsoleOutputMembersAsImpureEvidence()
        {
            const string source = @"
using System;
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void WriteString()
    {
        Console.Write(""impure"");
    }

    [EnforcePure]
    public void WriteObject()
    {
        Console.Write(new object());
    }

    [EnforcePure]
    public void WriteLine()
    {
        Console.WriteLine();
    }

    [EnforcePure]
    public void WriteLineString()
    {
        Console.WriteLine(""impure"");
    }

    [EnforcePure]
    public void WriteLineObject()
    {
        Console.WriteLine(new object());
    }

    [EnforcePure]
    public void WriteLineInt()
    {
        Console.WriteLine(42);
    }

    [EnforcePure]
    public void SetOut()
    {
        Console.SetOut(TextWriter.Null);
    }

    [EnforcePure]
    public void SetError()
    {
        Console.SetError(TextWriter.Null);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Console.Write(string)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.Write(\"impure\")"))
                        .Symbol!),
                (
                    "System.Console.Write(object)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.Write(new object())"))
                        .Symbol!),
                (
                    "System.Console.WriteLine()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.WriteLine()"))
                        .Symbol!),
                (
                    "System.Console.WriteLine(string)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.WriteLine(\"impure\")"))
                        .Symbol!),
                (
                    "System.Console.WriteLine(object)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.WriteLine(new object())"))
                        .Symbol!),
                (
                    "System.Console.WriteLine(int)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.WriteLine(42)"))
                        .Symbol!),
                (
                    "System.Console.SetOut(System.IO.TextWriter)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.SetOut(TextWriter.Null)"))
                        .Symbol!),
                (
                    "System.Console.SetError(System.IO.TextWriter)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.SetError(TextWriter.Null)"))
                        .Symbol!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(8));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            foreach (var label in classifications.Keys)
            {
                Assert.That(classifications[label].matched, Is.True,
                    "Generated purity catalog should resolve " + label + ".");
                Assert.That(classifications[label].classification, Is.EqualTo("impure"),
                    "Generated purity catalog should classify " + label + " as impure.");
            }
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ConsoleControlMembersAsImpureEvidence()
        {
            const string source = @"
using System;
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Clear()
    {
        Console.Clear();
    }

    [EnforcePure]
    public void Beep()
    {
        Console.Beep();
    }

    [EnforcePure]
    public ConsoleKeyInfo ReadKey()
    {
        return Console.ReadKey();
    }

    [EnforcePure]
    public void SetCursorPosition()
    {
        Console.SetCursorPosition(0, 0);
    }

    [EnforcePure]
    public void SetIn(TextReader reader)
    {
        Console.SetIn(reader);
    }

    [EnforcePure]
    public bool KeyAvailable()
    {
        return Console.KeyAvailable;
    }

    [EnforcePure]
    public void SetBufferHeight()
    {
        Console.BufferHeight = 1;
    }

    [EnforcePure]
    public void SetTitle()
    {
        Console.Title = ""impure"";
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, ISymbol Symbol)[]
            {
                (
                    "System.Console.Clear()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.Clear()"))
                        .Symbol!),
                (
                    "System.Console.Beep()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.Beep()"))
                        .Symbol!),
                (
                    "System.Console.ReadKey()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.ReadKey()"))
                        .Symbol!),
                (
                    "System.Console.SetCursorPosition(int, int)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.SetCursorPosition(0, 0)"))
                        .Symbol!),
                (
                    "System.Console.SetIn(System.IO.TextReader)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.SetIn(reader)"))
                        .Symbol!),
                (
                    "System.Console.get_KeyAvailable()",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.KeyAvailable"))
                        .Symbol!).GetMethod!),
                (
                    "System.Console.set_BufferHeight(int)",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.BufferHeight"))
                        .Symbol!).SetMethod!),
                (
                    "System.Console.set_Title(string)",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "Console.Title"))
                        .Symbol!).SetMethod!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(8));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            foreach (var label in classifications.Keys)
            {
                Assert.That(classifications[label].matched, Is.True,
                    "Generated purity catalog should resolve " + label + ".");
                Assert.That(classifications[label].classification, Is.EqualTo("impure"),
                    "Generated purity catalog should classify " + label + " as impure.");
            }
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentCommandLineAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.CommandLine;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.CommandLine");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.CommandLine.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.CommandLine depends on ambient process state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentVersionAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Version TestMethod()
    {
        return Environment.Version;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.Version");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.Version.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.Version depends on runtime assembly metadata and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_CultureInfoGetCultureInfoAsImpureEvidence()
        {
            const string source = @"
using System.Globalization;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public CultureInfo TestMethod()
    {
        return CultureInfo.GetCultureInfo(""en-US"");
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "CultureInfo.GetCultureInfo(\"en-US\")");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Globalization.CultureInfo.GetCultureInfo(string)"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve CultureInfo.GetCultureInfo(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "CultureInfo.GetCultureInfo(string) depends on shared culture caches and exception construction, so it should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentProcessIdAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Environment.ProcessId;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.ProcessId");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.ProcessId.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.ProcessId depends on process/runtime state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ConvertChangeTypeTypeOverloadAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public object TestMethod(object value)
    {
        return Convert.ChangeType(value, typeof(int));
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Convert.ChangeType(value, typeof(int))");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Convert.ChangeType"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Convert.ChangeType(object, System.Type).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Convert.ChangeType(object, System.Type) depends on culture-sensitive conversion helpers and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_MarshalPtrToStructureAsImpureEvidence()
        {
            const string source = @"
using System;
using System.Runtime.InteropServices;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(IntPtr ptr)
    {
        return Marshal.PtrToStructure<int>(ptr);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Marshal.PtrToStructure<int>(ptr)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Runtime.InteropServices.Marshal.PtrToStructure"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Marshal.PtrToStructure<T>(IntPtr).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Marshal.PtrToStructure<T>(IntPtr) should remain generated impure because it depends on runtime marshalling behavior.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ClaimsPrincipalIsInRoleAsImpureEvidence()
        {
            const string source = @"
using System.Security.Claims;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ClaimsPrincipal principal)
    {
        return principal.IsInRole(""admin"");
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "principal.IsInRole(\"admin\")");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Security.Claims.ClaimsPrincipal.IsInRole"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve ClaimsPrincipal.IsInRole(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "ClaimsPrincipal.IsInRole(string) should remain generated impure because it traverses claims identity state through impure callees.");
        }

        [Test]
        public async Task Ps0002_ProcessGetCurrentProcess_UsesGeneratedPuritySummarySource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Process TestMethod()
    {
        return Process.GetCurrentProcess();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Process.GetCurrentProcess"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ProcessMembersAsImpureEvidence()
        {
            const string source = @"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Process Current()
    {
        return Process.GetCurrentProcess();
    }

    [EnforcePure]
    public int Id(Process process)
    {
        return process.Id;
    }

    [EnforcePure]
    public ProcessStartInfo StartInfo(Process process)
    {
        return process.StartInfo;
    }

    [EnforcePure]
    public int ExitCode(Process process)
    {
        return process.ExitCode;
    }

    [EnforcePure]
    public Process Start()
    {
        return Process.Start(""tool"");
    }

    [EnforcePure]
    public Process[] ByName()
    {
        return Process.GetProcessesByName(""dotnet"");
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, ISymbol Symbol)[]
            {
                (
                    "System.Diagnostics.Process.GetCurrentProcess()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Process.GetCurrentProcess()"))
                        .Symbol!),
                (
                    "System.Diagnostics.Process.get_Id()",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "process.Id"))
                        .Symbol!).GetMethod!),
                (
                    "System.Diagnostics.Process.get_StartInfo()",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "process.StartInfo"))
                        .Symbol!).GetMethod!),
                (
                    "System.Diagnostics.Process.get_ExitCode()",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "process.ExitCode"))
                        .Symbol!).GetMethod!),
                (
                    "System.Diagnostics.Process.Start(string)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Process.Start(\"tool\")"))
                        .Symbol!),
                (
                    "System.Diagnostics.Process.GetProcessesByName(string)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "Process.GetProcessesByName(\"dotnet\")"))
                        .Symbol!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(6));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            foreach (var label in classifications.Keys)
            {
                Assert.That(classifications[label].matched, Is.True,
                    "Generated purity catalog should resolve " + label + ".");
                Assert.That(classifications[label].classification, Is.EqualTo("impure"),
                    "Generated purity catalog should classify " + label + " as impure.");
            }
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentSystemDirectoryAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.SystemDirectory;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.SystemDirectory");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.SystemDirectory.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.SystemDirectory is OS-dependent path state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentUserInteractiveAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return Environment.UserInteractive;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.UserInteractive");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.UserInteractive.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.UserInteractive depends on ambient process/UI state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentUserNameAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.UserName;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.UserName");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.UserName.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.UserName depends on ambient identity state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_VersionPureMembers()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        var left = new Version(1, 2, 3, 4);
        var right = new Version(1, 2, 3, 4);
        return left.CompareTo(right) == 0 &&
            left.Equals(right) &&
            left.Major == 1;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var constructor = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .First(node => node.ToString() == "new Version(1, 2, 3, 4)"))
                .Symbol!;
            var compareTo = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .First(node => node.ToString() == "left.CompareTo(right)"))
                .Symbol!;
            var equals = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .First(node => node.ToString() == "left.Equals(right)"))
                .Symbol!;
            var majorGetter = ((IPropertySymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .First(node => node.ToString() == "left.Major"))
                .Symbol!).GetMethod!;
            var trackedMethods = new[]
            {
                constructor.OriginalDefinition,
                compareTo.OriginalDefinition,
                equals.OriginalDefinition,
                majorGetter.OriginalDefinition,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Version constructors, comparisons, and getters.");
            Assert.That(matched, Is.EqualTo(new[] { true, true, true, true }),
                "Generated purity catalog should resolve the tracked Version members to their runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_BitConverterGetBytesAsPureFreshArrayEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(int value)
    {
        return BitConverter.GetBytes(value);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "BitConverter.GetBytes(value)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = matched
                ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                : string.Empty;
            var freshnessClassification = matched
                ? (string)purityEntry.GetType().GetProperty("FreshnessClassification")!.GetValue(purityEntry)!
                : string.Empty;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow BitConverter.GetBytes(int) without the manual pure catalog.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve BitConverter.GetBytes(int) to runtime-backed purity evidence.");
            Assert.That(classification, Is.EqualTo("pure"));
            Assert.That(freshnessClassification, Is.EqualTo("fresh_owned_array_write"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayEmptyAsPureSafeStaticCacheEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        _ = Array.Empty<int>();
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Array.Empty<int>()");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2];
            var classification = matched && purityEntry != null
                ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                : string.Empty;
            var freshnessClassification = matched && purityEntry != null
                ? (string)purityEntry.GetType().GetProperty("FreshnessClassification")!.GetValue(purityEntry)!
                : string.Empty;
            var effectVisibilityClassification = matched && purityEntry != null
                ? (string)purityEntry.GetType().GetProperty("EffectVisibilityClassification")!.GetValue(purityEntry)!
                : string.Empty;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow standalone Array.Empty<int>() invocations without the manual pure catalog.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Array.Empty<T>() to runtime-backed purity evidence.");
            Assert.That(classification, Is.EqualTo("pure"));
            Assert.That(freshnessClassification, Is.EqualTo("none"));
            Assert.That(effectVisibilityClassification, Is.EqualTo("internal_only"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_TypeGetTypeFromHandleAsPureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type TestMethod()
    {
        return Type.GetTypeFromHandle(default(RuntimeTypeHandle));
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Type.GetTypeFromHandle(default(RuntimeTypeHandle))");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Type.GetTypeFromHandle(RuntimeTypeHandle).");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Type.GetTypeFromHandle(RuntimeTypeHandle) to its runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_TypeIdentityHelpersAsPureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Type left, Type right, object boxed)
    {
        var sameType = left.Equals(right);
        var sameObject = left.Equals(boxed);
        return sameType == sameObject ? left.GetHashCode() : right.GetHashCode();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocations = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "left.Equals(right)" ||
                    node.ToString() == "left.Equals(boxed)" ||
                    node.ToString() == "left.GetHashCode()" ||
                    node.ToString() == "right.GetHashCode()")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = invocations.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var methodSymbol = (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2];
                    var classification = matched
                        ? (string)purityEntry!.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Type.Equals(Type), Type.Equals(object), and Type.GetHashCode().");
            Assert.That(classifications["left.Equals(right)"].matched, Is.True);
            Assert.That(classifications["left.Equals(right)"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["left.Equals(boxed)"].matched, Is.True);
            Assert.That(classifications["left.Equals(boxed)"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["left.GetHashCode()"].matched, Is.True);
            Assert.That(classifications["left.GetHashCode()"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["right.GetHashCode()"].matched, Is.True);
            Assert.That(classifications["right.GetHashCode()"].classification, Is.EqualTo("pure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ObjectGetTypeAsPureMetadataEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type TestMethod(object value)
    {
        return value.GetType();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node => node.ToString() == "value.GetType()")
                .Select(node => (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow object.GetType() as metadata-only read.");
            Assert.That(matched, Is.EqualTo(new[] { true }),
                "Generated purity catalog should resolve object.GetType() from runtime metadata evidence.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_TypeMetadataGettersAsPureEvidence()
        {
            const string source = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public object? TestMethod(Type type)
    {
        _ = type.DeclaringMethod;
        _ = type.DeclaringType;
        _ = type.IsAbstract;
        _ = type.IsAnsiClass;
        _ = type.IsArray;
        _ = type.Attributes;
        _ = type.IsAutoClass;
        _ = type.IsAutoLayout;
        _ = type.IsByRef;
        _ = type.IsClass;
        _ = type.IsCOMObject;
        _ = type.IsContextful;
        _ = type.IsExplicitLayout;
        _ = type.IsGenericType;
        _ = type.IsGenericTypeDefinition;
        _ = type.IsGenericParameter;
        _ = type.IsImport;
        _ = type.IsInterface;
        _ = type.IsLayoutSequential;
        _ = type.IsMarshalByRef;
        _ = type.IsNested;
        _ = type.IsNestedAssembly;
        _ = type.IsNestedFamANDAssem;
        _ = type.IsNestedFamORAssem;
        _ = type.IsNestedFamily;
        _ = type.IsNestedPrivate;
        _ = type.IsNestedPublic;
        _ = type.IsNotPublic;
        _ = type.IsPointer;
        _ = type.IsPrimitive;
        _ = type.IsPublic;
        _ = type.IsSealed;
        _ = type.IsSpecialName;
        _ = type.IsUnicodeClass;
        _ = type.IsValueType;
        _ = type.MemberType;
        return type.ReflectedType;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var trackedMembers = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "type.DeclaringMethod" ||
                    node.ToString() == "type.DeclaringType" ||
                    node.ToString() == "type.IsAbstract" ||
                    node.ToString() == "type.IsAnsiClass" ||
                    node.ToString() == "type.IsArray" ||
                    node.ToString() == "type.Attributes" ||
                    node.ToString() == "type.IsAutoClass" ||
                    node.ToString() == "type.IsAutoLayout" ||
                    node.ToString() == "type.IsByRef" ||
                    node.ToString() == "type.IsClass" ||
                    node.ToString() == "type.IsCOMObject" ||
                    node.ToString() == "type.IsContextful" ||
                    node.ToString() == "type.IsExplicitLayout" ||
                    node.ToString() == "type.IsGenericType" ||
                    node.ToString() == "type.IsGenericTypeDefinition" ||
                    node.ToString() == "type.IsGenericParameter" ||
                    node.ToString() == "type.IsImport" ||
                    node.ToString() == "type.IsInterface" ||
                    node.ToString() == "type.IsLayoutSequential" ||
                    node.ToString() == "type.IsMarshalByRef" ||
                    node.ToString() == "type.IsNested" ||
                    node.ToString() == "type.IsNestedAssembly" ||
                    node.ToString() == "type.IsNestedFamANDAssem" ||
                    node.ToString() == "type.IsNestedFamORAssem" ||
                    node.ToString() == "type.IsNestedFamily" ||
                    node.ToString() == "type.IsNestedPrivate" ||
                    node.ToString() == "type.IsNestedPublic" ||
                    node.ToString() == "type.IsNotPublic" ||
                    node.ToString() == "type.IsPointer" ||
                    node.ToString() == "type.IsPrimitive" ||
                    node.ToString() == "type.IsPublic" ||
                    node.ToString() == "type.IsSealed" ||
                    node.ToString() == "type.IsSpecialName" ||
                    node.ToString() == "type.IsUnicodeClass" ||
                    node.ToString() == "type.IsValueType" ||
                    node.ToString() == "type.MemberType" ||
                    node.ToString() == "type.ReflectedType")
                .Select(node => (node.ToString(), (IPropertySymbol)semanticModel.GetSymbolInfo(node).Symbol!))
                .ToArray();
            var resolutions = trackedMembers.ToDictionary(
                trackedMember => trackedMember.Item1,
                trackedMember =>
                {
                    var args = new object?[] { trackedMember.Item2.GetMethod!.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                },
                StringComparer.Ordinal);

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the reviewed System.Type metadata getters.");
            Assert.That(resolutions["type.DeclaringMethod"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.DeclaringType"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsAbstract"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsAnsiClass"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsArray"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.Attributes"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsAutoClass"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsAutoLayout"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsByRef"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsClass"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsCOMObject"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsContextful"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsExplicitLayout"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsGenericType"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsGenericTypeDefinition"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsGenericParameter"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsImport"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsInterface"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsLayoutSequential"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsMarshalByRef"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsNested"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsNestedAssembly"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsNestedFamANDAssem"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsNestedFamORAssem"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsNestedFamily"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsNestedPrivate"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsNestedPublic"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsNotPublic"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsPointer"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsPrimitive"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsPublic"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsSealed"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsSpecialName"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsUnicodeClass"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.IsValueType"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.MemberType"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["type.ReflectedType"], Is.EqualTo((true, "pure")));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_PathCombineAsPureEvidence()
        {
            const string source = @"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string left, string right)
    {
        return Path.Combine(left, right);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Path.Combine(left, right)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Path.Combine(string, string).");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Path.Combine(string, string) from runtime metadata evidence.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_PathGetFileNameAsPureEvidence()
        {
            const string source = @"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? TestMethod(string path)
    {
        return Path.GetFileName(path);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Path.GetFileName(path)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Path.GetFileName(string).");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Path.GetFileName(string) from runtime metadata evidence.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ObjectReferenceEqualsTupleFactoriesAndArraySegmentConstructors()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int[] values, object left, object right)
    {
        var same = object.ReferenceEquals(left, right);
        var whole = new ArraySegment<int>(values);
        var prefix = new ArraySegment<int>(values, 0, 1);
        var tuple = Tuple.Create(1, 2);
        var valueTuple = ValueTuple.Create(1, 2);
        return same ? 1 : values.Length;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "object.ReferenceEquals(left, right)"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<ObjectCreationExpressionSyntax>()
                        .Single(node => node.ToString() == "new ArraySegment<int>(values)"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<ObjectCreationExpressionSyntax>()
                        .Single(node => node.ToString() == "new ArraySegment<int>(values, 0, 1)"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "Tuple.Create(1, 2)"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "ValueTuple.Create(1, 2)"))
                    .Symbol!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow object.ReferenceEquals, ArraySegment constructors, Tuple.Create, and ValueTuple.Create.");
            Assert.That(matched, Is.EqualTo(new[] { true, true, true, true, true }),
                "Generated purity catalog should resolve the tracked ReferenceEquals, ArraySegment, Tuple, and ValueTuple members.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_PureCoreConstructorsAndValueTypes()
        {
            const string source = @"
using System;
using System.IO;
using System.Runtime.CompilerServices;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var argument = new ArgumentException(""bad argument"", ""value"");
        var divideByZero = new DivideByZeroException();
        var flags = new FlagsAttribute();
        var format = new FormatException(""bad format"");
        var index = new Index(2, false);
        var endOfStream = new EndOfStreamException();
        var invalidOperation = new InvalidOperationException(""bad operation"");
        var notImplemented = new NotImplementedException();
        var notSupported = new NotSupportedException(""unsupported"");
        var obsolete = new ObsoleteAttribute(""legacy"");
        var overflow = new OverflowException();
        var platformNotSupported = new PlatformNotSupportedException();
        var range = new Range(new Index(0, false), new Index(1, false));
        var callerArgument = new CallerArgumentExpressionAttribute(""value"");
        var methodImpl = new MethodImplAttribute(MethodImplOptions.AggressiveInlining);
        var serializable = new SerializableAttribute();
        var pointer = new UIntPtr(1u);
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedExpressions = new[]
            {
                "new ArgumentException(\"bad argument\", \"value\")",
                "new DivideByZeroException()",
                "new FlagsAttribute()",
                "new FormatException(\"bad format\")",
                "new Index(2, false)",
                "new EndOfStreamException()",
                "new InvalidOperationException(\"bad operation\")",
                "new NotImplementedException()",
                "new NotSupportedException(\"unsupported\")",
                "new ObsoleteAttribute(\"legacy\")",
                "new OverflowException()",
                "new PlatformNotSupportedException()",
                "new Range(new Index(0, false), new Index(1, false))",
                "new CallerArgumentExpressionAttribute(\"value\")",
                "new MethodImplAttribute(MethodImplOptions.AggressiveInlining)",
                "new SerializableAttribute()",
                "new UIntPtr(1u)",
            };
            var trackedMethods = trackedExpressions
                .Select(expressionText =>
                {
                    var symbol = semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<ObjectCreationExpressionSyntax>()
                            .Single(node => node.ToString() == expressionText))
                        .Symbol;
                    Assert.That(symbol, Is.Not.Null, expressionText);
                    return (IMethodSymbol)symbol!;
                })
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the probed core constructors and value-type constructors.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedExpressions.Length).ToArray()),
                "Generated purity catalog should resolve the tracked core constructor members.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_AdditionalPureExceptionConstructors()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var argumentNull = new ArgumentNullException(""value"");
        var disposed = new ObjectDisposedException(""stream"");
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedExpressions = new[]
            {
                "new ArgumentNullException(\"value\")",
                "new ObjectDisposedException(\"stream\")",
            };
            var trackedMethods = trackedExpressions
                .Select(expressionText =>
                {
                    var symbol = semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<ObjectCreationExpressionSyntax>()
                            .Single(node => node.ToString() == expressionText))
                        .Symbol;
                    Assert.That(symbol, Is.Not.Null, expressionText);
                    return (IMethodSymbol)symbol!;
                })
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the tracked exception constructors.");
            Assert.That(matched, Is.EqualTo(new[] { true, true }),
                "Generated purity catalog should resolve ArgumentNullException(string) and ObjectDisposedException(string).");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DateTimeStableGetterMembers()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(DateTime value)
    {
        var day = value.Day;
        var dayOfWeek = value.DayOfWeek;
        var dayOfYear = value.DayOfYear;
        var hour = value.Hour;
        var kind = value.Kind;
        var millisecond = value.Millisecond;
        var minute = value.Minute;
        var month = value.Month;
        var second = value.Second;
        var ticks = value.Ticks;
        var timeOfDay = value.TimeOfDay;
        return day;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedExpressions = new[]
            {
                "value.Day",
                "value.DayOfWeek",
                "value.DayOfYear",
                "value.Hour",
                "value.Kind",
                "value.Millisecond",
                "value.Minute",
                "value.Month",
                "value.Second",
                "value.Ticks",
                "value.TimeOfDay",
            };
            var trackedMethods = trackedExpressions
                .Select(expressionText =>
                {
                    var symbol = semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == expressionText))
                        .Symbol;
                    Assert.That(symbol, Is.Not.Null, expressionText);
                    return ((IPropertySymbol)symbol!).GetMethod!;
                })
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the tracked DateTime stable getters.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedExpressions.Length).ToArray()),
                "Generated purity catalog should resolve the tracked DateTime getter members.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DateTimeAndDateTimeOffsetAmbientStateAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public object Today()
    {
        return DateTime.Today;
    }

    [EnforcePure]
    public object Now()
    {
        return DateTime.Now;
    }

    [EnforcePure]
    public object UtcNow()
    {
        return DateTime.UtcNow;
    }

    [EnforcePure]
    public object LocalTime(DateTime value)
    {
        return value.ToLocalTime();
    }

    [EnforcePure]
    public object OffsetNow()
    {
        return DateTimeOffset.Now;
    }

    [EnforcePure]
    public object OffsetUtcNow()
    {
        return DateTimeOffset.UtcNow;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedGetters = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.DateTime.Today.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "DateTime.Today"))
                        .Symbol!).GetMethod!),
                (
                    "System.DateTime.Now.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "DateTime.Now"))
                        .Symbol!).GetMethod!),
                (
                    "System.DateTime.UtcNow.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "DateTime.UtcNow"))
                        .Symbol!).GetMethod!),
                (
                    "System.DateTime.ToLocalTime()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "value.ToLocalTime()"))
                        .Symbol!),
                (
                    "System.DateTimeOffset.Now.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "DateTimeOffset.Now"))
                        .Symbol!).GetMethod!),
                (
                    "System.DateTimeOffset.UtcNow.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "DateTimeOffset.UtcNow"))
                        .Symbol!).GetMethod!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedGetters.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(6));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            foreach (var label in classifications.Keys)
            {
                Assert.That(classifications[label].matched, Is.True,
                    "Generated purity catalog should resolve " + label + ".");
                Assert.That(classifications[label].classification, Is.EqualTo("impure"),
                    "Generated purity catalog should classify " + label + " as impure.");
            }
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_StringBuilderLengthAndHttpResponseSuccessStatusCode()
        {
            const string source = @"
using System.Net.Http;
using System.Text;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int BuilderLengthMethod(StringBuilder builder)
    {
        return builder.Length;
    }

    [EnforcePure]
    public bool HttpResponseMethod(HttpResponseMessage response)
    {
        return response.IsSuccessStatusCode;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedExpressions = new[]
            {
                "builder.Length",
                "response.IsSuccessStatusCode",
            };
            var trackedMethods = trackedExpressions
                .Select(expressionText =>
                {
                    var symbol = semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == expressionText))
                        .Symbol;
                    Assert.That(symbol, Is.Not.Null, expressionText);
                    return ((IPropertySymbol)symbol!).GetMethod!;
                })
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var resolutions = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                var purityEntry = args[2]!;
                var classification = matched
                    ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                    : string.Empty;
                return (matched, classification);
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow StringBuilder.Length and HttpResponseMessage.IsSuccessStatusCode.");
            Assert.That(resolutions.Select(result => result.matched).ToArray(), Is.EqualTo(new[] { true, true }),
                "Generated purity catalog should resolve StringBuilder.Length and HttpResponseMessage.IsSuccessStatusCode.");
            Assert.That(resolutions.Select(result => result.classification).ToArray(), Is.EqualTo(new[] { "pure", "pure" }),
                "Generated purity catalog should classify StringBuilder.Length and HttpResponseMessage.IsSuccessStatusCode as pure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_BooleanCompareAndCharClassificationHelpers()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(bool left, bool right, char value, char other)
    {
        var compare = left.CompareTo(right);
        var codePoint = char.ConvertToUtf32(value, other);
        var numeric = char.GetNumericValue(value);
        var isControl = char.IsControl(value);
        var isDigit = char.IsDigit(value);
        var isLetter = char.IsLetter(value);
        var isLower = char.IsLower(value);
        var isNumber = char.IsNumber(value);
        var isPunctuation = char.IsPunctuation(value);
        var isSeparator = char.IsSeparator(value);
        var isSymbol = char.IsSymbol(value);
        var isUpper = char.IsUpper(value);
        var isWhiteSpace = char.IsWhiteSpace(value);
        var lowerInvariant = char.ToLowerInvariant(value);
        var upperInvariant = char.ToUpperInvariant(value);
        return compare;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedExpressions = new[]
            {
                "left.CompareTo(right)",
                "char.ConvertToUtf32(value, other)",
                "char.GetNumericValue(value)",
                "char.IsControl(value)",
                "char.IsDigit(value)",
                "char.IsLetter(value)",
                "char.IsLower(value)",
                "char.IsNumber(value)",
                "char.IsPunctuation(value)",
                "char.IsSeparator(value)",
                "char.IsSymbol(value)",
                "char.IsUpper(value)",
                "char.IsWhiteSpace(value)",
                "char.ToLowerInvariant(value)",
                "char.ToUpperInvariant(value)",
            };
            var classifications = trackedExpressions.ToDictionary(
                expressionText => expressionText,
                expressionText =>
                {
                    var symbol = semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == expressionText))
                        .Symbol;
                    Assert.That(symbol, Is.Not.Null, expressionText);
                    return (IMethodSymbol)symbol!;
                });
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var resolutions = classifications.ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    var args = new object?[] { pair.Value.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2];
                    var classification = matched
                        ? (string)purityEntry!.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });
            var pureTrackedExpressions = trackedExpressions
                .Where(expressionText => expressionText != "char.ConvertToUtf32(value, other)")
                .ToArray();

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("char.ConvertToUtf32(char, char)"));
            foreach (var expressionText in pureTrackedExpressions)
            {
                Assert.That(resolutions[expressionText].matched, Is.True, "Generated purity catalog should resolve " + expressionText + ".");
                Assert.That(resolutions[expressionText].classification, Is.EqualTo("pure"),
                    expressionText + " should remain a generated pure helper.");
            }

            Assert.That(resolutions["char.ConvertToUtf32(value, other)"].matched, Is.True,
                "Generated purity catalog should resolve char.ConvertToUtf32(value, other).");
            Assert.That(resolutions["char.ConvertToUtf32(value, other)"].classification, Is.EqualTo("impure"),
                "char.ConvertToUtf32(char, char) now flows through generated impure evidence.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_CharConvertFromUtf32AsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int codePoint)
    {
        return char.ConvertFromUtf32(codePoint);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "char.ConvertFromUtf32(codePoint)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("char.ConvertFromUtf32(int)"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve char.ConvertFromUtf32(int).");
            Assert.That(classification, Is.EqualTo("impure"),
                "char.ConvertFromUtf32 can throw for invalid code points and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_IndexAndHashCodeHelpers()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        HashCode hash = default;
        var copy = new HashCode();
        var end = Index.End;
        var start = Index.Start;
        return hash.ToHashCode() + copy.ToHashCode();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var hashCode = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "hash.ToHashCode()"))
                .Symbol!;
            var copyHashCode = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "copy.ToHashCode()"))
                .Symbol!;
            var hashCodeConstructor = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .Single(node => node.ToString() == "new HashCode()"))
                .Symbol!;
            var endGetter = ((IPropertySymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Single(node => node.ToString() == "Index.End"))
                .Symbol!).GetMethod!;
            var startGetter = ((IPropertySymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Single(node => node.ToString() == "Index.Start"))
                .Symbol!).GetMethod!;
            var trackedMethods = new[]
            {
                hashCodeConstructor.OriginalDefinition,
                hashCode.OriginalDefinition,
                copyHashCode.OriginalDefinition,
                endGetter.OriginalDefinition,
                startGetter.OriginalDefinition,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the implicit HashCode constructor, HashCode.ToHashCode, and Index getters.");
            Assert.That(matched, Is.EqualTo(new[] { true, true, true, true, true }),
                "Generated purity catalog should resolve the tracked index and hash helpers, including new HashCode().");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_SpanAndMemoryMarshalHelpers()
        {
            const string source = @"
using System;
using System.Runtime.InteropServices;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ReadOnlySpan<int> readOnly, Span<int> writable)
    {
        var head = readOnly.Slice(0, 0);
        var readOnlyBytes = MemoryMarshal.AsBytes(readOnly);
        var writableBytes = MemoryMarshal.AsBytes(writable);
        return readOnly.Length + writable.Length + head.Length + readOnlyBytes.Length + writableBytes.Length + (readOnly.IsEmpty ? 0 : 1) + (writable.IsEmpty ? 0 : 1);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "readOnly.Slice(0, 0)"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "MemoryMarshal.AsBytes(readOnly)"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "MemoryMarshal.AsBytes(writable)"))
                    .Symbol!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "readOnly.Length"))
                    .Symbol!).GetMethod!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "writable.Length"))
                    .Symbol!).GetMethod!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "readOnly.IsEmpty"))
                    .Symbol!).GetMethod!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "writable.IsEmpty"))
                    .Symbol!).GetMethod!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the tracked span and MemoryMarshal helpers.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve the tracked span and MemoryMarshal helpers.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ReadOnlySequenceHelpersAndSlice()
        {
            const string source = @"
using System.Buffers;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ReadOnlySequence<int> value)
    {
        var start = value.Start;
        var end = value.End;
        var slice = value.Slice(1L);
        return value.IsEmpty ? 0 : value.Length > slice.Length ? 1 : 2;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "value.Start"))
                    .Symbol!).GetMethod!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "value.End"))
                    .Symbol!).GetMethod!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "value.Length"))
                    .Symbol!).GetMethod!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "value.IsEmpty"))
                    .Symbol!).GetMethod!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "value.Slice(1L)"))
                    .Symbol!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the tracked ReadOnlySequence helpers and Slice(long).");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve the tracked ReadOnlySequence helpers and Slice(long).");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ListCapacity()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(List<int> values)
    {
        return values.Capacity;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "values.Capacity"))
                    .Symbol!).GetMethod!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow List<T>.Capacity.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve List<T>.Capacity.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ListConstructorsAsImpureEvidence()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public List<int> DefaultMethod()
    {
        return new List<int>();
    }

    [EnforcePure]
    public List<int> CapacityMethod()
    {
        return new List<int>(4);
    }

    [EnforcePure]
    public List<int> EnumerableMethod(IEnumerable<int> values)
    {
        return new List<int>(values);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "new List<int>()" ||
                    node.ToString() == "new List<int>(4)" ||
                    node.ToString() == "new List<int>(values)")
                .ToDictionary(
                    node => node.ToString(),
                    node => (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!);
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var resolutions = trackedMethods.ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    var args = new object?[] { pair.Value.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    var primaryCategory = matched
                        ? (string)purityEntry.GetType().GetProperty("PrimaryCategory")!.GetValue(purityEntry)!
                        : string.Empty;
                    var categories = matched
                        ? ((IEnumerable<string>)purityEntry.GetType().GetProperty("Categories")!.GetValue(purityEntry)!).ToArray()
                        : Array.Empty<string>();
                    var freshnessClassification = matched
                        ? (string)purityEntry.GetType().GetProperty("FreshnessClassification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification, primaryCategory, categories, freshnessClassification);
                });
            var impuritySymbols = purityDiagnostics
                .Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty])
                .ToArray();

            Assert.That(purityDiagnostics, Has.Length.EqualTo(3));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "ObjectCreationPurityRule" }));
            Assert.That(impuritySymbols, Has.Some.Contain("System.Collections.Generic.List<int>.List()"));
            Assert.That(impuritySymbols, Has.Some.Contain("System.Collections.Generic.List<int>.List(int)"));
            Assert.That(impuritySymbols, Has.Some.Contain("System.Collections.Generic.List<int>.List(System.Collections.Generic.IEnumerable<int>)"));

            Assert.That(resolutions["new List<int>()"].matched, Is.True,
                "Generated purity catalog should resolve List<T>..ctor().");
            Assert.That(resolutions["new List<int>()"].classification, Is.EqualTo("impure"),
                "List<T>..ctor() now resolves through generated impure runtime evidence.");
            Assert.That(resolutions["new List<int>()"].primaryCategory, Is.EqualTo("global_state_read"));
            Assert.That(resolutions["new List<int>()"].categories, Has.Member("global_state_read"));
            Assert.That(resolutions["new List<int>()"].categories, Has.Member("object_state_write"));
            Assert.That(resolutions["new List<int>()"].freshnessClassification, Is.EqualTo("none"));

            Assert.That(resolutions["new List<int>(4)"].matched, Is.True,
                "Generated purity catalog should resolve List<T>..ctor(int).");
            Assert.That(resolutions["new List<int>(4)"].classification, Is.EqualTo("impure"),
                "List<T>..ctor(int) now resolves through generated impure runtime evidence.");
            Assert.That(resolutions["new List<int>(4)"].primaryCategory, Is.EqualTo("global_state_read"));
            Assert.That(resolutions["new List<int>(4)"].categories, Has.Member("global_state_read"));
            Assert.That(resolutions["new List<int>(4)"].categories, Has.Member("object_state_write"));
            Assert.That(
                resolutions["new List<int>(4)"].freshnessClassification,
                Is.EqualTo("fresh_array_candidate_requires_non_pure_resolution"));

            Assert.That(resolutions["new List<int>(values)"].matched, Is.True,
                "Generated purity catalog should resolve List<T>..ctor(IEnumerable<T>).");
            Assert.That(resolutions["new List<int>(values)"].classification, Is.EqualTo("impure"),
                "List<T>..ctor(IEnumerable<T>) now resolves through generated impure runtime evidence.");
            Assert.That(resolutions["new List<int>(values)"].primaryCategory, Is.EqualTo("global_state_read"));
            Assert.That(resolutions["new List<int>(values)"].categories, Has.Member("global_state_read"));
            Assert.That(resolutions["new List<int>(values)"].categories, Has.Member("impure_callee"));
            Assert.That(resolutions["new List<int>(values)"].categories, Has.Member("object_state_write"));
            Assert.That(
                resolutions["new List<int>(values)"].freshnessClassification,
                Is.EqualTo("fresh_array_candidate_requires_non_pure_resolution"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ListCapacitySetterAsImpureEvidence()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(List<int> values)
    {
        values.Capacity = 1;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var methodSymbol = ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "values.Capacity"))
                .Symbol!).SetMethod!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("Capacity.set"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve List<T>.Capacity.set.");
            Assert.That(classification, Is.EqualTo("impure"),
                "List<T>.Capacity.set mutates list state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ListFindIndex()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(List<int> values)
    {
        return values.FindIndex(static value => value > 0);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "values.FindIndex(static value => value > 0)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow List<T>.FindIndex.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve List<T>.FindIndex.");
            Assert.That(classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify List<T>.FindIndex as pure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ListMutatorsAndCollectionsMarshalAsSpanAsImpureEvidence()
        {
            const string source = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void AddItem(List<int> values)
    {
        values.Add(1);
    }

    [EnforcePure]
    public void ClearItems(List<int> values)
    {
        values.Clear();
    }

    [EnforcePure]
    public void InsertItem(List<int> values)
    {
        values.Insert(0, 1);
    }

    [EnforcePure]
    public void RemoveItem(List<int> values)
    {
        values.Remove(1);
    }

    [EnforcePure]
    public void VisitItems(List<int> values)
    {
        values.ForEach(static _ => { });
    }

    [EnforcePure]
    public Span<int> ViewItems(List<int> values)
    {
        return CollectionsMarshal.AsSpan(values);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId).ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var trackedExpressions = new[]
            {
                "values.Add(1)",
                "values.Clear()",
                "values.Insert(0, 1)",
                "values.Remove(1)",
                "values.ForEach(static _ => { })",
                "CollectionsMarshal.AsSpan(values)",
            };
            var resolutions = trackedExpressions.ToDictionary(
                expression => expression,
                expression =>
                {
                    var invocation = syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == expression);
                    var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!;
                    var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });
            var impuritySymbols = purityDiagnostics
                .Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty])
                .ToArray();

            Assert.That(purityDiagnostics, Has.Length.EqualTo(trackedExpressions.Length));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "MethodInvocationPurityRule" }));
            Assert.That(impuritySymbols, Has.Some.Contain("System.Collections.Generic.List<T>.Add(T)"));
            Assert.That(impuritySymbols, Has.Some.Contain("System.Collections.Generic.List<T>.Clear()"));
            Assert.That(impuritySymbols, Has.Some.Contain("System.Collections.Generic.List<T>.Insert(int, T)"));
            Assert.That(impuritySymbols, Has.Some.Contain("System.Collections.Generic.List<T>.Remove(T)"));
            Assert.That(impuritySymbols, Has.Some.Contain("System.Collections.Generic.List<T>.ForEach(System.Action<T>)"));
            Assert.That(impuritySymbols, Has.Some.Contain("System.Runtime.InteropServices.CollectionsMarshal.AsSpan"));

            foreach (var expression in trackedExpressions)
            {
                Assert.That(resolutions[expression].matched, Is.True, $"Generated purity catalog should resolve {expression}.");
                Assert.That(resolutions[expression].classification, Is.EqualTo("impure"), $"{expression} should remain generated impure.");
            }
        }

        [Test]
        public void GeneratedPurityCatalog_Resolves_QueueTryPeek()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Queue<int> values)
    {
        return values.TryPeek(out var value);
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "values.TryPeek(out var value)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Queue<T>.TryPeek.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Generated purity catalog should classify Queue<T>.TryPeek as impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_QueueAndStackMutatorsAsGeneratedImpureEvidence()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void QueueMutate(Queue<int> queue)
    {
        queue.Enqueue(1);
        _ = queue.Dequeue();
    }

    [EnforcePure]
    public void StackMutate(Stack<int> stack)
    {
        stack.Push(1);
        _ = stack.Pop();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId).ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var trackedExpressions = new[]
            {
                "queue.Enqueue(1)",
                "queue.Dequeue()",
                "stack.Push(1)",
                "stack.Pop()",
            };
            var resolutions = trackedExpressions.ToDictionary(
                expression => expression,
                expression =>
                {
                    var invocation = syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == expression);
                    var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!;
                    var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });
            var impuritySymbols = purityDiagnostics
                .Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty])
                .ToArray();

            Assert.That(purityDiagnostics, Has.Length.EqualTo(2));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "MethodInvocationPurityRule" }));
            Assert.That(impuritySymbols, Has.Some.Contain("System.Collections.Generic.Queue<T>.Enqueue(T)"));
            Assert.That(impuritySymbols, Has.Some.Contain("System.Collections.Generic.Stack<T>.Push(T)"));

            foreach (var expression in trackedExpressions)
            {
                Assert.That(resolutions[expression].matched, Is.True, $"Generated purity catalog should resolve {expression}.");
                Assert.That(resolutions[expression].classification, Is.EqualTo("impure"), $"{expression} should remain generated impure.");
            }
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_QueueAndStackToArrayClassifications()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] QueueMethod(Queue<int> queue)
    {
        return queue.ToArray();
    }

    [EnforcePure]
    public int[] StackMethod(Stack<int> stack)
    {
        return stack.ToArray();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId).ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var trackedExpressions = new[]
            {
                "queue.ToArray()",
                "stack.ToArray()",
            };
            var resolutions = trackedExpressions.ToDictionary(
                expression => expression,
                expression =>
                {
                    var invocation = syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == expression);
                    var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!;
                    var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    var freshnessClassification = matched
                        ? (string)purityEntry.GetType().GetProperty("FreshnessClassification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification, freshnessClassification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(1));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));

            Assert.That(resolutions["queue.ToArray()"], Is.EqualTo((true, "pure", "fresh_array_candidate_via_local_helpers")));
            Assert.That(resolutions["stack.ToArray()"], Is.EqualTo((true, "impure", "fresh_array_candidate_requires_non_pure_resolution")));
        }

        [Test]
        public void GeneratedPurityCatalog_Resolves_ImmutableCollectionPureMembers()
        {
            const string source = @"
using System.Collections.Immutable;
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableList<int> CreateList()
    {
        return ImmutableList.Create<int>();
    }

    [EnforcePure]
    public ImmutableHashSet<int> CreateSet()
    {
        return ImmutableHashSet.Create<int>();
    }

    [EnforcePure]
    public int SetCount(ImmutableHashSet<int> set)
    {
        return set.Count;
    }

    [EnforcePure]
    public bool SetIsEmpty(ImmutableHashSet<int> set)
    {
        return set.IsEmpty;
    }

    [EnforcePure]
    public object? SetComparer(ImmutableHashSet<int> set)
    {
        return set.KeyComparer;
    }

    [EnforcePure]
    public ImmutableDictionary<int, string> CreateDictionary()
    {
        return ImmutableDictionary.Create<int, string>();
    }

    [EnforcePure]
    public ImmutableDictionary<int, string> CreateDictionaryRange(IEnumerable<KeyValuePair<int, string>> pairs)
    {
        return ImmutableDictionary.CreateRange(pairs);
    }

    [EnforcePure]
    public ImmutableHashSet<int> CreateSetRange(IEnumerable<int> values)
    {
        return ImmutableHashSet.CreateRange(values);
    }

    [EnforcePure]
    public ImmutableQueue<int> ClearQueue(ImmutableQueue<int> queue)
    {
        return queue.Clear();
    }

    [EnforcePure]
    public ImmutableStack<int> ClearStack(ImmutableStack<int> stack)
    {
        return stack.Clear();
    }

    [EnforcePure]
    public bool StackIsEmpty(ImmutableStack<int> stack)
    {
        return stack.IsEmpty;
    }

    [EnforcePure]
    public ImmutableStack<int> PushStack(ImmutableStack<int> stack, int value)
    {
        return stack.Push(value);
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;

            var trackedMembers = new (string Key, IMethodSymbol Symbol)[]
            {
                (
                    "ImmutableList.Create<int>()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "ImmutableList.Create<int>()"))
                        .Symbol!),
                (
                    "ImmutableHashSet.Create<int>()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "ImmutableHashSet.Create<int>()"))
                        .Symbol!),
                (
                    "ImmutableDictionary.Create<int, string>()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "ImmutableDictionary.Create<int, string>()"))
                        .Symbol!),
                (
                    "ImmutableDictionary.CreateRange(pairs)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "ImmutableDictionary.CreateRange(pairs)"))
                        .Symbol!),
                (
                    "ImmutableHashSet.CreateRange(values)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "ImmutableHashSet.CreateRange(values)"))
                        .Symbol!),
                (
                    "queue.Clear()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "queue.Clear()"))
                        .Symbol!),
                (
                    "stack.Clear()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "stack.Clear()"))
                        .Symbol!),
                (
                    "stack.IsEmpty",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "stack.IsEmpty"))
                        .Symbol!).GetMethod!),
                (
                    "stack.Push(value)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "stack.Push(value)"))
                        .Symbol!),
                (
                    "set.Count",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "set.Count"))
                        .Symbol!).GetMethod!),
                (
                    "set.IsEmpty",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "set.IsEmpty"))
                        .Symbol!).GetMethod!),
                (
                    "set.KeyComparer",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "set.KeyComparer"))
                        .Symbol!).GetMethod!),
            };

            var resolutions = trackedMembers.ToDictionary(
                trackedMember => trackedMember.Key,
                trackedMember =>
                {
                    var args = new object?[] { trackedMember.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var entry = args[2]!;
                    var classification = matched
                        ? (string)entry.GetType().GetProperty("Classification")!.GetValue(entry)!
                        : string.Empty;
                    return (matched, classification);
                },
                StringComparer.Ordinal);

            Assert.That(resolutions["ImmutableList.Create<int>()"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["ImmutableHashSet.Create<int>()"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["ImmutableDictionary.Create<int, string>()"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["ImmutableDictionary.CreateRange(pairs)"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["ImmutableHashSet.CreateRange(values)"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["queue.Clear()"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["stack.Clear()"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["stack.IsEmpty"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["stack.Push(value)"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["set.Count"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["set.IsEmpty"], Is.EqualTo((true, "pure")));
            Assert.That(resolutions["set.KeyComparer"], Is.EqualTo((true, "pure")));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_PriorityQueueMutatorsAsGeneratedImpureEvidence()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void EnqueueMethod(PriorityQueue<int, int> queue, int value, int priority)
    {
        queue.Enqueue(value, priority);
    }

    [EnforcePure]
    public int DequeueMethod(PriorityQueue<int, int> queue)
    {
        return queue.Dequeue();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId).ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var enqueue = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "queue.Enqueue(value, priority)"))
                .Symbol!;
            var dequeue = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "queue.Dequeue()"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                ("System.Collections.Generic.PriorityQueue<TElement, TPriority>.Enqueue(TElement, TPriority)", enqueue.OriginalDefinition),
                ("System.Collections.Generic.PriorityQueue<TElement, TPriority>.Dequeue()", dequeue.OriginalDefinition),
            };
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(2));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Generic.PriorityQueue<TElement, TPriority>.Enqueue(TElement, TPriority)"));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Generic.PriorityQueue<TElement, TPriority>.Dequeue()"));
            Assert.That(classifications["System.Collections.Generic.PriorityQueue<TElement, TPriority>.Enqueue(TElement, TPriority)"], Is.EqualTo((true, "impure")));
            Assert.That(classifications["System.Collections.Generic.PriorityQueue<TElement, TPriority>.Dequeue()"], Is.EqualTo((true, "impure")));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ConcurrentQueueMutatorsAsGeneratedImpureEvidence()
        {
            const string source = @"
using System.Collections.Concurrent;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void EnqueueMethod(ConcurrentQueue<int> queue, int value)
    {
        queue.Enqueue(value);
    }

    [EnforcePure]
    public bool TryDequeueMethod(ConcurrentQueue<int> queue)
    {
        return queue.TryDequeue(out _);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId).ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var enqueue = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "queue.Enqueue(value)"))
                .Symbol!;
            var tryDequeue = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "queue.TryDequeue(out _)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                ("System.Collections.Concurrent.ConcurrentQueue<T>.Enqueue(T)", enqueue.OriginalDefinition),
                ("System.Collections.Concurrent.ConcurrentQueue<T>.TryDequeue(out T)", tryDequeue.OriginalDefinition),
            };
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(2));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Concurrent.ConcurrentQueue<T>.Enqueue"));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Concurrent.ConcurrentQueue<T>.TryDequeue"));
            Assert.That(classifications["System.Collections.Concurrent.ConcurrentQueue<T>.Enqueue(T)"], Is.EqualTo((true, "impure")));
            Assert.That(classifications["System.Collections.Concurrent.ConcurrentQueue<T>.TryDequeue(out T)"], Is.EqualTo((true, "impure")));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_AdditionalConcurrentCollectionMutatorsAsGeneratedImpureEvidence()
        {
            const string source = @"
using System.Collections.Concurrent;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TryAddMethod(ConcurrentDictionary<int, int> dictionary)
    {
        return dictionary.TryAdd(1, 2);
    }

    [EnforcePure]
    public void BlockingAddMethod(BlockingCollection<int> blockingCollection)
    {
        blockingCollection.Add(1);
    }

    [EnforcePure]
    public int BlockingTakeMethod(BlockingCollection<int> blockingCollection)
    {
        return blockingCollection.Take();
    }

    [EnforcePure]
    public void BagAddMethod(ConcurrentBag<int> bag)
    {
        bag.Add(1);
    }

    [EnforcePure]
    public bool BagTryTakeMethod(ConcurrentBag<int> bag)
    {
        return bag.TryTake(out _);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId).ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var tryAdd = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "dictionary.TryAdd(1, 2)"))
                .Symbol!;
            var blockingAdd = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "blockingCollection.Add(1)"))
                .Symbol!;
            var blockingTake = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "blockingCollection.Take()"))
                .Symbol!;
            var bagAdd = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "bag.Add(1)"))
                .Symbol!;
            var bagTryTake = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "bag.TryTake(out _)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                ("System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>.TryAdd(TKey, TValue)", tryAdd.OriginalDefinition),
                ("System.Collections.Concurrent.BlockingCollection<T>.Add(T)", blockingAdd.OriginalDefinition),
                ("System.Collections.Concurrent.BlockingCollection<T>.Take()", blockingTake.OriginalDefinition),
                ("System.Collections.Concurrent.ConcurrentBag<T>.Add(T)", bagAdd.OriginalDefinition),
                ("System.Collections.Concurrent.ConcurrentBag<T>.TryTake(out T)", bagTryTake.OriginalDefinition),
            };
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(5));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>.TryAdd(TKey, TValue)"));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Concurrent.BlockingCollection<T>.Add(T)"));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Concurrent.BlockingCollection<T>.Take()"));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Concurrent.ConcurrentBag<T>.Add(T)"));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Concurrent.ConcurrentBag<T>.TryTake(out T)"));
            Assert.That(classifications["System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>.TryAdd(TKey, TValue)"], Is.EqualTo((true, "impure")));
            Assert.That(classifications["System.Collections.Concurrent.BlockingCollection<T>.Add(T)"], Is.EqualTo((true, "impure")));
            Assert.That(classifications["System.Collections.Concurrent.BlockingCollection<T>.Take()"], Is.EqualTo((true, "impure")));
            Assert.That(classifications["System.Collections.Concurrent.ConcurrentBag<T>.Add(T)"], Is.EqualTo((true, "impure")));
            Assert.That(classifications["System.Collections.Concurrent.ConcurrentBag<T>.TryTake(out T)"], Is.EqualTo((true, "impure")));
        }

        [Test]
        public async Task Ps0002_DiagnosticListenerConstructor_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DiagnosticListener TestMethod()
    {
        return new DiagnosticListener(""test"");
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.DiagnosticListener.DiagnosticListener(string)"));
        }

        [Test]
        public void GeneratedPurityCatalog_Resolves_DiagnosticsHelpersFromGeneratedEvidence()
        {
            const string source = @"
using System.Diagnostics;
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void AssertMethod()
    {
        Debug.Assert(true);
    }

    [EnforcePure]
    public DiagnosticListener ListenerMethod()
    {
        return new DiagnosticListener(""test"");
    }

    [EnforcePure]
    public string? FileVersionMethod(FileVersionInfo fileVersionInfo)
    {
        return fileVersionInfo.FileVersion;
    }

    [EnforcePure]
    public MethodBase? StackFrameMethod(StackFrame stackFrame)
    {
        return stackFrame.GetMethod();
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var assertMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "Debug.Assert(true)"))
                .Symbol!;
            var listenerCtor = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .Single(node => node.ToString() == "new DiagnosticListener(\"test\")"))
                .Symbol!;
            var fileVersionProperty = (IPropertySymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Single(node => node.ToString() == "fileVersionInfo.FileVersion"))
                .Symbol!;
            var stackFrameMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "stackFrame.GetMethod()"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var trackedMembers = new (string Label, ISymbol Symbol)[]
            {
                ("System.Diagnostics.Debug.Assert(bool)", assertMethod.OriginalDefinition),
                ("System.Diagnostics.DiagnosticListener.DiagnosticListener(string)", listenerCtor.OriginalDefinition),
                ("System.Diagnostics.FileVersionInfo.FileVersion.get", fileVersionProperty.GetMethod!.OriginalDefinition),
                ("System.Diagnostics.StackFrame.GetMethod()", stackFrameMethod.OriginalDefinition),
            };
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(classifications["System.Diagnostics.Debug.Assert(bool)"], Is.EqualTo((true, "pure")));
            Assert.That(classifications["System.Diagnostics.DiagnosticListener.DiagnosticListener(string)"], Is.EqualTo((true, "impure")));
            Assert.That(classifications["System.Diagnostics.FileVersionInfo.FileVersion.get"], Is.EqualTo((true, "pure")));
            Assert.That(classifications["System.Diagnostics.StackFrame.GetMethod()"], Is.EqualTo((true, "pure")));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_SortedCollectionAndBitArrayMutatorsAsGeneratedImpureEvidence()
        {
            const string source = @"
using System.Collections;
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void SortedDictionaryMethod(SortedDictionary<int, string> dictionary)
    {
        dictionary.Add(1, ""one"");
    }

    [EnforcePure]
    public void SortedSetMethod(SortedSet<int> set)
    {
        set.Add(1);
    }

    [EnforcePure]
    public void BitArrayMethod(BitArray bits)
    {
        bits.Set(0, true);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId).ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var sortedDictionaryAdd = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "dictionary.Add(1, \"one\")"))
                .Symbol!;
            var sortedSetAdd = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "set.Add(1)"))
                .Symbol!;
            var bitArraySet = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "bits.Set(0, true)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                ("System.Collections.Generic.SortedDictionary<TKey, TValue>.Add(TKey, TValue)", sortedDictionaryAdd.OriginalDefinition),
                ("System.Collections.Generic.SortedSet<T>.Add(T)", sortedSetAdd.OriginalDefinition),
                ("System.Collections.BitArray.Set(int, bool)", bitArraySet.OriginalDefinition),
            };
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(3));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Generic.SortedDictionary<TKey, TValue>.Add(TKey, TValue)"));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Generic.SortedSet<T>.Add(T)"));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.BitArray.Set(int, bool)"));
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.Add(TKey, TValue)"], Is.EqualTo((true, "impure")));
            Assert.That(classifications["System.Collections.Generic.SortedSet<T>.Add(T)"], Is.EqualTo((true, "impure")));
            Assert.That(classifications["System.Collections.BitArray.Set(int, bool)"], Is.EqualTo((true, "impure")));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayConvertAllAndComparerSortAsGeneratedImpureEvidence()
        {
            const string source = @"
using System;
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] ConvertAllMethod(int[] values)
    {
        return Array.ConvertAll(values, static value => value + 1);
    }

    [EnforcePure]
    public void SortMethod(int[] values, IComparer<int> comparer)
    {
        Array.Sort(values, comparer);
    }

    [EnforcePure]
    public void SortRangeMethod(int[] values, IComparer<int> comparer)
    {
        Array.Sort(values, 0, values.Length, comparer);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId).ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var convertAll = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "Array.ConvertAll(values, static value => value + 1)"))
                .Symbol!;
            var sort = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "Array.Sort(values, comparer)"))
                .Symbol!;
            var sortRange = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "Array.Sort(values, 0, values.Length, comparer)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                ("System.Array.ConvertAll<TInput, TOutput>(TInput[], System.Converter<TInput, TOutput>)", convertAll.OriginalDefinition),
                ("System.Array.Sort<T>(T[], System.Collections.Generic.IComparer<T>?)", sort.OriginalDefinition),
                ("System.Array.Sort<T>(T[], int, int, System.Collections.Generic.IComparer<T>?)", sortRange.OriginalDefinition),
            };
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(3));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Array.ConvertAll"));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Array.Sort"));
            Assert.That(classifications["System.Array.ConvertAll<TInput, TOutput>(TInput[], System.Converter<TInput, TOutput>)"], Is.EqualTo((true, "impure")));
            Assert.That(classifications["System.Array.Sort<T>(T[], System.Collections.Generic.IComparer<T>?)"], Is.EqualTo((true, "impure")));
            Assert.That(classifications["System.Array.Sort<T>(T[], int, int, System.Collections.Generic.IComparer<T>?)"], Is.EqualTo((true, "impure")));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_StaticCustomAttributeHelpers()
        {
            const string source = @"
using System;
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int PureHelpers(MemberInfo member, Type attributeType)
    {
        _ = Attribute.GetCustomAttribute(member, attributeType);
        _ = CustomAttributeData.GetCustomAttributes(member);
        return 0;
    }

    [EnforcePure]
    public object[] UnknownHelper(MemberInfo member)
    {
        return Attribute.GetCustomAttributes(member);
    }

    [EnforcePure]
    public bool ImpureHelper(MemberInfo member, Type attributeType)
    {
        return Attribute.IsDefined(member, attributeType);
    }
}";

            var additionalFiles = CreateSyntheticGeneratedPurityAdditionalFiles(
                typeof(Attribute).Assembly.Location,
                (
                    "Synthetic.StaticCustomAttribute.GetCustomAttribute.PurelySharp.EffectSummary.json",
                    "System.Attribute.GetCustomAttribute(System.Reflection.MemberInfo, System.Type)",
                    "System.Attribute.GetCustomAttribute(System.Reflection.MemberInfo, System.Type)",
                    "pure",
                    FormatJsonArray()),
                (
                    "Synthetic.StaticCustomAttribute.GetCustomAttributes.PurelySharp.EffectSummary.json",
                    "System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo)",
                    "System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo)",
                    "conservative_unknown",
                    FormatJsonArray("unknown_callee")),
                (
                    "Synthetic.StaticCustomAttribute.IsDefined.PurelySharp.EffectSummary.json",
                    "System.Attribute.IsDefined(System.Reflection.MemberInfo, System.Type)",
                    "System.Attribute.IsDefined(System.Reflection.MemberInfo, System.Type)",
                    "impure",
                    FormatJsonArray("impure_callee")),
                (
                    "Synthetic.StaticCustomAttribute.CustomAttributeData.GetCustomAttributes.PurelySharp.EffectSummary.json",
                    "System.Reflection.CustomAttributeData.GetCustomAttributes(System.Reflection.MemberInfo)",
                    "System.Reflection.CustomAttributeData.GetCustomAttributes(System.Reflection.MemberInfo)",
                    "pure",
                    FormatJsonArray()));

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source, additionalFiles: additionalFiles);
            var purityDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId).ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var getCustomAttribute = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "Attribute.GetCustomAttribute(member, attributeType)"))
                .Symbol!;
            var getCustomAttributes = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "Attribute.GetCustomAttributes(member)"))
                .Symbol!;
            var isDefined = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "Attribute.IsDefined(member, attributeType)"))
                .Symbol!;
            var getCustomAttributesData = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "CustomAttributeData.GetCustomAttributes(member)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateAnalyzerOptions(additionalFiles: additionalFiles), CancellationToken.None })!;
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                ("System.Attribute.GetCustomAttribute(System.Reflection.MemberInfo, System.Type)", getCustomAttribute.OriginalDefinition),
                ("System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo)", getCustomAttributes.OriginalDefinition),
                ("System.Attribute.IsDefined(System.Reflection.MemberInfo, System.Type)", isDefined.OriginalDefinition),
                ("System.Reflection.CustomAttributeData.GetCustomAttributes(System.Reflection.MemberInfo)", getCustomAttributesData.OriginalDefinition),
            };
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(2));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]).OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray(),
                Is.EqualTo(new[]
                {
                    "static System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo)",
                    "static System.Attribute.IsDefined(System.Reflection.MemberInfo, System.Type)"
                }));
            Assert.That(classifications["System.Attribute.GetCustomAttribute(System.Reflection.MemberInfo, System.Type)"], Is.EqualTo((true, "pure")));
            Assert.That(classifications["System.Attribute.GetCustomAttributes(System.Reflection.MemberInfo)"], Is.EqualTo((true, "conservative_unknown")));
            Assert.That(classifications["System.Attribute.IsDefined(System.Reflection.MemberInfo, System.Type)"], Is.EqualTo((true, "impure")));
            Assert.That(classifications["System.Reflection.CustomAttributeData.GetCustomAttributes(System.Reflection.MemberInfo)"], Is.EqualTo((true, "pure")));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EmailAddressAttributeConstructor()
        {
            const string source = @"
using System;
using System.ComponentModel.DataAnnotations;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var attribute = new EmailAddressAttribute();
        return attribute is null ? 0 : 1;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<ObjectCreationExpressionSyntax>()
                        .Single(node => node.ToString() == "new EmailAddressAttribute()"))
                    .Symbol!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow EmailAddressAttribute..ctor().");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve EmailAddressAttribute..ctor().");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DateTimeToFileTimeAndMemberwiseCloneAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class CloneableSample
{
    [EnforcePure]
    public object CloneSelf()
    {
        return MemberwiseClone();
    }
}

public class TestClass
{
    [EnforcePure]
    public long ToFileTimeMethod(DateTime value)
    {
        return value.ToFileTime();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "MemberwiseClone()" ||
                    node.ToString() == "value.ToFileTime()")
                .Select(node => (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var resolutions = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                var purityEntry = args[2]!;
                var classification = matched
                    ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                    : string.Empty;
                return (matched, classification);
            }).ToArray();
            var impuritySymbols = purityDiagnostics
                .Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty])
                .ToArray();

            Assert.That(purityDiagnostics, Has.Length.EqualTo(2));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "MethodInvocationPurityRule" }));
            Assert.That(impuritySymbols, Has.Some.Contain("System.DateTime.ToFileTime()"));
            Assert.That(impuritySymbols, Has.Some.Contain("MemberwiseClone"));
            Assert.That(resolutions.Select(result => result.matched).ToArray(), Is.EqualTo(new[] { true, true }),
                "Generated purity catalog should resolve DateTime.ToFileTime and MemberwiseClone.");
            Assert.That(resolutions.Select(result => result.classification).ToArray(), Is.EqualTo(new[] { "impure", "impure" }),
                "Generated purity catalog should classify DateTime.ToFileTime and MemberwiseClone as impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_CoreComponentModelAttributeConstructors()
        {
            const string source = @"
using System;
using System.ComponentModel;
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var browsable = new BrowsableAttribute(true);
        var description = new DescriptionAttribute(""sample"");
        var conditional = new ConditionalAttribute(""DEBUG"");
        return (browsable is null ? 0 : 1) + (description is null ? 0 : 1) + (conditional is null ? 0 : 1);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "new BrowsableAttribute(true)" ||
                    node.ToString() == "new DescriptionAttribute(\"sample\")" ||
                    node.ToString() == "new ConditionalAttribute(\"DEBUG\")")
                .Select(node => (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow these component-model attribute constructors.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve BrowsableAttribute, DescriptionAttribute, and ConditionalAttribute constructors.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_RegularExpressionAttributeConstructorAsImpureEvidence()
        {
            const string source = @"
using System;
using System.ComponentModel.DataAnnotations;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public RegularExpressionAttribute TestMethod()
    {
        return new RegularExpressionAttribute(""^[a-z]+$"");
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .Single(node => node.ToString() == "new RegularExpressionAttribute(\"^[a-z]+$\")"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(
                diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty],
                Does.Contain("System.ComponentModel.DataAnnotations.RegularExpressionAttribute.RegularExpressionAttribute(string)"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve RegularExpressionAttribute..ctor(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "RegularExpressionAttribute..ctor(string) now resolves through generated impure runtime evidence.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_CoreDataAnnotationsConstructorsAsImpureEvidence()
        {
            const string source = @"
using System;
using System.ComponentModel.DataAnnotations;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public RequiredAttribute RequiredMethod()
    {
        return new RequiredAttribute();
    }

    [EnforcePure]
    public StringLengthAttribute StringLengthMethod()
    {
        return new StringLengthAttribute(10);
    }

    [EnforcePure]
    public RangeAttribute RangeMethod()
    {
        return new RangeAttribute(0d, 1d);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "new RequiredAttribute()" ||
                    node.ToString() == "new StringLengthAttribute(10)" ||
                    node.ToString() == "new RangeAttribute(0d, 1d)")
                .ToDictionary(
                    node => node.ToString(),
                    node => (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!);
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var resolutions = trackedMethods.ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    var args = new object?[] { pair.Value.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });
            var impuritySymbols = purityDiagnostics
                .Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty])
                .ToArray();

            Assert.That(purityDiagnostics, Has.Length.EqualTo(3));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "ObjectCreationPurityRule" }));
            Assert.That(impuritySymbols, Has.Some.Contain("System.ComponentModel.DataAnnotations.RequiredAttribute.RequiredAttribute()"));
            Assert.That(impuritySymbols, Has.Some.Contain("System.ComponentModel.DataAnnotations.StringLengthAttribute.StringLengthAttribute(int)"));
            Assert.That(impuritySymbols, Has.Some.Contain("System.ComponentModel.DataAnnotations.RangeAttribute.RangeAttribute(double, double)"));
            Assert.That(resolutions["new RequiredAttribute()"].matched, Is.True,
                "Generated purity catalog should resolve RequiredAttribute..ctor().");
            Assert.That(resolutions["new RequiredAttribute()"].classification, Is.EqualTo("impure"),
                "RequiredAttribute..ctor() now resolves through generated impure runtime evidence.");
            Assert.That(resolutions["new StringLengthAttribute(10)"].matched, Is.True,
                "Generated purity catalog should resolve StringLengthAttribute..ctor(int).");
            Assert.That(resolutions["new StringLengthAttribute(10)"].classification, Is.EqualTo("impure"),
                "StringLengthAttribute..ctor(int) now resolves through generated impure runtime evidence.");
            Assert.That(resolutions["new RangeAttribute(0d, 1d)"].matched, Is.True,
                "Generated purity catalog should resolve RangeAttribute..ctor(double, double).");
            Assert.That(resolutions["new RangeAttribute(0d, 1d)"].classification, Is.EqualTo("impure"),
                "RangeAttribute..ctor(double, double) now resolves through generated impure runtime evidence.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DecimalNegate()
        {
            const string source = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(decimal value)
    {
        var negated = decimal.Negate(value);
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "decimal.Negate(value)"))
                    .Symbol!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve decimal.Negate.");
            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow decimal.Negate.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DecimalComparisonAndConversionsAsMixedEvidence()
        {
            const string source = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Compare(decimal left, decimal right)
    {
        return decimal.Compare(left, right);
    }

    [EnforcePure]
    public double Convert(decimal value)
    {
        return decimal.ToDouble(value);
    }

    [EnforcePure]
    public int Narrow(decimal value)
    {
        return decimal.ToInt32(value);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "decimal.Compare(left, right)" ||
                    node.ToString() == "decimal.ToDouble(value)" ||
                    node.ToString() == "decimal.ToInt32(value)")
                .Select(node => (invocation: node.ToString(), method: (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!))
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var resolutions = trackedMethods
                .Select(trackedMethod =>
                {
                    var args = new object?[] { trackedMethod.method.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2];
                    var classification = matched
                        ? (string)purityEntry!.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (trackedMethod.invocation, matched, classification);
                })
                .ToDictionary(
                    result => result.invocation,
                    result => (result.matched, result.classification),
                    StringComparer.Ordinal);

            Assert.That(purityDiagnostics, Has.Length.EqualTo(1));
            Assert.That(
                purityDiagnostics[0].Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty],
                Is.EqualTo("generated_purity_summary"));
            Assert.That(
                purityDiagnostics[0].Properties[PurelySharpDiagnostics.ImpuritySymbolProperty],
                Does.Contain("decimal.ToInt32(decimal)"));

            Assert.That(resolutions["decimal.Compare(left, right)"].matched, Is.True);
            Assert.That(resolutions["decimal.Compare(left, right)"].classification, Is.EqualTo("pure"));
            Assert.That(resolutions["decimal.ToDouble(value)"].matched, Is.True);
            Assert.That(resolutions["decimal.ToDouble(value)"].classification, Is.EqualTo("pure"));
            Assert.That(resolutions["decimal.ToInt32(value)"].matched, Is.True);
            Assert.That(resolutions["decimal.ToInt32(value)"].classification, Is.EqualTo("impure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayFindAndFindIndex()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int FindMethod(int[] values)
    {
        return Array.Find(values, static value => value > 0);
    }

    [EnforcePure]
    public int FindIndexMethod(int[] values)
    {
        return Array.FindIndex(values, static value => value > 0);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocationNodes = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "Array.Find(values, static value => value > 0)" ||
                    node.ToString() == "Array.FindIndex(values, static value => value > 0)")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = invocationNodes.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var method = (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { method.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Count(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.EqualTo(1),
                "Runtime-derived array summaries should keep Array.Find impure while allowing Array.FindIndex through generated purity evidence.");
            Assert.That(classifications["Array.Find(values, static value => value > 0)"].matched, Is.True,
                "Generated purity catalog should resolve Array.Find.");
            Assert.That(classifications["Array.Find(values, static value => value > 0)"].classification, Is.EqualTo("impure"),
                "Generated purity catalog should classify Array.Find as impure.");
            Assert.That(classifications["Array.FindIndex(values, static value => value > 0)"].matched, Is.True,
                "Generated purity catalog should resolve Array.FindIndex.");
            Assert.That(classifications["Array.FindIndex(values, static value => value > 0)"].classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify Array.FindIndex as pure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayExistsAndTrueForAll()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool ExistsMethod(int[] values)
    {
        return Array.Exists(values, static value => value > 0);
    }

    [EnforcePure]
    public bool TrueForAllMethod(int[] values)
    {
        return Array.TrueForAll(values, static value => value > 0);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocationNodes = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "Array.Exists(values, static value => value > 0)" ||
                    node.ToString() == "Array.TrueForAll(values, static value => value > 0)")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = invocationNodes.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var method = (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { method.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Generated array predicate summaries should allow Exists and TrueForAll without the manual pure catalog.");
            Assert.That(classifications["Array.Exists(values, static value => value > 0)"].matched, Is.True,
                "Generated purity catalog should resolve Array.Exists.");
            Assert.That(classifications["Array.Exists(values, static value => value > 0)"].classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify Array.Exists as pure.");
            Assert.That(classifications["Array.TrueForAll(values, static value => value > 0)"].matched, Is.True,
                "Generated purity catalog should resolve Array.TrueForAll.");
            Assert.That(classifications["Array.TrueForAll(values, static value => value > 0)"].classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify Array.TrueForAll as pure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayIndexOfAndLength()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Array values, object target)
    {
        return Array.IndexOf(values, target) + values.Length;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<SyntaxNode>()
                .Where(node =>
                    node is InvocationExpressionSyntax invocation && invocation.ToString() == "Array.IndexOf(values, target)" ||
                    node is MemberAccessExpressionSyntax memberAccess && memberAccess.ToString() == "values.Length")
                .Select(node => node switch
                {
                    InvocationExpressionSyntax => semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol,
                    MemberAccessExpressionSyntax => (semanticModel.GetSymbolInfo(node).Symbol as IPropertySymbol)?.GetMethod,
                    _ => null,
                })
                .Where(method => method is not null)
                .Select(method => method!)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(trackedMethods, Has.Length.EqualTo(2));
            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Array.IndexOf and Array.Length.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve Array.IndexOf and Array.Length.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayGetLengthAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Array values)
    {
        return values.GetLength(0);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "values.GetLength(0)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "Generated runtime summary should report Array.GetLength as potentially throwing.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Array.GetLength.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Generated purity catalog should classify Array.GetLength as impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayGetEnumerator()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Array values)
    {
        _ = values.GetEnumerator();
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "values.GetEnumerator()"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Array.GetEnumerator.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Array.GetEnumerator.");
            Assert.That(classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify Array.GetEnumerator as pure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Narrows_ArrayIEnumerableGetEnumeratorDispatch()
        {
            const string source = @"
using System.Collections;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int[] values)
    {
        _ = ((IEnumerable)values).GetEnumerator();
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Array receivers cast to IEnumerable should narrow back to the trusted Array.GetEnumerator runtime helper.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Narrows_ArrayGenericIEnumerableGetEnumeratorDispatch()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int[] values)
    {
        _ = ((IEnumerable<int>)values).GetEnumerator();
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Array receivers cast to IEnumerable<T> should narrow back to the trusted Array.GetEnumerator runtime helper.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ContractHelpers()
        {
            const string source = @"
using System.Diagnostics.Contracts;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(bool condition)
    {
        Contract.Requires(condition);
        Contract.Ensures(condition);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "Contract.Requires(condition)" ||
                    node.ToString() == "Contract.Ensures(condition)")
                .Select(node => semanticModel.GetSymbolInfo(node).Symbol)
                .OfType<IMethodSymbol>()
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(trackedMethods, Has.Length.EqualTo(2));
            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Contract.Requires and Contract.Ensures.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve Contract.Requires and Contract.Ensures.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayBinarySearch()
        {
            const string source = @"
using System;
using System.Collections;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Array values, object target, IComparer comparer)
    {
        return Array.BinarySearch(values, target, comparer);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "Array.BinarySearch(values, target, comparer)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "Runtime-derived array binary search summary should no longer allow Array.BinarySearch as a hard-coded pure helper.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Array.BinarySearch.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Generated purity catalog should classify Array.BinarySearch as impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_SortedSetGetViewBetween()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(SortedSet<int> values, int lower, int upper)
    {
        values.GetViewBetween(lower, upper);
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "values.GetViewBetween(lower, upper)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "Runtime-derived SortedSet summary should no longer allow GetViewBetween as a hard-coded pure helper.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve SortedSet<T>.GetViewBetween.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Generated purity catalog should classify SortedSet<T>.GetViewBetween as impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_SortedListAndLinkedListReadHelpers()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(SortedList<int, int> values, int key, LinkedListNode<int> node)
    {
        return values.IndexOfKey(key) + node.Value;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Collections.Generic.SortedList<TKey, TValue>.IndexOfKey(TKey)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.IndexOfKey(key)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.LinkedListNode<T>.Value.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "node.Value"))
                        .Symbol!).GetMethod!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow SortedList<TKey, TValue>.IndexOfKey and LinkedListNode<T>.Value.");
            Assert.That(classifications["System.Collections.Generic.SortedList<TKey, TValue>.IndexOfKey(TKey)"].matched, Is.True,
                "Generated purity catalog should resolve SortedList<TKey, TValue>.IndexOfKey.");
            Assert.That(classifications["System.Collections.Generic.SortedList<TKey, TValue>.IndexOfKey(TKey)"].classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify SortedList<TKey, TValue>.IndexOfKey as pure.");
            Assert.That(classifications["System.Collections.Generic.LinkedListNode<T>.Value.get"].matched, Is.True,
                "Generated purity catalog should resolve LinkedListNode<T>.Value.get.");
            Assert.That(classifications["System.Collections.Generic.LinkedListNode<T>.Value.get"].classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify LinkedListNode<T>.Value.get as pure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_LinkedListMutatorsAsGeneratedImpureEvidence()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void AddFirstMethod(LinkedList<int> list, int value)
    {
        list.AddFirst(value);
    }

    [EnforcePure]
    public void SetNodeValueMethod(LinkedListNode<int> node, int value)
    {
        node.Value = value;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics.Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId).ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var addFirst = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "list.AddFirst(value)"))
                .Symbol!;
            var assignment = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Single(node => node.ToString() == "node.Value = value");
            var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(assignment.Left).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                ("System.Collections.Generic.LinkedList<T>.AddFirst(T)", addFirst.OriginalDefinition),
                ("System.Collections.Generic.LinkedListNode<T>.Value.set", propertySymbol.SetMethod!.OriginalDefinition),
            };
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    var primaryCategory = (string)purityEntry.GetType().GetProperty("PrimaryCategory")!.GetValue(purityEntry)!;
                    return (matched, classification, primaryCategory);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(2));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Generic.LinkedList<T>.AddFirst(T)"));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]),
                Has.Some.Contain("System.Collections.Generic.LinkedListNode<T>.Value.set"));

            Assert.That(classifications["System.Collections.Generic.LinkedList<T>.AddFirst(T)"].matched, Is.True,
                "Generated purity catalog should resolve LinkedList<T>.AddFirst(T).");
            Assert.That(classifications["System.Collections.Generic.LinkedList<T>.AddFirst(T)"].classification, Is.EqualTo("impure"),
                "Generated purity catalog should classify LinkedList<T>.AddFirst(T) as impure.");
            Assert.That(classifications["System.Collections.Generic.LinkedList<T>.AddFirst(T)"].primaryCategory, Is.EqualTo("impure_callee"),
                "LinkedList<T>.AddFirst(T) should remain generated impure because it delegates to mutating helper paths.");
            Assert.That(classifications["System.Collections.Generic.LinkedListNode<T>.Value.set"].matched, Is.True,
                "Generated purity catalog should resolve LinkedListNode<T>.Value.set.");
            Assert.That(classifications["System.Collections.Generic.LinkedListNode<T>.Value.set"].classification, Is.EqualTo("impure"),
                "Generated purity catalog should classify LinkedListNode<T>.Value.set as impure.");
            Assert.That(classifications["System.Collections.Generic.LinkedListNode<T>.Value.set"].primaryCategory, Is.EqualTo("object_state_write"),
                "LinkedListNode<T>.Value.set should remain a caller-visible object-state write.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_KeyValuePairCtorAndAccessors()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass<TKey, TValue>
{
    [EnforcePure]
    public TValue TestMethod(KeyValuePair<TKey, TValue> pair, TKey key, TValue value)
    {
        var created = new KeyValuePair<TKey, TValue>(key, value);
        _ = pair.Key;
        return created.Value;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Collections.Generic.KeyValuePair<TKey, TValue>.KeyValuePair(TKey, TValue)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<ObjectCreationExpressionSyntax>()
                            .Single(node => node.ToString() == "new KeyValuePair<TKey, TValue>(key, value)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.KeyValuePair<TKey, TValue>.Key.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "pair.Key"))
                        .Symbol!).GetMethod!),
                (
                    "System.Collections.Generic.KeyValuePair<TKey, TValue>.Value.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "created.Value"))
                        .Symbol!).GetMethod!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow KeyValuePair<TKey, TValue> ctor and accessors.");
            foreach (var label in classifications.Keys)
            {
                Assert.That(classifications[label].matched, Is.True,
                    "Generated purity catalog should resolve " + label + ".");
                Assert.That(classifications[label].classification, Is.EqualTo("pure"),
                    "Generated purity catalog should classify " + label + " as pure.");
            }
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_SortedDictionaryLookupHelpers()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(SortedDictionary<int, string> values, int key, string target)
    {
        return values.ContainsKey(key) &&
            values.ContainsValue(target) &&
            values.TryGetValue(key, out var resolved) &&
            resolved == target;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Collections.Generic.SortedDictionary<TKey, TValue>.ContainsKey(TKey)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.ContainsKey(key)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.SortedDictionary<TKey, TValue>.ContainsValue(TValue)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.ContainsValue(target)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.SortedDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.TryGetValue(key, out var resolved)"))
                        .Symbol!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedMethods.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "SortedDictionary lookup helpers should stay semantically pure for builtin keys and values after removing the static pure catalog entries.");
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.ContainsKey(TKey)"].matched, Is.True,
                "Generated purity catalog should resolve SortedDictionary<TKey, TValue>.ContainsKey.");
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.ContainsKey(TKey)"].classification, Is.EqualTo("impure"),
                "Generated purity catalog should capture the runtime summary classification for SortedDictionary<TKey, TValue>.ContainsKey.");
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.ContainsValue(TValue)"].matched, Is.True,
                "Generated purity catalog should resolve SortedDictionary<TKey, TValue>.ContainsValue.");
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.ContainsValue(TValue)"].classification, Is.EqualTo("impure"),
                "Generated purity catalog should capture the runtime summary classification for SortedDictionary<TKey, TValue>.ContainsValue.");
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)"].matched, Is.True,
                "Generated purity catalog should resolve SortedDictionary<TKey, TValue>.TryGetValue.");
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)"].classification, Is.EqualTo("impure"),
                "Generated purity catalog should capture the runtime summary classification for SortedDictionary<TKey, TValue>.TryGetValue.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DictionaryAndSortedDictionaryViewGetters()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Dictionary<int, string>.KeyCollection DictionaryKeys(Dictionary<int, string> values)
    {
        return values.Keys;
    }

    [EnforcePure]
    public Dictionary<int, string>.ValueCollection DictionaryValues(Dictionary<int, string> values)
    {
        return values.Values;
    }

    [EnforcePure]
    public SortedDictionary<int, string>.KeyCollection SortedKeys(SortedDictionary<int, string> values)
    {
        return values.Keys;
    }

    [EnforcePure]
    public SortedDictionary<int, string>.ValueCollection SortedValues(SortedDictionary<int, string> values)
    {
        return values.Values;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, string MethodName, string ExpressionText)[]
            {
                (
                    "System.Collections.Generic.Dictionary<TKey, TValue>.Keys.get",
                    "DictionaryKeys",
                    "values.Keys"),
                (
                    "System.Collections.Generic.Dictionary<TKey, TValue>.Values.get",
                    "DictionaryValues",
                    "values.Values"),
                (
                    "System.Collections.Generic.SortedDictionary<TKey, TValue>.Keys.get",
                    "SortedKeys",
                    "values.Keys"),
                (
                    "System.Collections.Generic.SortedDictionary<TKey, TValue>.Values.get",
                    "SortedValues",
                    "values.Values"),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var memberAccess = syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node =>
                            node.ToString() == entry.ExpressionText &&
                            string.Equals(
                                node.Ancestors().OfType<MethodDeclarationSyntax>().First().Identifier.ValueText,
                                entry.MethodName,
                                StringComparison.Ordinal));
                    var symbol = ((IPropertySymbol)semanticModel.GetSymbolInfo(memberAccess).Symbol!).GetMethod!;
                    var args = new object?[] { symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Count(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.EqualTo(4),
                "Concrete Dictionary/SortedDictionary view getters should still report diagnostics after moving off the static impure catalog.");
            foreach (var label in classifications.Keys)
            {
                Assert.That(classifications[label].matched, Is.True,
                    "Generated purity catalog should resolve " + label + ".");
                Assert.That(classifications[label].classification, Is.EqualTo("impure"),
                    "Generated purity catalog should classify " + label + " as impure.");
            }
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_InterfaceCollectionLookupHelpers()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ICollection<int> collection, IList<int> list, int value)
    {
        return collection.Contains(value) && list.IndexOf(value) >= 0 && collection.Count >= 0;
    }
}";

            var additionalFiles = CreateSyntheticGeneratedPurityAdditionalFiles(
                typeof(ICollection<>).Assembly.Location,
                (
                    "Synthetic.InterfaceCollectionLookup.Contains.PurelySharp.EffectSummary.json",
                    "System.Collections.Generic.ICollection`1.Contains(!0)",
                    "System.Collections.Generic.ICollection<T>.Contains(T)",
                    "conservative_unknown",
                    FormatJsonArray("abstract", "metadata_only_or_external", "no_il_body")),
                (
                    "Synthetic.InterfaceCollectionLookup.IndexOf.PurelySharp.EffectSummary.json",
                    "System.Collections.Generic.IList`1.IndexOf(!0)",
                    "System.Collections.Generic.IList<T>.IndexOf(T)",
                    "conservative_unknown",
                    FormatJsonArray("abstract", "metadata_only_or_external", "no_il_body")),
                (
                    "Synthetic.InterfaceCollectionLookup.Count.PurelySharp.EffectSummary.json",
                    "System.Collections.Generic.ICollection`1.get_Count()",
                    "System.Collections.Generic.ICollection<T>.Count.get",
                    "conservative_unknown",
                    FormatJsonArray("abstract", "metadata_only_or_external", "no_il_body")));

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source, additionalFiles: additionalFiles);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, ISymbol Symbol)[]
            {
                (
                    "System.Collections.Generic.ICollection<T>.Contains(T)",
                    (ISymbol)(IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "collection.Contains(value)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.IList<T>.IndexOf(T)",
                    (ISymbol)(IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "list.IndexOf(value)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.ICollection<T>.Count.get",
                    (ISymbol)((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "collection.Count"))
                        .Symbol!).GetMethod!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateAnalyzerOptions(additionalFiles: additionalFiles), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var methodSymbol = (IMethodSymbol)entry.Symbol;
                    var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "Unknown ICollection<T> contract dispatch should become conservative once the static pure fallback is removed.");
            Assert.That(classifications["System.Collections.Generic.ICollection<T>.Contains(T)"].matched, Is.True,
                "Generated purity catalog should resolve ICollection<T>.Contains.");
            Assert.That(classifications["System.Collections.Generic.ICollection<T>.Contains(T)"].classification, Is.EqualTo("conservative_unknown"),
                "Generated purity catalog should classify ICollection<T>.Contains as conservative_unknown.");
            Assert.That(classifications["System.Collections.Generic.ICollection<T>.Count.get"].matched, Is.True,
                "Generated purity catalog should resolve ICollection<T>.Count.get.");
            Assert.That(classifications["System.Collections.Generic.ICollection<T>.Count.get"].classification, Is.EqualTo("conservative_unknown"),
                "Generated purity catalog should classify ICollection<T>.Count.get as conservative_unknown.");
            Assert.That(classifications["System.Collections.Generic.IList<T>.IndexOf(T)"].matched, Is.True,
                "Generated purity catalog should resolve IList<T>.IndexOf.");
            Assert.That(classifications["System.Collections.Generic.IList<T>.IndexOf(T)"].classification, Is.EqualTo("conservative_unknown"),
                "Generated purity catalog should classify IList<T>.IndexOf as conservative_unknown.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_InterfaceCollectionMutators()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(ICollection<int> collection, IList<int> list, int value)
    {
        collection.Add(value);
        _ = collection.Remove(value);
        collection.Clear();
        list.Insert(0, value);
        list.RemoveAt(0);
    }
}";

            var additionalFiles = CreateSyntheticGeneratedPurityAdditionalFiles(
                typeof(ICollection<>).Assembly.Location,
                (
                    "Synthetic.InterfaceCollectionMutators.Add.PurelySharp.EffectSummary.json",
                    "System.Collections.Generic.ICollection`1.Add(!0)",
                    "System.Collections.Generic.ICollection<T>.Add(T)",
                    "conservative_unknown",
                    FormatJsonArray("abstract", "metadata_only_or_external", "no_il_body")),
                (
                    "Synthetic.InterfaceCollectionMutators.Remove.PurelySharp.EffectSummary.json",
                    "System.Collections.Generic.ICollection`1.Remove(!0)",
                    "System.Collections.Generic.ICollection<T>.Remove(T)",
                    "conservative_unknown",
                    FormatJsonArray("abstract", "metadata_only_or_external", "no_il_body")),
                (
                    "Synthetic.InterfaceCollectionMutators.Clear.PurelySharp.EffectSummary.json",
                    "System.Collections.Generic.ICollection`1.Clear()",
                    "System.Collections.Generic.ICollection<T>.Clear()",
                    "conservative_unknown",
                    FormatJsonArray("abstract", "metadata_only_or_external", "no_il_body")),
                (
                    "Synthetic.InterfaceCollectionMutators.Insert.PurelySharp.EffectSummary.json",
                    "System.Collections.Generic.IList`1.Insert(int, !0)",
                    "System.Collections.Generic.IList<T>.Insert(int, T)",
                    "conservative_unknown",
                    FormatJsonArray("abstract", "metadata_only_or_external", "no_il_body")),
                (
                    "Synthetic.InterfaceCollectionMutators.RemoveAt.PurelySharp.EffectSummary.json",
                    "System.Collections.Generic.IList`1.RemoveAt(int)",
                    "System.Collections.Generic.IList<T>.RemoveAt(int)",
                    "conservative_unknown",
                    FormatJsonArray("abstract", "metadata_only_or_external", "no_il_body")));

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source, additionalFiles: additionalFiles);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Collections.Generic.ICollection<T>.Add(T)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "collection.Add(value)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.ICollection<T>.Remove(T)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "collection.Remove(value)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.ICollection<T>.Clear()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "collection.Clear()"))
                        .Symbol!),
                (
                    "System.Collections.Generic.IList<T>.Insert(int, T)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "list.Insert(0, value)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.IList<T>.RemoveAt(int)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "list.RemoveAt(0)"))
                        .Symbol!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateAnalyzerOptions(additionalFiles: additionalFiles), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "Unknown ICollection<T>/IList<T> mutator dispatch should remain conservative once the static impure fallback is removed.");
            foreach (var label in classifications.Keys)
            {
                Assert.That(classifications[label].matched, Is.True,
                    "Generated purity catalog should resolve " + label + ".");
                Assert.That(classifications[label].classification, Is.EqualTo("conservative_unknown"),
                    "Generated purity catalog should classify " + label + " as conservative_unknown.");
            }
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_InterfaceEnumeratorContracts()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(IEnumerable<int> values, IEnumerator<int> enumerator)
    {
        _ = values.GetEnumerator();
        return enumerator.Current;
    }
}";

            var additionalFiles = CreateSyntheticGeneratedPurityAdditionalFiles(
                typeof(IEnumerable<>).Assembly.Location,
                (
                    "Synthetic.InterfaceEnumeratorContracts.GetEnumerator.PurelySharp.EffectSummary.json",
                    "System.Collections.Generic.IEnumerable`1.GetEnumerator()",
                    "System.Collections.Generic.IEnumerable<T>.GetEnumerator()",
                    "conservative_unknown",
                    FormatJsonArray("abstract", "metadata_only_or_external", "no_il_body")),
                (
                    "Synthetic.InterfaceEnumeratorContracts.Current.PurelySharp.EffectSummary.json",
                    "System.Collections.Generic.IEnumerator`1.get_Current()",
                    "System.Collections.Generic.IEnumerator<T>.Current.get",
                    "conservative_unknown",
                    FormatJsonArray("abstract", "metadata_only_or_external", "no_il_body")));

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source, additionalFiles: additionalFiles);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Collections.Generic.IEnumerable<T>.GetEnumerator()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.GetEnumerator()"))
                        .Symbol!),
                (
                    "System.Collections.Generic.IEnumerator<T>.Current.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "enumerator.Current"))
                        .Symbol!).GetMethod!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateAnalyzerOptions(additionalFiles: additionalFiles), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "Unknown IEnumerable<T>/IEnumerator<T> contract dispatch should become conservative once the static pure fallback is removed.");
            Assert.That(classifications["System.Collections.Generic.IEnumerable<T>.GetEnumerator()"].matched, Is.True,
                "Generated purity catalog should resolve IEnumerable<T>.GetEnumerator.");
            Assert.That(classifications["System.Collections.Generic.IEnumerable<T>.GetEnumerator()"].classification, Is.EqualTo("conservative_unknown"),
                "Generated purity catalog should classify IEnumerable<T>.GetEnumerator as conservative_unknown.");
            Assert.That(classifications["System.Collections.Generic.IEnumerator<T>.Current.get"].matched, Is.True,
                "Generated purity catalog should resolve IEnumerator<T>.Current.get.");
            Assert.That(classifications["System.Collections.Generic.IEnumerator<T>.Current.get"].classification, Is.EqualTo("conservative_unknown"),
                "Generated purity catalog should classify IEnumerator<T>.Current.get as conservative_unknown.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_HashtableCompareInfoAndSortedListHelpersAsMixedEvidence()
        {
            const string source = @"
using System.Collections;
using System.Globalization;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool ContainsKey(Hashtable values, object key)
    {
        return values.ContainsKey(key);
    }

    [EnforcePure]
    public int Compare(CompareInfo compareInfo, string left, string right)
    {
        return compareInfo.Compare(left, right);
    }

    [EnforcePure]
    public object GetKey(SortedList values, int index)
    {
        return values.GetKey(index);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Collections.Hashtable.ContainsKey(object)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.ContainsKey(key)"))
                        .Symbol!),
                (
                    "System.Globalization.CompareInfo.Compare(string, string)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "compareInfo.Compare(left, right)"))
                        .Symbol!),
                (
                    "System.Collections.SortedList.GetKey(int)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.GetKey(index)"))
                        .Symbol!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var classifications = trackedMethods.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(2));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "unknown_external_call" }),
                "Open virtual dispatch on overridable Hashtable/SortedList members should remain conservative even though the base methods have reviewed generated summaries.");
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]).OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray(),
                Is.EqualTo(new[]
                {
                    "virtual System.Collections.Hashtable.ContainsKey(object)",
                    "virtual System.Collections.SortedList.GetKey(int)",
                }));
            Assert.That(
                purityDiagnostics.All(diagnostic => !diagnostic.Properties.ContainsKey(PurelySharpDiagnostics.ImpurityCatalogSourceProperty)),
                Is.True,
                "These diagnostics should come from conservative open virtual dispatch, not from directly trusting the base-method runtime summary at the call site.");

            Assert.That(classifications["System.Collections.Hashtable.ContainsKey(object)"].matched, Is.True);
            Assert.That(classifications["System.Collections.Hashtable.ContainsKey(object)"].classification, Is.EqualTo("impure"));
            Assert.That(classifications["System.Globalization.CompareInfo.Compare(string, string)"].matched, Is.True);
            Assert.That(classifications["System.Globalization.CompareInfo.Compare(string, string)"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["System.Collections.SortedList.GetKey(int)"].matched, Is.True);
            Assert.That(classifications["System.Collections.SortedList.GetKey(int)"].classification, Is.EqualTo("impure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_KeyedCollectionContains()
        {
            const string source = @"
using System.Collections.ObjectModel;
using PurelySharp.Attributes;

public sealed class NameCollection : KeyedCollection<string, string>
{
    protected override string GetKeyForItem(string item) => item;
}

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(NameCollection values, string key)
    {
        return values.Contains(key);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "values.Contains(key)");
            var methodSymbol = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "KeyedCollection<TKey, TItem>.Contains should no longer be globally trusted as pure when it dispatches through runtime hooks.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve KeyedCollection<TKey, TItem>.Contains(TKey).");
            Assert.That(classification, Is.EqualTo("conservative_unknown"),
                "Generated purity catalog should classify KeyedCollection<TKey, TItem>.Contains(TKey) as conservative_unknown.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_SortedDictionaryCount()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(SortedDictionary<int, string> values)
    {
        return values.Count;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "values.Count");
            var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(memberAccess).Symbol!;
            var getter = propertySymbol.GetMethod!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { getter.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow SortedDictionary<TKey, TValue>.Count.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve SortedDictionary<TKey, TValue>.Count.get.");
            Assert.That(classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify SortedDictionary<TKey, TValue>.Count.get as pure.");
        }

        [Test]
        public async Task Ps0002_ConservativeUnknownGeneratedPurity_SuppressesKnownPureConstructorFallback()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        ReadOnlySpan<char> value = ""alpha"".AsSpan();
        return new string(value);
    }
}";

            const string metadataSymbol = "System.String..ctor(System.ReadOnlySpan`1<char>)";
            const string displaySymbol = "string.String(System.ReadOnlySpan<char>)";
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                source,
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new InMemoryAdditionalText(
                        "Synthetic.StringConstructor.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            typeof(string).Assembly.Location,
                            metadataSymbol,
                            "conservative_unknown",
                            "[\"dynamic_dispatch\"]",
                            displaySymbol))));

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties.ContainsKey(PurelySharpDiagnostics.ImpurityCategoryProperty), Is.True);
        }

        [Test]
        public async Task Ps0002_AppContextSetSwitch_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        AppContext.SetSwitch(""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"", true);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.AppContext.SetSwitch"));
        }

        [Test]
        public async Task Ps0002_ListFindLast_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(List<int> values)
    {
        return values.FindLast(static value => value > 0);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("caller_visible_memory_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.List<T>.FindLast(System.Predicate<T>)"));
        }

        [Test]
        public async Task Ps0002_QueueTryPeek_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Queue<int> values)
    {
        return values.TryPeek(out var value);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("caller_visible_memory_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.Queue<T>.TryPeek(out T)"));
        }

        public async Task Ps0002_DictionaryClear_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Dictionary<int, int> values)
    {
        values.Clear();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.Dictionary<TKey, TValue>.Clear()"));
        }

        [Test]
        public async Task Ps0002_DictionaryAdd_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Dictionary<int, int> values)
    {
        values.Add(1, 2);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.Dictionary<TKey, TValue>.Add(TKey, TValue)"));
        }

        [Test]
        public async Task Ps0002_DictionaryRemove_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Dictionary<int, int> values, int key)
    {
        return values.Remove(key);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.Dictionary<TKey, TValue>.Remove(TKey)"));
        }

        [Test]
        public async Task Ps0002_DictionaryTryAdd_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Dictionary<int, int> values)
    {
        return values.TryAdd(1, 2);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.Dictionary<TKey, TValue>.TryAdd(TKey, TValue)"));
        }

        [Test]
        public async Task Ps0002_ArrayFind_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int[] values)
    {
        return Array.Find(values, static value => value > 0);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("caller_visible_memory_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.Find"));
        }

        [Test]
        public async Task Ps0002_ArrayBinarySearch_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.Collections;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Array values, object target, IComparer comparer)
    {
        return Array.BinarySearch(values, target, comparer);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.BinarySearch"));
        }

        [Test]
        public async Task Ps0002_ArrayCopyRange_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] source, int[] destination)
    {
        Array.Copy(source, 0, destination, 0, source.Length);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.Copy(System.Array, int, System.Array, int, int)"));
        }

        [Test]
        public async Task Ps0002_ArrayCopyTo_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] source, int[] destination)
    {
        source.CopyTo(destination, 0);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.CopyTo(System.Array, int)"));
        }

        [Test]
        public async Task Ps0002_ArrayCopyLengthOverload_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Array source, Array destination, int length)
    {
        Array.Copy(source, destination, length);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.Copy(System.Array, System.Array, int)"));
        }

        [Test]
        public async Task Ps0002_ArrayConstrainedCopy_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Array source, Array destination, int length)
    {
        Array.ConstrainedCopy(source, 0, destination, 0, length);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.ConstrainedCopy(System.Array, int, System.Array, int, int)"));
        }

        [Test]
        public async Task Ps0002_ArrayClearFullArray_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        Array.Clear(values);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.Clear(System.Array)"));
        }

        [Test]
        public async Task Ps0002_ArrayClearRange_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        Array.Clear(values, 0, values.Length);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.Clear(System.Array, int, int)"));
        }

        [Test]
        public async Task Ps0002_ArrayFill_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        Array.Fill(values, 42);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.Fill"));
        }

        [Test]
        public async Task Ps0002_ArrayResize_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        var values = new[] { 1, 2 };
        Array.Resize(ref values, values.Length + 1);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("caller_visible_memory_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.Resize"));
        }

        [Test]
        public async Task Ps0002_BufferBlockCopy_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] source, int[] destination)
    {
        Buffer.BlockCopy(source, 0, destination, 0, source.Length * sizeof(int));
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Buffer.BlockCopy(System.Array, int, System.Array, int, int)"));
        }

        [Test]
        public async Task Ps0002_SortedSetGetViewBetween_NoLongerUsesManualPureFallback()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(SortedSet<int> values, int lower, int upper)
    {
        values.GetViewBetween(lower, upper);
        return 0;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.SortedSet<int>.GetViewBetween"));
        }

        [Test]
        public async Task Ps0002_AppDomainCurrentDomain_IsNowPureInGeneratedPurityCatalog()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public AppDomain TestMethod()
    {
        return AppDomain.CurrentDomain;
    }
}");

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "AppDomain.CurrentDomain now resolves as pure from runtime-generated summaries and should not raise PS0002.");
        }

        [Test]
        public async Task Ps0002_AppDomainBaseDirectoryOnParameter_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(AppDomain domain)
    {
        return domain.BaseDirectory;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.AppDomain.BaseDirectory.get"));
        }

        [Test]
        public async Task Ps0002_AppDomainFriendlyNameOnParameter_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(AppDomain domain)
    {
        return domain.FriendlyName;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.AppDomain.FriendlyName.get"));
        }

        [Test]
        public async Task Ps0002_StopwatchGetTimestamp_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod()
    {
        return Stopwatch.GetTimestamp();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Stopwatch.GetTimestamp"));
        }

        [Test]
        public async Task Ps0002_FrameworkNameConstructor_UsesGeneratedPurityCatalogSource()
        {
            var additionalFiles = CreateSyntheticGeneratedPurityAdditionalFiles(
                typeof(System.Runtime.Versioning.FrameworkName).Assembly.Location,
                (
                    "Synthetic.FrameworkName.Constructor.PurelySharp.EffectSummary.json",
                    "System.Runtime.Versioning.FrameworkName..ctor(string)",
                    "System.Runtime.Versioning.FrameworkName..ctor(string)",
                    "impure",
                    FormatJsonArray("object_state_write", "throw")));

            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Runtime.Versioning;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public FrameworkName TestMethod()
    {
        return new FrameworkName("".NETCoreApp,Version=v8.0"");
    }
}", additionalFiles: additionalFiles);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("object_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Runtime.Versioning.FrameworkName"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_StopwatchConstructorAsPureEvidence()
        {
            const string source = @"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Stopwatch TestMethod()
    {
        return new Stopwatch();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var objectCreation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(node => node.ToString() == "new Stopwatch()");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(objectCreation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow new Stopwatch() when the constructor only initializes fresh-owned state.");
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Stopwatch..ctor().");
            Assert.That(classification, Is.EqualTo("pure"));
        }

        [Test]
        public async Task Ps0002_StopwatchElapsedTicksOnParameter_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(Stopwatch stopwatch)
    {
        return stopwatch.ElapsedTicks;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Stopwatch.ElapsedTicks.get"));
        }

        [Test]
        public async Task Ps0002_StopwatchStartOnParameter_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Stopwatch stopwatch)
    {
        stopwatch.Start();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Stopwatch.Start"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_StopwatchStaticFieldFromStaticConstructorEvidence()
        {
            const string source = @"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod()
    {
        return Stopwatch.Frequency;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Stopwatch.Frequency");
            var fieldSymbol = (IFieldSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetFieldPurity = catalogType.GetMethod("TryGetFieldPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateGeneratedPurityAnalyzerOptions(), CancellationToken.None })!;
            var args = new object?[] { fieldSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetFieldPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("FieldReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Stopwatch static fields from the runtime static constructor.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Stopwatch static fields should derive impurity from the runtime static constructor.");
        }

        [Test]
        public async Task Ps0002_StopwatchFrequency_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod()
    {
        return Stopwatch.Frequency;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("FieldReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Stopwatch"));
        }

        [Test]
        public async Task Ps0002_StopwatchIsHighResolution_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return Stopwatch.IsHighResolution;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("FieldReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Stopwatch"));
        }

        [Test]
        public async Task Ps0002_TimeProviderSystem_IsNowPureInGeneratedPurityCatalog()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeProvider TestMethod()
    {
        return TimeProvider.System;
    }
}");

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "TimeProvider.System now resolves as pure from runtime-generated summaries and should not raise PS0002.");
        }

        [Test]
        public async Task Ps0002_TimeProviderLocalTimeZoneOnParameter_UsesDynamicDispatchEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeZoneInfo TestMethod(TimeProvider provider)
    {
        return provider.LocalTimeZone;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("dynamic_dispatch"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties.ContainsKey(PurelySharpDiagnostics.ImpurityCatalogSourceProperty), Is.False);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.TimeProvider.LocalTimeZone.get"));
        }

        [Test]
        public async Task Ps0002_TimeZoneInfoFindSystemTimeZoneById_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeZoneInfo TestMethod()
    {
        return TimeZoneInfo.FindSystemTimeZoneById(""UTC"");
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.TimeZoneInfo.FindSystemTimeZoneById"));
        }

        [Test]
        public async Task Ps0002_TimeZoneInfoConvertTimeDateTimeOffset_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset TestMethod(DateTimeOffset value, TimeZoneInfo timeZone)
    {
        return TimeZoneInfo.ConvertTime(value, timeZone);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.TimeZoneInfo.ConvertTime(System.DateTimeOffset, System.TimeZoneInfo)"));
        }

        [Test]
        public async Task Ps0002_GuidNewGuid_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Guid TestMethod()
    {
        return Guid.NewGuid();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Guid.NewGuid"));
        }

        [Test]
        public async Task Ps0002_PathGetFullPath_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        return Path.GetFullPath(value);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.Path.GetFullPath"));
        }

        [Test]
        public async Task Ps0002_PathGetTempPath_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Path.GetTempPath();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.Path.GetTempPath"));
        }

        [Test]
        public async Task Ps0002_ConfigurationManagerAppSettings_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
using System.Configuration;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? TestMethod()
    {
        return ConfigurationManager.AppSettings[""MyKey""];
    }
}",
                additionalMetadataReferences: ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(ConfigurationManager).Assembly.Location)));

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Configuration.ConfigurationManager.AppSettings"));
        }

        [Test]
        public async Task Ps0002_ConfigurationManagerConnectionStrings_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                @"
using System.Configuration;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ConnectionStringSettingsCollection TestMethod()
    {
        return ConfigurationManager.ConnectionStrings;
    }
}",
                additionalMetadataReferences: ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(ConfigurationManager).Assembly.Location)));

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Configuration.ConfigurationManager.ConnectionStrings"));
        }

        [Test]
        public async Task Ps0002_MonitorExit_UsesThreadingSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(object gate)
    {
        Monitor.Exit(gate);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("synchronization"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("threading_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Threading.Monitor.Exit"));
        }

        [Test]
        public async Task Ps0002_TimerStart_UsesTypeFallbackAfterMemberCatalogRemoval()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Timers;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Timer timer)
    {
        timer.Start();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("known_impure_namespace_or_type"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Timers.Timer.Start"));
        }

        [Test]
        public async Task Ps0002_TimerStop_UsesTypeFallbackAfterMemberCatalogRemoval()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Timers;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Timer timer)
    {
        timer.Stop();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("known_impure_namespace_or_type"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Timers.Timer.Stop"));
        }

        [Test]
        public async Task Ps0002_SafeHandleDispose_UsesNamespaceFallbackAfterMemberCatalogRemoval()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.Runtime.InteropServices;
using PurelySharp.Attributes;

sealed class TestHandle : SafeHandle
{
    public TestHandle() : base(IntPtr.Zero, true)
    {
    }

    public override bool IsInvalid => false;

    protected override bool ReleaseHandle()
    {
        return true;
    }
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod(TestHandle handle)
    {
        handle.Dispose();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("known_impure_namespace_or_type"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Runtime.InteropServices.SafeHandle.Dispose"));
        }

        [Test]
        public async Task Ps0002_AssemblyNameGetAssemblyName_UsesNamespaceFallbackAfterMemberCatalogRemoval()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Reflection;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public AssemblyName TestMethod(string path)
    {
        return AssemblyName.GetAssemblyName(path);
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("reflection_environment_source"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("known_impure_namespace_or_type"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Reflection.AssemblyName.GetAssemblyName"));
        }

        public async Task Ps0002_ImmutableQueueDequeue_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Immutable;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableQueue<int> TestMethod(ImmutableQueue<int> queue)
    {
        return queue.Dequeue();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Immutable.ImmutableQueue"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("Dequeue"));
        }

        [Test]
        public async Task Ps0002_ImmutableHashSetCreateRangeWithComparer_IsPure()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using System.Collections.Immutable;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableHashSet<int> TestMethod(IEnumerable<int> values, IEqualityComparer<int> comparer)
    {
        return ImmutableHashSet.CreateRange(comparer, values);
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_TypeEqualsType_IsPure()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Type left, Type right)
    {
        return left.Equals(right);
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_TypeEqualsObject_IsPure()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Type left, object right)
    {
        return left.Equals(right);
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_TypeGetHashCode_IsPure()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Type value)
    {
        return value.GetHashCode();
    }
}");

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_AddingNewEventArgsConstructorAndStaticObjectEqualsAsPureEvidence()
        {
            const string source = @"
using System;
using System.ComponentModel;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(object left, object right)
    {
        _ = new AddingNewEventArgs();
        return object.Equals(left, right) ? 1 : 0;
    }
}";

            var additionalFiles = CreateSyntheticGeneratedPurityAdditionalFiles(
                typeof(System.ComponentModel.AddingNewEventArgs).Assembly.Location,
                (
                    "Synthetic.ComponentModel.AddingNewEventArgs.Constructor.PurelySharp.EffectSummary.json",
                    "System.ComponentModel.AddingNewEventArgs..ctor()",
                    "System.ComponentModel.AddingNewEventArgs..ctor()",
                    "pure",
                    FormatJsonArray("fresh_owned_object_write", "internal_only")))
                .Concat(CreateSyntheticGeneratedPurityAdditionalFiles(
                    typeof(object).Assembly.Location,
                    (
                        "Synthetic.Object.Equals.Static.PurelySharp.EffectSummary.json",
                        "System.Object.Equals(object, object)",
                        "System.Object.Equals(object, object)",
                        "pure",
                        FormatJsonArray())))
                .ToImmutableArray();

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source, additionalFiles: additionalFiles);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<ObjectCreationExpressionSyntax>()
                        .Single(node => node.ToString() == "new AddingNewEventArgs()"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "object.Equals(left, right)"))
                    .Symbol!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { CreateAnalyzerOptions(additionalFiles: additionalFiles), CancellationToken.None })!;
            var resolutions = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                var purityEntry = args[2];
                var classification = matched
                    ? (string)purityEntry!.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                    : string.Empty;
                return (matched, classification);
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Generated purity summary evidence should allow AddingNewEventArgs() and object.Equals(object, object).");
            Assert.That(resolutions.Select(result => result.matched).ToArray(), Is.EqualTo(new[] { true, true }),
                "Generated purity catalog should resolve AddingNewEventArgs() and object.Equals(object, object) from synthetic generated evidence.");
            Assert.That(resolutions.Select(result => result.classification).ToArray(), Is.EqualTo(new[] { "pure", "pure" }),
                "Generated purity catalog should classify AddingNewEventArgs() and object.Equals(object, object) as pure.");
        }

        [Test]
        public async Task Ps0002_StringReplaceChar_IsPureFromGeneratedPuritySummary()
        {
            var additionalFiles = CreateSyntheticGeneratedPurityAdditionalFiles(
                typeof(string).Assembly.Location,
                (
                    "Synthetic.String.Replace.Char.PurelySharp.EffectSummary.json",
                    "System.String.Replace(char, char)",
                    "System.String.Replace(char, char)",
                    "pure",
                    "[]"));

            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string input)
    {
        return input.Replace('a', 'b');
    }
}",
                additionalFiles: additionalFiles);

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_StringTrimLastIndexEnumeratorAndSpanViewHelpers_ArePureFromGeneratedPuritySummary()
        {
            var additionalFiles = CreateSyntheticGeneratedPurityAdditionalFiles(
                typeof(string).Assembly.Location,
                (
                    "Synthetic.String.Trim.Char.PurelySharp.EffectSummary.json",
                    "System.String.Trim(char)",
                    "System.String.Trim(char)",
                    "pure",
                    "[]"),
                (
                    "Synthetic.String.TrimStart.Char.PurelySharp.EffectSummary.json",
                    "System.String.TrimStart(char)",
                    "System.String.TrimStart(char)",
                    "pure",
                    "[]"),
                (
                    "Synthetic.String.TrimEnd.Char.PurelySharp.EffectSummary.json",
                    "System.String.TrimEnd(char)",
                    "System.String.TrimEnd(char)",
                    "pure",
                    "[]"),
                (
                    "Synthetic.String.LastIndexOf.Char.PurelySharp.EffectSummary.json",
                    "System.String.LastIndexOf(char)",
                    "System.String.LastIndexOf(char)",
                    "pure",
                    "[]"),
                (
                    "Synthetic.String.GetEnumerator.PurelySharp.EffectSummary.json",
                    "System.String.GetEnumerator()",
                    "System.String.GetEnumerator()",
                    "pure",
                    "[]"),
                (
                    "Synthetic.String.ReadOnlySpanView.PurelySharp.EffectSummary.json",
                    "System.String.op_Implicit(string)",
                    "System.String.op_Implicit(string)",
                    "pure",
                    "[]"));

            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string input)
    {
        ReadOnlySpan<char> span = input;
        _ = input.GetEnumerator();
        var trimmed = input.Trim('x').TrimStart('y').TrimEnd('z');
        return span.Length + trimmed.LastIndexOf('a');
    }
}",
                additionalFiles: additionalFiles);

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Ps0002_ImmutableStackPop_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Immutable;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableStack<int> TestMethod(ImmutableStack<int> stack)
    {
        return stack.Pop();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Immutable.ImmutableStack"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("Pop"));
        }

        [Test]
        public async Task Ps0002_HashSetAdd_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(HashSet<int> values)
    {
        values.Add(1);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.HashSet"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("Add"));
        }

        [Test]
        public void InvariantCultureDeterministicParseHelper_Recognizes_TimeSpanSpanParseExact()
        {
            const string source = @"
#nullable enable
using System;
using System.Globalization;

public static class TestClass
{
    public static TimeSpan TestMethod(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeSpan.ParseExact(span, ""c"", CultureInfo.InvariantCulture, TimeSpanStyles.None);
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "DeterministicParseProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString().Contains("TimeSpan.ParseExact", StringComparison.Ordinal));
            var operation = (IInvocationOperation)semanticModel.GetOperation(invocation)!;
            var engineType = typeof(PurelySharpAnalyzer).Assembly.GetType(
                "PurelySharp.Analyzer.Engine.PurityAnalysisEngine",
                throwOnError: true)!;
            var helper = engineType.GetMethod(
                "IsInvariantCultureDeterministicParseInvocation",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var matched = (bool)helper.Invoke(null, new object[] { operation })!;

            Assert.That(matched, Is.True, "The deterministic parse helper should recognize span ParseExact with InvariantCulture and None styles.");
        }

        [Test]
        public async Task Ps0002_CurrentCultureNumericParse_UsesSemanticCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public double TestMethod(string value)
    {
        return double.Parse(value);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("current_culture_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("double.Parse"));
        }

        public async Task Ps0002_CurrentCultureNumericFormat_UsesSemanticCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(double value)
    {
        return value.ToString(""N"");
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("current_culture_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("double.ToString"));
        }

        [Test]
        public async Task Ps0002_CurrentCultureDateParse_UsesSemanticCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly TestMethod(string value)
    {
        return DateOnly.ParseExact(value, ""d"");
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("current_culture_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.DateOnly.ParseExact"));
        }

        [Test]
        public async Task Ps0002_DateTimeOffsetParse_WithInvariantCulture_UsesSemanticCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
#nullable enable
using System;
using System.Globalization;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateTimeOffset TestMethod(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("current_culture_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.DateTimeOffset.Parse"));
        }

        [Test]
        public async Task Ps0002_DateTimeTryParseExact_WithInvariantCulture_UsesSemanticCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
#nullable enable
using System;
using System.Globalization;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string value)
    {
        return DateTime.TryParseExact(value, ""O"", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("current_culture_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.DateTime.TryParseExact"));
        }

        [Test]
        public async Task Ps0002_DateTimeOffsetTryParseExact_WithInvariantCulture_UsesSemanticCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
#nullable enable
using System;
using System.Globalization;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string value)
    {
        return DateTimeOffset.TryParseExact(value, ""O"", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("current_culture_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.DateTimeOffset.TryParseExact"));
        }

        [Test]
        public async Task Ps0002_CurrentCultureDateFormat_UsesSemanticCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(DateTime value)
    {
        return value.ToLongDateString();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("current_culture_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.DateTime.ToLongDateString"));
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownPureGenericMethodOverridesConfiguredImpureType()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] TestMethod()
    {
        return Array.Empty<int>();
    }
}",
                ImmutableDictionary<string, string>.Empty
                    .Add("purelysharp_known_impure_types", "System.Array")
                    .Add("purelysharp_known_pure_methods", "System.Array.Empty<T>()"));

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Configured pure generic method should override a configured impure type for the same member.");
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownPureGenericValueTypePropertyOverridesConfiguredImpureType()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(KeyValuePair<int, int> pair)
    {
        return pair.Key;
    }
}",
                ImmutableDictionary<string, string>.Empty
                    .Add("purelysharp_known_impure_types", "System.Collections.Generic.KeyValuePair<TKey, TValue>")
                    .Add("purelysharp_known_pure_methods", "System.Collections.Generic.KeyValuePair<TKey, TValue>.Key.get"));

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Configured pure generic value-type property should override a configured impure type for the same member.");
        }

        [Test]
        public async Task Ps0002_ImpureCallee_IncludesCalleeChain()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Caller()
    {
        Callee();
    }

    [EnforcePure]
    public void Callee()
    {
        Console.WriteLine(""impure"");
    }
}");

            var callerDiagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'Caller'", StringComparison.Ordinal));

            Assert.That(callerDiagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(callerDiagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("TestClass.Callee"));
            Assert.That(callerDiagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_UnresolvedDelegateTarget_IncludesDistinctCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Action action)
    {
        action();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unresolved_delegate_target"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Action.Invoke"));
        }

        [Test]
        public async Task Ps0002_DynamicDispatch_IncludesDistinctCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(dynamic value)
    {
        return value.ToString();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("dynamic_dispatch"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("DynamicInvocation"));
        }

        [Test]
        public async Task Ps0002_DynamicBinaryOperation_IncludesDynamicDispatchCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(dynamic value)
    {
        return value + 1;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("dynamic_dispatch"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("BinaryOperationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("Binary"));
        }

        [Test]
        public async Task Ps0002_DynamicUnaryOperation_IncludesDynamicDispatchCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(dynamic value)
    {
        return -value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("dynamic_dispatch"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("UnaryOperationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("Unary"));
        }

        [Test]
        public async Task Ps0002_ClosedWorldInterfaceDispatchWithoutImplementation_UsesDynamicDispatchEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

internal interface IWorker
{
    int Compute(int value);
}

public class WorkerHost
{
    [EnforcePure]
    internal int ComputeWithUnknownImplementation(IWorker worker, int value)
    {
        return worker.Compute(value);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("dynamic_dispatch"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties.ContainsKey(PurelySharpDiagnostics.ImpurityCatalogSourceProperty), Is.False);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("IWorker.Compute"));
        }

        [Test]
        public async Task Ps0002_SourceExternCall_IncludesUnknownExternalCallCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Runtime.InteropServices;
using PurelySharp.Attributes;

public static class NativeMethods
{
    [DllImport(""native.dll"")]
    public static extern int ReadValue();
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return NativeMethods.ReadValue();
    }
}");

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unknown_external_call"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("NativeMethods.ReadValue"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("extern"));
        }

        [Test]
        public async Task Ps0002_MetadataBclFallback_ProbablyPureShape_IncludesGuessEvidence()
        {
            using var fixture = CreateMetadataOnlyAssemblyFixture(
                "System.FallbackSdk",
                BclFallbackFixtureSource);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        return System.Experimental.NumericFacts.Normalize(value);
    }
}",
                globalOptions: ImmutableDictionary<string, string>.Empty.Add("purelysharp_emit_explanations", "true"),
                additionalMetadataReferences: ImmutableArray.Create(fixture.Reference));

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("bcl_fallback_probably_pure"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("bcl_heuristic_fallback"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.BclFallbackGuessProperty], Is.EqualTo("probably_pure"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.BclFallbackConfidenceProperty], Is.EqualTo("low"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.BclFallbackReasonProperty], Is.EqualTo("value_return_no_ref_or_out"));

            var fallbackDiagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.BclFallbackGuessId);
            Assert.That(fallbackDiagnostic.GetMessage(), Does.Contain("probably_pure"));
        }

        [Test]
        public async Task Ps0002_MetadataBclFallback_ProbablyImpureShape_IncludesGuessEvidence()
        {
            using var fixture = CreateMetadataOnlyAssemblyFixture(
                "System.FallbackSdk",
                BclFallbackFixtureSource);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(int value)
    {
        System.Experimental.MutatingSink.WriteMetric(value);
    }
}",
                globalOptions: ImmutableDictionary<string, string>.Empty.Add("purelysharp_emit_explanations", "true"),
                additionalMetadataReferences: ImmutableArray.Create(fixture.Reference));

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("bcl_fallback_probably_impure"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("bcl_heuristic_fallback"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.BclFallbackGuessProperty], Is.EqualTo("probably_impure"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.BclFallbackReasonProperty], Is.EqualTo("void_returning_metadata_method"));

            var fallbackDiagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.BclFallbackGuessId);
            Assert.That(fallbackDiagnostic.GetMessage(), Does.Contain("probably_impure"));
        }

        [Test]
        public async Task Ps0002_MetadataBclFallback_AmbiguousShape_IncludesUnknownGuessEvidence()
        {
            using var fixture = CreateMetadataOnlyAssemblyFixture(
                "System.FallbackSdk",
                BclFallbackFixtureSource);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public System.Experimental.StatefulBox TestMethod(System.Experimental.StatefulBox box)
    {
        return box.Next();
    }
}",
                globalOptions: ImmutableDictionary<string, string>.Empty.Add("purelysharp_emit_explanations", "true"),
                additionalMetadataReferences: ImmutableArray.Create(fixture.Reference));

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("bcl_fallback_unknown"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("bcl_heuristic_fallback"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.BclFallbackGuessProperty], Is.EqualTo("unknown"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.BclFallbackReasonProperty], Is.EqualTo("reference_returning_instance_metadata_method"));

            var fallbackDiagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.BclFallbackGuessId);
            Assert.That(fallbackDiagnostic.GetMessage(), Does.Contain("unknown"));
        }

        [Test]
        public async Task Ps0002_MetadataBclFallback_PropertyGetter_IncludesGuessEvidence()
        {
            using var fixture = CreateMetadataOnlyAssemblyFixture(
                "System.FallbackSdk",
                BclFallbackFixtureSource);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(System.Experimental.NumericBox box)
    {
        return box.Value;
    }
}",
                globalOptions: ImmutableDictionary<string, string>.Empty.Add("purelysharp_emit_explanations", "true"),
                additionalMetadataReferences: ImmutableArray.Create(fixture.Reference));

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("bcl_fallback_probably_pure"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("bcl_heuristic_fallback"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.BclFallbackGuessProperty], Is.EqualTo("probably_pure"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.BclFallbackReasonProperty], Is.EqualTo("metadata_getter_value_like_return"));

            var fallbackDiagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.BclFallbackGuessId);
            Assert.That(fallbackDiagnostic.GetMessage(), Does.Contain("probably_pure"));
        }

        [Test]
        public async Task Ps0002_MetadataBclFallback_DoesNotOverrideGeneratedSummaryEvidence()
        {
            using var fixture = CreateMetadataOnlyAssemblyFixture(
                "System.FallbackSdk",
                BclFallbackFixtureSource);

            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        return System.Experimental.NumericFacts.Normalize(value);
    }
}",
                globalOptions: ImmutableDictionary<string, string>.Empty.Add("purelysharp_emit_explanations", "true"),
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new InMemoryAdditionalText(
                        "Synthetic.NumericFacts.Unknown.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            fixture.AssemblyPath,
                            "System.Experimental.NumericFacts.Normalize(int)",
                            "conservative_unknown",
                            "[\"metadata_only_or_external\"]"))),
                additionalMetadataReferences: ImmutableArray.Create(fixture.Reference));

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("metadata_only_or_external"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties.ContainsKey(PurelySharpDiagnostics.BclFallbackGuessProperty), Is.False);
            Assert.That(diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.BclFallbackGuessId), Is.False);
        }

        [Test]
        public async Task Ps0002_MutableStateWrite_IncludesDistinctCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    private int _value;

    [EnforcePure]
    public void TestMethod()
    {
        _value = 1;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("mutable_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("AssignmentPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass._value"));
        }

        [Test]
        public async Task Ps0002_AssignmentRhsImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        int value;
        value = Console.Read();
        return value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_MutableStateRead_IncludesDistinctCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    private static int s_value;

    [EnforcePure]
    public int TestMethod()
    {
        return s_value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("mutable_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("FieldReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass.s_value"));
        }

        [Test]
        public async Task Ps0002_ContextStaticRead_IncludesDistinctCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [ContextStatic]
    private static int s_value;

    [EnforcePure]
    public int TestMethod()
    {
        return s_value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("mutable_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("FieldReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass.s_value"));
        }

        [Test]
        public async Task Ps0002_StaticPropertyGetterImpurity_PreservesGetterEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    private static int Value
    {
        get
        {
            Console.WriteLine(""impure"");
            return 1;
        }
    }

    [EnforcePure]
    public int TestMethod()
    {
        return Value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("TestClass.Value.get"));
        }

        [Test]
        public async Task Ps0002_StaticConstructorTrigger_PreservesConstructorEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class Config
{
    static Config()
    {
        Console.WriteLine(""impure"");
    }

    public static int Value => 1;
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Config.Value;
    }
}");

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_MethodArgumentImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Math.Abs(Console.Read());
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_LinqArgumentImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.Collections.Generic;
using System.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> TestMethod(IEnumerable<int> values)
    {
        return values.Skip(Console.Read());
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_DirectThrowOnly_IncludesThrowCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        throw new InvalidOperationException();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ThrowOperationPurityRule"));
        }

        [Test]
        public async Task Ps0002_ThrowExceptionExpressionImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        throw new InvalidOperationException(Console.ReadLine());
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.ReadLine"));
        }

        [Test]
        public async Task Ps0002_ThrowExpression_IncludesThrowEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? throw new FormatException(""fuzz"")
            : text.Length;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ThrowOperationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("Throw"));
        }

        [Test]
        public async Task Ps0002_UnsafePointerOperation_IncludesUnsafePointerCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public unsafe int TestMethod()
    {
        int value = 1;
        int* pointer = &value;
        return *pointer;
    }
}", allowUnsafe: true);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unsafe_pointer"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("UnsupportedOperation"));
        }

        [Test]
        public async Task Ps0002_MutualRecursivePurityConservativeDiagnostic_IncludesStructuredEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Fibonacci(int n)
    {
        if (n <= 1)
        {
            return n;
        }

        return Bounce(n - 1) + Bounce(n - 2);
    }

    private int Bounce(int n)
    {
        if (n <= 1)
        {
            return n;
        }

        return Fibonacci(n);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unsupported_operation"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("RecursivePurityAnalysis"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("recursive_call"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass.Fibonacci"));
        }

        [Test]
        public async Task Ps0002_ImplicitIndexerWithImpureLengthGetter_PreservesRealCalleeEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public sealed class Bag
{
    public int Length
    {
        get
        {
            Console.WriteLine(""length"");
            return 3;
        }
    }

    public int this[int index] => index + 10;
}

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod(Bag bag)
    {
        return bag[^1];
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("Invocation"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("Bag.Length.get"));
        }

        [Test]
        public async Task Ps0002_MutualRecursionWithRealImpurity_PreservesRealCalleeEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void A()
    {
        B();
    }

    [EnforcePure]
    public void B()
    {
        A();
        Console.WriteLine(""impure"");
    }
}");

            var diagnostic = diagnostics
                .Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(diagnostic => diagnostic.GetMessage().Contains("'A'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("TestClass.B"));
        }

        [Test]
        public async Task Ps0002_EnvironmentTickCountProperty_UsesGeneratedConservativeUnknownEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Environment.TickCount;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("metadata_only_or_external"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Environment.TickCount"));
        }

        [Test]
        public async Task Ps0002_EnvironmentCurrentManagedThreadId_UsesGeneratedConservativeUnknownEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Environment.CurrentManagedThreadId;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("metadata_only_or_external"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Environment.CurrentManagedThreadId"));
        }

        [Test]
        public async Task Ps0002_EnvironmentTickCount64_UsesGeneratedConservativeUnknownEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod()
    {
        return Environment.TickCount64;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("metadata_only_or_external"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Environment.TickCount64"));
        }

        [Test]
        public async Task Ps0002_EnvironmentExitCode_UsesGeneratedConservativeUnknownEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Environment.ExitCode;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("metadata_only_or_external"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Environment.ExitCode"));
        }

        [Test]
        public async Task Ps0002_EnvironmentExitMethod_UsesGeneratedConservativeUnknownEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Environment.Exit(1);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unknown_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Environment.Exit"));
        }

        [Test]
        public async Task GeneratedPuritySummary_Allows_EnvironmentTickCount_WhenSyntheticMetadataEvidenceIsPure()
        {
            const string metadataSymbol = "System.Environment.get_TickCount()";
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Environment.TickCount;
    }
}",
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new InMemoryAdditionalText(
                        "Synthetic.Environment.TickCount.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            typeof(Environment).Assembly.Location,
                            metadataSymbol,
                            "pure",
                            "[]"))));

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated pure summaries should override the conservative TickCount fallback.");
        }

        [Test]
        public async Task Ps0002_EnvironmentStackTrace_UsesGeneratedPuritySummaryEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.StackTrace;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Environment.StackTrace"));
        }

        [Test]
        public async Task Ps0002_ThreadCurrentThread_UsesGeneratedPuritySummaryEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Thread TestMethod()
    {
        return Thread.CurrentThread;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Threading.Thread.CurrentThread"));
        }

        [Test]
        public async Task Ps0002_ReflectionCall_IncludesReflectionEnvironmentCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type? TestMethod(string typeName)
    {
        return Type.GetType(typeName);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("reflection_environment_source"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Type.GetType"));
        }

        [Test]
        public async Task Ps0002_TypeFullName_UsesDerivedDispatchEvidenceAfterManualCatalogRemoval()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
#nullable enable
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? TestMethod(System.Type type)
    {
        return type.FullName;
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("dynamic_dispatch"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties.ContainsKey(PurelySharpDiagnostics.ImpurityCatalogSourceProperty), Is.False);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Type.FullName"));
        }

        [Test]
        public async Task Ps0002_TypeTypeHandle_UsesDerivedDispatchEvidenceAfterManualCatalogRemoval()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public RuntimeTypeHandle TestMethod(System.Type type)
    {
        return type.TypeHandle;
    }
}",
                additionalFiles: ImmutableArray<AdditionalText>.Empty);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("dynamic_dispatch"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties.ContainsKey(PurelySharpDiagnostics.ImpurityCatalogSourceProperty), Is.False);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Type.TypeHandle"));
        }

        [Test]
        public async Task Ps0002_AssemblyLoadContextDefault_UsesSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Runtime.Loader;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public AssemblyLoadContext TestMethod()
    {
        return AssemblyLoadContext.Default;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("reflection_environment_source"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("assembly_load_context_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Runtime.Loader.AssemblyLoadContext.Default"));
        }

        [Test]
        public async Task Ps0002_AssemblyLoadContextLoadFromAssemblyPath_UsesSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Reflection;
using System.Runtime.Loader;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Assembly TestMethod(AssemblyLoadContext context, string path)
    {
        return context.LoadFromAssemblyPath(path);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("reflection_environment_source"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("assembly_load_context_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Runtime.Loader.AssemblyLoadContext.LoadFromAssemblyPath"));
        }

        [Test]
        public async Task Ps0002_AssemblyLoadContextConstructor_UsesSemanticRuleSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Runtime.Loader;
using PurelySharp.Attributes;

public sealed class DerivedLoadContext : AssemblyLoadContext
{
    public DerivedLoadContext() : base(""test"", isCollectible: true)
    {
    }
}

public class TestClass
{
    [EnforcePure]
    public DerivedLoadContext TestMethod()
    {
        return new DerivedLoadContext();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("reflection_environment_source"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("assembly_load_context_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("DerivedLoadContext.DerivedLoadContext()"));
        }

        [Test]
        public async Task GeneratedPuritySummary_Allows_TypeToString_WhenMetadataEvidenceIsPure()
        {
            const string metadataSymbol = "System.Type.ToString()";
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(System.Type type)
    {
        return type.ToString();
    }
}",
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new InMemoryAdditionalText(
                        "Synthetic.TypeToString.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            typeof(Type).Assembly.Location,
                            metadataSymbol,
                            "pure",
                            "[]"))));

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated pure summaries should override the conservative reflection fallback for Type.ToString().");
        }

        [Test]
        public async Task GeneratedPuritySummary_PrefersStrongerExactMatch_RegardlessOfAdditionalFileOrder()
        {
            const string metadataSymbol = "System.Type.ToString()";
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(System.Type type)
    {
        return type.ToString();
    }
}",
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new InMemoryAdditionalText(
                        "Synthetic.TypeToString.Unknown.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            typeof(Type).Assembly.Location,
                            metadataSymbol,
                            "conservative_unknown",
                            "[\"metadata_only_or_external\"]")),
                    new InMemoryAdditionalText(
                        "Synthetic.TypeToString.Pure.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            typeof(Type).Assembly.Location,
                            metadataSymbol,
                            "pure",
                            "[]"))));

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted exact-match purity selection should prefer the stronger pure summary regardless of additional-file order.");
        }

        [Test]
        public async Task GeneratedPuritySummary_Allows_TypeToString_InsideInterpolatedString_WhenMetadataEvidenceIsPure()
        {
            const string metadataSymbol = "System.Type.ToString()";
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(System.Type type)
    {
        return $""{type}"";
    }
}",
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new InMemoryAdditionalText(
                        "Synthetic.TypeToString.Interpolation.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            typeof(Type).Assembly.Location,
                            metadataSymbol,
                            "pure",
                            "[]"))));

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated pure summaries should override the manual ToString catalog inside interpolated strings.");
        }

        [Test]
        public async Task GeneratedPuritySummary_Reports_InterpolatedSystemToString_WhenMetadataEvidenceIsConservativeUnknown()
        {
            const string boundarySource = @"
namespace System
{
    public sealed class SyntheticSystemFormattingBoundary
    {
        public override string ToString()
        {
            return ""ok"";
        }
    }
}";

            var tempDirectory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "diagnostic-evidence-boundary-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var assemblyPath = Path.Combine(tempDirectory, "SyntheticSystemFormattingBoundary.dll");
                var boundarySyntaxTree = CSharpSyntaxTree.ParseText(boundarySource, new CSharpParseOptions(LanguageVersion.Preview));
                var boundaryCompilation = CSharpCompilation.Create(
                    "SyntheticSystemFormattingBoundary",
                    new[] { boundarySyntaxTree },
                    GetTrustedPlatformReferences(),
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                var emitResult = boundaryCompilation.Emit(assemblyPath);
                Assert.That(
                    emitResult.Success,
                    Is.True,
                    string.Join(Environment.NewLine, emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));

                var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(System.SyntheticSystemFormattingBoundary value)
    {
        return $""{value}"";
    }
}",
                    additionalFiles: ImmutableArray.Create<AdditionalText>(
                        new InMemoryAdditionalText(
                            "Synthetic.SystemFormattingBoundary.Unknown.PurelySharp.EffectSummary.json",
                            CreatePuritySummaryJson(
                                assemblyPath,
                                "System.SyntheticSystemFormattingBoundary.ToString()",
                                "conservative_unknown",
                                "[\"metadata_only_or_external\"]"))),
                    additionalMetadataReferences: ImmutableArray.Create<MetadataReference>(MetadataReference.CreateFromFile(assemblyPath)));

                var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

                Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
                Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("metadata_only_or_external"));
                Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.SyntheticSystemFormattingBoundary.ToString"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public async Task GeneratedPuritySummary_Allows_TypeGetTypeFromHandle_WhenMetadataEvidenceIsPure()
        {
            const string metadataSymbol = "System.Type.GetTypeFromHandle(System.RuntimeTypeHandle)";
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type TestMethod(RuntimeTypeHandle handle)
    {
        return Type.GetTypeFromHandle(handle);
    }
}",
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new InMemoryAdditionalText(
                        "Synthetic.TypeGetTypeFromHandle.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            typeof(Type).Assembly.Location,
                            metadataSymbol,
                            "pure",
                            "[]"))));

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated pure summaries should override the conservative reflection fallback for Type.GetTypeFromHandle(RuntimeTypeHandle).");
        }

        [Test]
        public async Task Ps0002_LockStatement_IncludesSynchronizationCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    private readonly object _gate = new object();

    [EnforcePure]
    public void TestMethod()
    {
        lock (_gate)
        {
        }
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("synchronization"));
        }

        [Test]
        public async Task Ps0002_MonitorCall_IncludesSynchronizationCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    private static readonly object Gate = new object();

    [EnforcePure]
    public void TestMethod()
    {
        Monitor.Enter(Gate);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("synchronization"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Threading.Monitor.Enter"));
        }

        [Test]
        public async Task Ps0002_MutableCollectionCreation_IncludesCatalogEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public List<int> TestMethod()
    {
        return new List<int>();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.List<int>.List()"));
        }

        [Test]
        public async Task Ps0002_VariableInitializerImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        int value = Console.Read();
        return value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_SpreadOperandImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.Collections.Immutable;
using PurelySharp.Attributes;

public class TestClass
{
    private static ImmutableArray<int> GetValues()
    {
        Console.WriteLine(""side effect"");
        return ImmutableArray<int>.Empty;
    }

    [EnforcePure]
    public ImmutableArray<int> Extend()
    {
        return [.. GetValues(), 42];
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("TestClass.GetValues"));
        }

        [Test]
        public async Task Ps0002_DirectArrayCreation_IncludesArrayCreationEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] TestMethod()
    {
        return new int[1];
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("mutable_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ArrayCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("array_creation"));
        }

        [Test]
        public async Task Ps0002_GenericTypeConstruction_IncludesObjectCreationEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass<T> where T : new()
{
    [EnforcePure]
    public T TestMethod()
    {
        return new T();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unsupported_operation"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("TypeParameterObjectCreation"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generic_type_construction"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("T"));
        }

        [Test]
        public async Task Ps0002_ArrayElementImpureArrayReference_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return GetValues()[0];
    }

    [EnforcePure]
    private int[] GetValues()
    {
        Console.WriteLine(""impure"");
        return new int[1];
    }
}");

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("TestClass.GetValues"));
        }

        [Test]
        public async Task Ps0002_ArrayInitializerElementImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] TestMethod()
    {
        int[] values = new[] { Console.Read() };
        return values;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_ArrayDimensionImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        int[] values = new int[Console.Read()];
        return values.Length;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_UserDefinedConversionImpurity_PreservesOperatorEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public readonly struct Wrapped
{
    public static explicit operator int(Wrapped value)
    {
        Console.WriteLine(""side effect"");
        return 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Wrapped value)
    {
        return (int)value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_UserDefinedBinaryOperatorImpurity_PreservesOperatorEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public readonly struct Wrapped
{
    public static Wrapped operator +(Wrapped left, Wrapped right)
    {
        Console.WriteLine(""side effect"");
        return left;
    }
}

public class TestClass
{
    [EnforcePure]
    public Wrapped TestMethod(Wrapped left, Wrapped right)
    {
        return left + right;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_UserDefinedUnaryOperatorImpurity_PreservesOperatorEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public readonly struct Wrapped
{
    public static Wrapped operator -(Wrapped value)
    {
        Console.WriteLine(""side effect"");
        return value;
    }
}

public class TestClass
{
    [EnforcePure]
    public Wrapped TestMethod(Wrapped value)
    {
        return -value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_UsingDisposeImpurity_PreservesDisposeEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public sealed class Resource : IDisposable
{
    public void Dispose()
    {
        Console.WriteLine(""side effect"");
    }
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        using (var resource = new Resource())
        {
        }
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("Resource.Dispose"));
        }

        [Test]
        public async Task Ps0002_ConstructorInitializerImpurity_PreservesConstructorEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class BaseType
{
    public BaseType()
    {
        Console.WriteLine(""side effect"");
    }
}

public class DerivedType : BaseType
{
    [EnforcePure]
    public DerivedType()
        : base()
    {
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("BaseType.BaseType"));
        }

        [Test]
        public async Task Ps0002_DelegateCreationImpurity_IncludesTargetCalleeChain()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    public static void ImpureTarget()
    {
        Console.WriteLine(""side effect"");
    }

    [EnforcePure]
    public void TestMethod()
    {
        Action action = ImpureTarget;
        action();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("TestClass.ImpureTarget"));
        }

        [Test]
        public async Task Ps0002_EventAssignment_IncludesMutableStateWriteEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    public event EventHandler? Changed;

    private void Handler(object? sender, EventArgs args)
    {
    }

    [EnforcePure]
    public void TestMethod()
    {
        Changed += Handler;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("mutable_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("EventAssignmentPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("event_subscription"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass.Changed"));
        }

        [Test]
        public async Task Ps0002_InterpolatedStringExpressionImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return $""{Console.Read()}"";
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_ArrayCollectionExpression_IncludesTargetEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] TestMethod()
    {
        return [1, 2, 3];
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("mutable_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("CollectionExpressionPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("collection_expression_target"));
        }

        [Test]
        public async Task Ps0009_IsOnlyEmittedWhenExplanationsAreEnabled()
        {
            var source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Console.WriteLine(""impure"");
    }
}";

            var defaultDiagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var explanationDiagnostics = await GetAnalyzerDiagnosticsAsync(
                source,
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_emit_explanations", "true"));

            Assert.That(defaultDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityExplanationId), Is.False);
            Assert.That(explanationDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityExplanationId), Is.True);
        }

        [Test]
        public async Task Ps0010_ExceptionSummary_IsOptIn()
        {
            var source = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        throw new InvalidOperationException();
    }
}";

            var defaultDiagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var reportDiagnostics = await GetAnalyzerDiagnosticsAsync(
                source,
                ReportExceptionsOptions());
            var checkedDiagnostics = await GetAnalyzerDiagnosticsAsync(
                source,
                CheckedExceptionsOptions());
            var combinedDiagnostics = await GetAnalyzerDiagnosticsAsync(
                source,
                ReportAndCheckedExceptionsOptions());

            Assert.That(defaultDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
            Assert.That(defaultDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
            Assert.That(reportDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.True);
            Assert.That(reportDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
            Assert.That(checkedDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
            Assert.That(checkedDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.True);
            Assert.That(combinedDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.True);
            Assert.That(combinedDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.True);
        }

        [Test]
        public async Task Ps0010_DirectThrows_ReportsExceptionTypes()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public string TestMethod(string? value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return value.Length > 0 ? value : throw new InvalidOperationException();
    }
}",
                ReportExceptionsOptions());

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("System.ArgumentNullException"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentNullException;System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_CaughtThrow_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
        }
    }
}",
                CheckedExceptionsOptions());

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NestedLambdaThrow_IsNotReportedOnOuterMethod()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public Func<int> TestMethod()
    {
        return () => throw new InvalidOperationException();
    }
}",
                ReportExceptionsOptions());

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SourceCalleeThrow_PropagatesToCaller()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void Caller()
    {
        Callee();
    }

    private void Callee()
    {
        throw new InvalidOperationException();
    }
}",
                ReportExceptionsOptions());

            var exceptionDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .ToArray();

            Assert.That(exceptionDiagnostics.Length, Is.EqualTo(2));
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'Caller'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'Callee'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_SourceCalleeThrow_CaughtByCaller_IsSuppressedOnCaller()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void Caller()
    {
        try
        {
            Callee();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Callee()
    {
        throw new InvalidOperationException();
    }
}",
                ReportExceptionsOptions());

            var exceptionDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .ToArray();

            Assert.That(exceptionDiagnostics.Any(d => d.GetMessage().Contains("'Caller'", StringComparison.Ordinal)), Is.False);
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'Callee'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_SourceConstructorThrow_PropagatesToFactory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public Widget Create()
    {
        return new Widget();
    }
}

public class Widget
{
    public Widget()
    {
        throw new InvalidOperationException();
    }
}",
                ReportExceptionsOptions());

            var exceptionDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .ToArray();

            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'Create'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'.ctor'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_SourceConstructorThrow_CaughtAtCreation_IsSuppressedOnFactory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public Widget? Create()
    {
        try
        {
            return new Widget();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

public class Widget
{
    public Widget()
    {
        throw new InvalidOperationException();
    }
}",
                ReportExceptionsOptions());

            var exceptionDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .ToArray();

            Assert.That(exceptionDiagnostics.Any(d => d.GetMessage().Contains("'Create'", StringComparison.Ordinal)), Is.False);
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'.ctor'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_SourcePropertyGetterThrow_PropagatesToReader()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int Read(Widget widget)
    {
        return widget.Value;
    }
}

public class Widget
{
    public int Value
    {
        get
        {
            throw new InvalidOperationException();
        }
    }
}",
                ReportExceptionsOptions());

            var exceptionDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .ToArray();

            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'Read'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'get_Value'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_SourcePropertyGetterThrow_CaughtAtRead_IsSuppressedOnReader()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int Read(Widget widget)
    {
        try
        {
            return widget.Value;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }
}

public class Widget
{
    public int Value
    {
        get
        {
            throw new InvalidOperationException();
        }
    }
}",
                ReportExceptionsOptions());

            var exceptionDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .ToArray();

            Assert.That(exceptionDiagnostics.Any(d => d.GetMessage().Contains("'Read'", StringComparison.Ordinal)), Is.False);
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'get_Value'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_EffectSummaryLibraryCall_PropagatesToCaller()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
    }
}",
                ReportExceptionsOptions(),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(ArgumentNullException).Assembly.Location,
                        "System.ArgumentNullException.ThrowIfNull(object, string)",
                        Array.Empty<string>(),
                        "System.ArgumentNullException"))));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentNullException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull"));
            AssertExceptionEdgesPropertyContainsIfPresent(
                diagnostic,
                "System.ArgumentNullException",
                "System.ArgumentNullException.ThrowIfNull");
        }

        [Test]
        public async Task Ps0010_EffectSummaryLibraryCall_CaughtAtCallSite_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(object value)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(value);
        }
        catch (ArgumentNullException)
        {
        }
    }
}",
                ReportExceptionsOptions(),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(ArgumentNullException).Assembly.Location,
                        "System.ArgumentNullException.ThrowIfNull(object, string)",
                        Array.Empty<string>(),
                        "System.ArgumentNullException"))));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0011_EffectSummaryLibraryCall_UncaughtAtCallSite_ReportsWarning()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
    }
}",
                CheckedExceptionsOptions(),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(ArgumentNullException).Assembly.Location,
                        "System.ArgumentNullException.ThrowIfNull(object, string)",
                        Array.Empty<string>(),
                        "System.ArgumentNullException"))));

            var diagnostic = SingleDiagnostic(
                diagnostics.Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId).ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("ArgumentNullException.ThrowIfNull(value)"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentNullException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSymbolProperty], Does.Contain("System.ArgumentNullException.ThrowIfNull"));
            AssertExceptionEdgesPropertyContainsIfPresent(
                diagnostic,
                "System.ArgumentNullException",
                "System.ArgumentNullException.ThrowIfNull");
        }

        [Test]
        public async Task Ps0011_EffectSummaryGuardHelperChain_UncaughtAtCallSite_ReportsRecursiveExceptionSet()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
    }
}",
                CheckedExceptionsOptions(),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(ArgumentException).Assembly.Location,
                        "System.ArgumentException.ThrowIfNullOrEmpty(string, string)",
                        Array.Empty<string>(),
                        "System.ArgumentException",
                        "System.ArgumentNullException"))));

            var diagnostic = SingleDiagnostic(
                diagnostics.Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId).ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("ArgumentException.ThrowIfNullOrEmpty(text)"));
            Assert.That(
                diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty],
                Is.EqualTo("System.ArgumentException;System.ArgumentNullException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(
                diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty],
                Does.Contain("System.ArgumentException=effect_summary:System.ArgumentException.ThrowIfNullOrEmpty"));
            Assert.That(
                diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty],
                Does.Contain("System.ArgumentNullException=effect_summary:System.ArgumentException.ThrowIfNullOrEmpty"));
            Assert.That(
                diagnostic.Properties[PurelySharpDiagnostics.ExceptionSymbolProperty],
                Does.Contain("System.ArgumentException.ThrowIfNullOrEmpty"));
        }

        [Test]
        public async Task Ps0011_EffectSummaryLibraryCall_CaughtAtCallSite_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(object value)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(value);
        }
        catch (ArgumentNullException)
        {
        }
    }
}",
                CheckedExceptionsOptions(),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(ArgumentNullException).Assembly.Location,
                        "System.ArgumentNullException.ThrowIfNull(object, string)",
                        Array.Empty<string>(),
                        "System.ArgumentNullException"))));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0010AndPs0011_EffectSummaryLibraryCall_PartiallyCaughtAtCallSite_ReportsOnlyEscapingType()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(object value)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(value);
        }
        catch (ArgumentNullException)
        {
        }
    }
}",
                ReportAndCheckedExceptionsOptions(),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(ArgumentNullException).Assembly.Location,
                        "System.ArgumentNullException.ThrowIfNull(object, string)",
                        new[] { "System.ArgumentNullException", "System.InvalidOperationException" }))));

            var summaryDiagnostic = SingleDiagnostic(
                diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.InvalidOperationException=effect_summary:System.ArgumentNullException.ThrowIfNull"));

            var siteDiagnostic = SingleDiagnostic(
                diagnostics.Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId).ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);
            Assert.That(siteDiagnostic.GetMessage(), Does.Contain("ArgumentNullException.ThrowIfNull(value)"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.InvalidOperationException=effect_summary:System.ArgumentNullException.ThrowIfNull"));
        }

        [Test]
        public async Task Ps0011_EffectSummaryConstructor_UncaughtAtCallSite_ReportsWarning()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public Uri Create(string value)
    {
        return new Uri(value);
    }
}",
                CheckedExceptionsOptions(),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(Uri).Assembly.Location,
                        "System.Uri..ctor(string)",
                        new[] { "System.UriFormatException" }))));

            var diagnostic = SingleDiagnostic(
                diagnostics.Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId).ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("new Uri(value)"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.UriFormatException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSymbolProperty], Does.Contain("System.Uri"));
        }

        [Test]
        public async Task Ps0011_EffectSummaryConstructor_CaughtAtCallSite_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public Uri? Create(string value)
    {
        try
        {
            return new Uri(value);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}",
                CheckedExceptionsOptions(),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(Uri).Assembly.Location,
                        "System.Uri..ctor(string)",
                        new[] { "System.UriFormatException" }))));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0010AndPs0011_EffectSummaryConstructorInitializer_PropagateToDerivedConstructor()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class DerivedException : Exception
{
    public DerivedException(string value)
        : base(value)
    {
    }
}",
                ReportAndCheckedExceptionsOptions(),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(Exception).Assembly.Location,
                        "System.Exception..ctor(string)",
                        new[] { "System.InvalidOperationException" }))));

            var summaryDiagnostic = SingleDiagnostic(
                diagnostics
                    .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                    .Where(d => d.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty] == "effect_summary")
                    .Where(d => (d.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty] ?? string.Empty).Contains("System.Exception", StringComparison.Ordinal))
                    .ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.Exception"));

            var siteDiagnostic = SingleDiagnostic(
                diagnostics
                    .Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId)
                    .Where(d => d.GetMessage().Contains("base(value)", StringComparison.Ordinal))
                    .ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSymbolProperty], Does.Contain("System.Exception"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.Exception"));
        }

        [Test]
        public async Task Ps0011_SourceCallee_UncaughtAtCallSite_ReportsWarning()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void Caller()
    {
        Callee();
    }

    private void Callee()
    {
        throw new InvalidOperationException();
    }
}",
                CheckedExceptionsOptions());

            var diagnostic = SingleDiagnostic(
                diagnostics
                    .Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId)
                    .Where(d => d.GetMessage().Contains("Callee()", StringComparison.Ordinal))
                    .ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("Callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("source_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.InvalidOperationException=source_callee:"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSymbolProperty], Does.Contain("Callee"));
        }

        [Test]
        public async Task Ps0011_SourceCallee_LocalFunctionPropagation_EmitsExceptionEdges()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Local();

        void Local()
        {
            throw new InvalidOperationException();
        }
    }
}",
                CheckedExceptionsOptions());

            var diagnostic = SingleDiagnostic(
                diagnostics
                    .Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId)
                    .Where(d => d.GetMessage().Contains("Local()", StringComparison.Ordinal))
                    .ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("source_callee"));
            AssertExceptionEdgesPropertyContains(
                diagnostic,
                "System.InvalidOperationException",
                "TestMethod",
                "TestClass.Local()");
        }

        [Test]
        public async Task Ps0011_SourceCallee_LambdaPropagation_EmitsExceptionEdges()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        Action thunk = () => throw new InvalidOperationException();
        thunk();
    }
}",
                CheckedExceptionsOptions());

            var diagnostic = SingleDiagnostic(
                diagnostics
                    .Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId)
                    .Where(d => d.GetMessage().Contains("thunk()", StringComparison.Ordinal))
                    .ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("source_callee"));
            AssertExceptionEdgesPropertyContains(
                diagnostic,
                "System.InvalidOperationException",
                "TestMethod",
                "lambda expression");
        }

        [Test]
        public async Task Ps0011_SourceCallee_CaughtAtCallSite_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void Caller()
    {
        try
        {
            Callee();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Callee()
    {
        throw new InvalidOperationException();
    }
}",
                CheckedExceptionsOptions());

            var siteDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId)
                .ToImmutableArray();

            Assert.That(siteDiagnostics.Any(d => d.GetMessage().Contains("Callee()", StringComparison.Ordinal)), Is.False);
            Assert.That(siteDiagnostics.Any(d => d.GetMessage().Contains("throw new InvalidOperationException()", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public async Task Ps0010AndPs0011_SourceConstructorInitializer_PropagateToDerivedConstructor()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class BaseClass
{
    protected BaseClass(string value)
    {
        throw new InvalidOperationException();
    }
}

public class DerivedClass : BaseClass
{
    public DerivedClass(string value)
        : base(value)
    {
    }
}",
                ReportAndCheckedExceptionsOptions());

            var summaryDiagnostic = SingleDiagnostic(
                diagnostics
                    .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                    .Where(d => d.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty] == "source_callee")
                    .Where(d => (d.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty] ?? string.Empty).Contains("BaseClass", StringComparison.Ordinal))
                    .ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("source_callee"));
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("BaseClass"));
            Assert.That(summaryDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("direct_throw:throw"));

            var siteDiagnostic = SingleDiagnostic(
                diagnostics
                    .Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId)
                    .Where(d => d.GetMessage().Contains("base(value)", StringComparison.Ordinal))
                    .ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("source_callee"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSymbolProperty], Does.Contain("BaseClass"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("BaseClass"));
            Assert.That(siteDiagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("direct_throw:throw"));
        }

        [Test]
        public async Task Ps0011_InterfaceMethodDispatch_AliasLocalExactConcreteReceiver_ReportsSourceCalleeEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public interface IService
{
    void Work();
}

public sealed class ThrowingService : IService
{
    public void Work()
    {
        throw new InvalidOperationException();
    }
}

public class TestClass
{
    public void TestMethod()
    {
        var concrete = new ThrowingService();
        IService alias = concrete;
        alias.Work();
    }
}",
                CheckedExceptionsOptions());

            var diagnostic = SingleDiagnostic(
                diagnostics
                    .Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId)
                    .Where(d => d.GetMessage().Contains("alias.Work()", StringComparison.Ordinal))
                    .ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("alias.Work()"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("source_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.InvalidOperationException=source_callee:"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSymbolProperty], Does.Contain("ThrowingService.Work"));
        }

        [Test]
        public async Task Ps0011_DirectThrow_UncaughtAtSite_ReportsWarning()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        throw new InvalidOperationException();
    }
}",
                CheckedExceptionsOptions());

            var diagnostic = SingleDiagnostic(
                diagnostics.Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId).ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("throw new InvalidOperationException()"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidOperationException=direct_throw:throw"));
        }

        [Test]
        public async Task Ps0011_DefiniteDivideByZero_UncaughtAtSite_ReportsWarning()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value / 0;
    }
}",
                CheckedExceptionsOptions());

            var diagnostic = SingleDiagnostic(
                diagnostics.Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId).ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("value / 0"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.DivideByZeroException=definite_divide_by_zero:binary_operator"));
        }

        [Test]
        public async Task Ps0011_DefiniteNullDereference_UncaughtAtSite_ReportsWarning()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        string value = null!;
        return value.Length;
    }
}",
                CheckedExceptionsOptions());

            var diagnostic = SingleDiagnostic(
                diagnostics.Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId).ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("value.Length"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.NullReferenceException=definite_null_dereference:null_receiver"));
        }

        [Test]
        public async Task Ps0011_DefiniteDivideByZero_Caught_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod(int value)
    {
        try
        {
            return value / 0;
        }
        catch (DivideByZeroException)
        {
            return 0;
        }
    }
}",
                CheckedExceptionsOptions());

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0011_DefiniteNullDereference_Caught_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod()
    {
        try
        {
            string value = null!;
            return value.Length;
        }
        catch (NullReferenceException)
        {
            return 0;
        }
    }
}",
                CheckedExceptionsOptions());

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId), Is.False);
        }

        [Test]
        public async Task Ps0011_SourceCallee_MultiHopChain_PreservesRecursiveSourceEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void Entry()
    {
        Outer();
    }

    private void Outer()
    {
        Inner();
    }

    private void Inner()
    {
        throw new InvalidOperationException();
    }
}",
                CheckedExceptionsOptions());

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId)
                .Single(d => d.GetMessage().Contains("Outer()", StringComparison.Ordinal));

            var sources = diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty];
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(sources, Does.Contain("Outer()"));
            Assert.That(sources, Does.Contain("Inner()"));
            Assert.That(sources, Does.Contain("direct_throw:throw"));
            AssertExceptionEdgesPropertyContainsIfPresent(
                diagnostic,
                "System.InvalidOperationException",
                "Outer",
                "Inner");
        }

        [Test]
        public async Task Ps0010_EffectSummaryConstructor_PropagatesToFactory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public Uri Create(string value)
    {
        return new Uri(value);
    }
}",
                ReportExceptionsOptions(),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(Uri).Assembly.Location,
                        "System.Uri..ctor(string)",
                        new[] { "System.UriFormatException" }))));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'Create'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.UriFormatException"));
        }

        [Test]
        public async Task Ps0010_EffectSummaryPropertyGetter_PropagatesToReader()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public string Read()
    {
        return Environment.CurrentDirectory;
    }
}",
                ReportExceptionsOptions(),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(Environment).Assembly.Location,
                        "System.Environment.get_CurrentDirectory()",
                        new[] { "System.InvalidOperationException" }))));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'Read'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0011_EffectSummaryPropertyGetter_UncaughtAtAccessSite_ReportsWarning()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public string Read()
    {
        return Environment.CurrentDirectory;
    }
}",
                CheckedExceptionsOptions(),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(Environment).Assembly.Location,
                        "System.Environment.get_CurrentDirectory()",
                        new[] { "System.InvalidOperationException" }))));

            var diagnostic = SingleDiagnostic(
                diagnostics.Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId).ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("Environment.CurrentDirectory"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSymbolProperty], Does.Contain("System.Environment.CurrentDirectory.get"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.Environment.CurrentDirectory.get"));
        }

        [Test]
        public async Task Ps0010_RethrowTypedCatch_ReportsCaughtExceptionType()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            Dangerous();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
    }

    private void Dangerous()
    {
    }
}",
                ReportExceptionsOptions());

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_RethrowTypedCatch_CaughtByOuterTry_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            try
            {
                Dangerous();
            }
            catch (InvalidOperationException)
            {
                throw;
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Dangerous()
    {
    }
}",
                ReportExceptionsOptions());

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ConstantIntegerDivideByZero_ReportsDivideByZeroException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value / 0;
    }
}",
                ReportExceptionsOptions());

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_ConstantDecimalModuloByZero_Caught_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public decimal TestMethod(decimal value)
    {
        try
        {
            return value % 0m;
        }
        catch (DivideByZeroException)
        {
            return 0m;
        }
    }
}",
                ReportExceptionsOptions());

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_FloatingPointDivideByZero_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public double TestMethod(double value)
    {
        return value / 0.0;
    }
}",
                ReportExceptionsOptions());

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_IfBranchZeroDivisor_ReportsDivideByZeroException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor == 0)
        {
            return value / divisor;
        }

        return 0;
    }
}",
                ReportExceptionsOptions());

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_IfElseNonZeroCondition_ReportsDivideByZeroExceptionInElse()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor != 0)
        {
            return 0;
        }
        else
        {
            return value % divisor;
        }
    }
}",
                ReportExceptionsOptions());

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_DirectNullMemberAccess_ReportsNullReferenceException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        return ((string)null).Length;
    }
}",
                ReportExceptionsOptions());

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.NullReferenceException=definite_null_dereference:null_receiver"));
        }

        [Test]
        public async Task Ps0010_IfBranchNullReceiver_ReportsNullReferenceException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (value is null)
        {
            return value.Length;
        }

        return 0;
    }
}",
                ReportExceptionsOptions());

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Ps0010_IfBranchNullReceiver_ReassignedBeforeUse_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (value == null)
        {
            value = string.Empty;
            return value.Length;
        }

        return 0;
    }
}",
                ReportExceptionsOptions());

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_DirectThrow_IncludesStructuredEvidenceProperties()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        throw new InvalidOperationException();
    }
}",
                ReportExceptionsOptions());

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidOperationException=direct_throw:throw"));
        }

        [Test]
        public async Task Ps0010_FinallyThrow_ShadowsEarlierEscapingThrow()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        finally
        {
            throw new ArgumentNullException();
        }
    }
}",
                ReportExceptionsOptions());

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentNullException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.ArgumentNullException=direct_throw:throw"));
        }

        [Test]
        public async Task Ps0011_FinallyThrow_ShadowsEarlierEscapingThrowSite()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        finally
        {
            throw new ArgumentNullException();
        }
    }
}",
                CheckedExceptionsOptions());

            var siteDiagnostics = diagnostics.Where(d => d.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId).ToArray();

            Assert.That(siteDiagnostics.Length, Is.EqualTo(1));
            Assert.That(siteDiagnostics[0].GetMessage(), Does.Contain("throw new ArgumentNullException()"));
            Assert.That(siteDiagnostics[0].Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentNullException"));
            Assert.That(siteDiagnostics[0].Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0010_DefaultReferenceMemberAccess_Caught_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod()
    {
        try
        {
            return default(string).Length;
        }
        catch (NullReferenceException)
        {
            return 0;
        }
    }
}",
                ReportExceptionsOptions());

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NullConditionalAccess_DoesNotReportNullReferenceException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int? TestMethod()
    {
        return ((string)null)?.Length;
    }
}",
                ReportExceptionsOptions());

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

    }
}
