using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
[NonParallelizable]
public sealed class RuntimeRequiresOracleTests
{
    [Test]
    public async Task RandomizedRequiresDiagnosticsMatchRuntimePredicates()
    {
        var random = new Random(41873);
        var cases = Enumerable.Range(0, 192)
            .Select(index => new RangeCase(
                index,
                random.Next(-100, 101),
                random.Next(-100, 101),
                random.Next(-100, 101)))
            .ToArray();
        var lines = new List<string> {
            "using System;",
            "using SharpProof.Attributes;",
            "public static class RuntimeRequiresFixture {",
            "    public static bool Range(int value, int minimum, int maximum) {",
            "        Contract.Requires(value >= minimum && value <= maximum);",
            "        return value >= minimum && value <= maximum;",
            "    }",
            "    private static int Throwing() =>",
            "        throw new InvalidOperationException();"
        };
        foreach (var item in cases)
        {
            lines.Add($"    public static void Case{item.Index:D3}() {{");
            lines.Add(
                $"        Range({item.Value}, {item.Minimum}, {item.Maximum});");
            item.CallLine = lines.Count - 1;
            lines.Add("    }");
        }
        lines.Add("    public static void ThrowingArgument() {");
        lines.Add("        Range(-1, Throwing(), 0);");
        var throwingArgumentLine = lines.Count - 1;
        lines.Add("    }");
        lines.Add("    public static void ThrowingPrefix() {");
        lines.Add("        _ = Throwing();");
        lines.Add("        Range(-1, 0, 10);");
        var throwingPrefixLine = lines.Count - 1;
        lines.Add("    }");
        lines.Add("}");
        var source = string.Join("\n", lines);
        var compilation = AnalyzerTestHost.CreateCompilation(
            source,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            "contracts");
        var image = AnalyzerTestHost.EmitImage(compilation);

        WithRuntimeAssembly(image, assembly =>
        {
            var fixture = assembly.GetType(
                    "RuntimeRequiresFixture",
                    throwOnError: true)!;
            var range = fixture.GetMethod(
                    "Range",
                    BindingFlags.Public | BindingFlags.Static) ??
                throw new InvalidOperationException(
                    "Runtime Range method is missing.");
            var runtimeRefutationLines = cases
                .Where(item => !(bool)range.Invoke(
                    null,
                    [item.Value, item.Minimum, item.Maximum])!)
                .Select(static item => item.CallLine)
                .Order()
                .ToArray();
            var diagnosticLines = diagnostics
                .Where(static diagnostic => diagnostic.Id == "SP0027")
                .Select(static diagnostic =>
                    diagnostic.Location.GetLineSpan()
                        .StartLinePosition.Line)
                .Order()
                .ToArray();

            Assert.That(runtimeRefutationLines, Is.Not.Empty);
            Assert.That(
                runtimeRefutationLines.Length,
                Is.LessThan(cases.Length));
            Assert.That(diagnosticLines, Is.EqualTo(runtimeRefutationLines));
            Assert.That(
                diagnosticLines,
                Does.Not.Contain(throwingArgumentLine));
            Assert.That(
                diagnosticLines,
                Does.Not.Contain(throwingPrefixLine));
            AssertRuntimeThrows(fixture, "ThrowingArgument");
            AssertRuntimeThrows(fixture, "ThrowingPrefix");
        });
    }

    private static void AssertRuntimeThrows(Type fixture, string methodName)
    {
        var method = fixture.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static) ??
            throw new InvalidOperationException(
                $"Runtime method '{methodName}' is missing.");
        Action invocation = () => method.Invoke(null, null);
        var exception =
            Assert.Throws<TargetInvocationException>(invocation);
        Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    private static void WithRuntimeAssembly(
        byte[] image,
        Action<Assembly> action)
    {
        var context = new AssemblyLoadContext(
            "SharpProof.Analyzer.Test.RuntimeRequires",
            isCollectible: true);
        context.Resolving += ResolveFromDefaultContext;
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            action(context.LoadFromStream(stream));
        }
        finally
        {
            context.Resolving -= ResolveFromDefaultContext;
            context.Unload();
        }
    }

    private static Assembly? ResolveFromDefaultContext(
        AssemblyLoadContext context,
        AssemblyName requestedName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate =>
                AssemblyName.ReferenceMatchesDefinition(
                    candidate.GetName(),
                    requestedName));
    }

    private sealed class RangeCase(
        int index,
        int value,
        int minimum,
        int maximum)
    {
        internal int Index { get; } = index;
        internal int Value { get; } = value;
        internal int Minimum { get; } = minimum;
        internal int Maximum { get; } = maximum;
        internal int CallLine
        {
            get; set;
        }
    }
}
