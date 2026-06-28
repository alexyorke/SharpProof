using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    public partial class DiagnosticEvidenceTests
    {
        private static IEnumerable<TestCaseData> GetThreadingSemanticRuleCases()
        {
            yield return new TestCaseData(
                @"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(object gate)
    {
        Monitor.Enter(gate);
    }
}",
                "synchronization",
                "MethodInvocationPurityRule",
                "System.Threading.Monitor.Enter")
                .SetName("Ps0002_MonitorEnter_UsesThreadingSemanticRuleSource");

            yield return new TestCaseData(
                @"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Thread.Sleep(1);
    }
}",
                "catalog_hit",
                "MethodInvocationPurityRule",
                "System.Threading.Thread.Sleep")
                .SetName("Ps0002_ThreadSleep_UsesThreadingSemanticRuleSource");

            yield return new TestCaseData(
                @"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Thread thread)
    {
        return thread.ManagedThreadId;
    }
}",
                "catalog_hit",
                "PropertyReferencePurityRule",
                "System.Threading.Thread.ManagedThreadId")
                .SetName("Ps0002_ThreadManagedThreadId_UsesThreadingSemanticRuleSource");

            yield return new TestCaseData(
                @"
using System;
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public CancellationTokenRegistration TestMethod(CancellationToken token)
    {
        return token.Register(() => { });
    }
}",
                "catalog_hit",
                "MethodInvocationPurityRule",
                "System.Threading.CancellationToken.Register")
                .SetName("Ps0002_CancellationTokenRegister_UsesThreadingSemanticRuleSource");

            yield return new TestCaseData(
                @"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(AsyncLocal<int> state)
    {
        return state.Value;
    }
}",
                "catalog_hit",
                "PropertyReferencePurityRule",
                "System.Threading.AsyncLocal")
                .SetName("Ps0002_AsyncLocalValue_UsesThreadingSemanticRuleSource");

            yield return new TestCaseData(
                @"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Semaphore TestMethod()
    {
        return new Semaphore(0, 1);
    }
}",
                "catalog_hit",
                "ObjectCreationPurityRule",
                "System.Threading.Semaphore.Semaphore")
                .SetName("Ps0002_SemaphoreConstructor_UsesThreadingSemanticRuleSource");

            yield return new TestCaseData(
                @"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ThreadLocal<int> state)
    {
        return state.Value;
    }
}",
                "catalog_hit",
                "PropertyReferencePurityRule",
                "System.Threading.ThreadLocal")
                .SetName("Ps0002_ThreadLocalValue_UsesThreadingSemanticRuleSource");

            yield return new TestCaseData(
                @"
using System.Threading.Channels;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Channel<int> TestMethod()
    {
        return Channel.CreateUnbounded<int>();
    }
}",
                "catalog_hit",
                "MethodInvocationPurityRule",
                "System.Threading.Channels.Channel.CreateUnbounded")
                .SetName("Ps0002_ChannelCreateUnbounded_UsesThreadingSemanticRuleSource");
        }

        private static ImmutableDictionary<string, string> ReportExceptionsOptions()
        {
            return ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true");
        }

        private static ImmutableDictionary<string, string> CheckedExceptionsOptions()
        {
            return ImmutableDictionary<string, string>.Empty.Add("purelysharp_checked_exceptions", "true");
        }

        private static ImmutableDictionary<string, string> ReportAndCheckedExceptionsOptions()
        {
            return ReportExceptionsOptions().Add("purelysharp_checked_exceptions", "true");
        }

        private static void AssertExceptionEdgesPropertyContains(Diagnostic diagnostic, params string[] expectedFragments)
        {
            Assert.That(
                diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ExceptionEdgesProperty, out var serializedEdges) &&
                !string.IsNullOrWhiteSpace(serializedEdges),
                Is.True,
                "Expected purelysharp.exceptions.edges on diagnostic.");

            foreach (var expectedFragment in expectedFragments)
            {
                Assert.That(serializedEdges, Does.Contain(expectedFragment));
            }
        }

        private static void AssertExceptionEdgesPropertyContainsIfPresent(Diagnostic diagnostic, params string[] expectedFragments)
        {
            if (!diagnostic.Properties.TryGetValue(PurelySharpDiagnostics.ExceptionEdgesProperty, out var serializedEdges) ||
                string.IsNullOrWhiteSpace(serializedEdges))
            {
                return;
            }

            foreach (var expectedFragment in expectedFragments)
            {
                Assert.That(serializedEdges, Does.Contain(expectedFragment));
            }
        }

        private static string CreateEffectSummaryJson(
            string assemblyPath,
            string symbol,
            string[] thrownExceptionTypes,
            params string[] transitiveThrownExceptionTypes)
        {
            return GeneratedPurityTestSupport.CreateEffectSummaryJson(
                assemblyPath,
                symbol,
                thrownExceptionTypes,
                transitiveThrownExceptionTypes);
        }

        private static string CreatePuritySummaryJson(
            string assemblyPath,
            string actualMethodLookupSymbol,
            string classification,
            string categoriesJson,
            string? symbolOverride = null)
        {
            return GeneratedPurityTestSupport.CreatePuritySummaryJson(
                assemblyPath,
                actualMethodLookupSymbol,
                classification,
                categoriesJson,
                symbolOverride);
        }

        private static AnalyzerOptions CreateAnalyzerOptions(
            ImmutableDictionary<string, string>? globalOptions = null,
            ImmutableArray<AdditionalText>? additionalFiles = null)
        {
            return AnalyzerTestHost.CreateAnalyzerOptions(globalOptions, additionalFiles);
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            ImmutableDictionary<string, string>? globalOptions = null,
            bool allowUnsafe = false,
            ImmutableArray<AdditionalText>? additionalFiles = null,
            ImmutableArray<MetadataReference>? additionalMetadataReferences = null)
        {
            return await AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                globalOptions,
                allowUnsafe,
                additionalFiles,
                additionalMetadataReferences,
                "DiagnosticEvidenceTests");
        }

        private static AnalyzerOptions CreateGeneratedPurityAnalyzerOptions()
        {
            return CreateAnalyzerOptions();
        }

        private static ImmutableArray<AdditionalText> CreateSyntheticGeneratedPurityAdditionalFiles(
            string assemblyPath,
            params (string FileName, string ActualMethodLookupSymbol, string DisplaySymbol, string Classification, string CategoriesJson)[] entries)
        {
            return GeneratedPurityTestSupport.CreateSyntheticGeneratedPurityAdditionalFiles(
                entries.Select(entry => (
                    assemblyPath,
                    entry.FileName,
                    entry.ActualMethodLookupSymbol,
                    entry.DisplaySymbol,
                    entry.Classification,
                    entry.CategoriesJson)).ToArray());
        }

        private static string FormatJsonArray(params string[] values)
        {
            if (values.Length == 0)
            {
                return "[]";
            }

            return "[\"" + string.Join("\", \"", values) + "\"]";
        }

        private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences() =>
            AnalyzerTestHost.GetTrustedPlatformReferences();
    }
}
