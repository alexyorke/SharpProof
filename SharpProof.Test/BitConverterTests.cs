using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class BitConverterTests
{
    private static readonly GetBytesType[] GetBytesTypes =
    [
        new("Int", "int", "value + 1"),
        new("Long", "long", "value + 1"),
        new("Float", "float", "value + 1"),
        new("UInt", "uint", "value + 1"),
        new("ULong", "ulong", "value + 1"),
        new("Half", "Half", null, false),
        new("Short", "short", "(short)(value + 1)"),
        new("UShort", "ushort", "(ushort)(value + 1)"),
        new("Bool", "bool", "!value"),
        new("Char", "char", "(char)(value + 1)")
    ];

    private static IEnumerable<TestCaseData> GetBytesCases()
    {
        foreach (var type in GetBytesTypes)
        foreach (var scenario in Enum.GetValues<GetBytesScenario>())
        {
            if (!type.SupportsLocal && scenario != GetBytesScenario.ReturnedArray) continue;

            yield return new TestCaseData(type.TypeName, type.AlternateExpression, scenario.ToString())
                .SetName($"BitConverterGetBytes{type.Name}_{scenario}_NoDiagnostic");
        }
    }

    private static IEnumerable<TestCaseData> SpanCases()
    {
        yield return new TestCaseData("int", "ToInt32").SetName("BitConverterToInt32Span_NoDiagnostic");
        yield return new TestCaseData("double", "ToDouble").SetName("BitConverterToDoubleSpan_NoDiagnostic");
    }

    [TestCaseSource(nameof(GetBytesCases))]
    public async Task BitConverterGetBytes_NoDiagnostic(
        string typeName,
        string? alternateExpression,
        string scenarioName)
    {
        var scenario = Enum.Parse<GetBytesScenario>(scenarioName);
        var parameters = scenario == GetBytesScenario.ConditionalReturnedArray
            ? $"bool useLeft, {typeName} value"
            : $"{typeName} value";
        var body = scenario switch
        {
            GetBytesScenario.ReturnedArray => "return BitConverter.GetBytes(value);",
            GetBytesScenario.LocalReturnedArray => "var bytes = BitConverter.GetBytes(value);\n        return bytes;",
            GetBytesScenario.ConditionalReturnedArray =>
                $"return useLeft\n            ? BitConverter.GetBytes(value)\n            : BitConverter.GetBytes({alternateExpression});",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var test = $@"
using System;
using SharpProof.Attributes;

public class TestClass
{{
    [EnforcePure]
    public byte[] TestMethod({parameters})
    {{
        {body}
    }}
}}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [TestCaseSource(nameof(SpanCases))]
    public async Task BitConverterSpan_NoDiagnostic(string returnType, string methodName)
    {
        var test = $@"
using System;
using SharpProof.Attributes;

public class TestClass
{{
    [EnforcePure]
    public {returnType} TestMethod(ReadOnlySpan<byte> bytes)
    {{
        return BitConverter.{methodName}(bytes);
    }}
}}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    private enum GetBytesScenario
    {
        ReturnedArray,
        LocalReturnedArray,
        ConditionalReturnedArray
    }

    private sealed record GetBytesType(
        string Name,
        string TypeName,
        string? AlternateExpression,
        bool SupportsLocal = true);
}
