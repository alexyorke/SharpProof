using System.Numerics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    private static bool CheckIntegralToInt64Intrinsic(CSharpCompilation references, string api, bool wrongClaim)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(api);
        var parameter = api.Substring(api.IndexOf('(', StringComparison.Ordinal) + 1).TrimEnd(')');
        var (type, minimum, maximum) = parameter switch
        {
            "System.SByte" => ("sbyte", "-128", "127"),
            "System.Byte" => ("byte", "0", "255"),
            "System.Int16" => ("short", "-32768", "32767"),
            "System.UInt16" => ("ushort", "0", "65535"),
            "System.Char" => ("char", "0", "65535"),
            "System.Int32" => ("int", "-2147483648", "2147483647"),
            "System.UInt32" => ("uint", "0", "4294967295"),
            "System.Int64" => ("long", "-9223372036854775808", "9223372036854775807"),
            "System.UInt64" => ("ulong", "0", "18446744073709551615"),
            "System.Boolean" => ("bool", "0", "1"),
            _ => throw new ArgumentOutOfRangeException(nameof(api))
        };
        var edges = new List<string> { minimum, "0", maximum };
        if (type == "ulong")
        { edges.AddRange(["9223372036854775807", "9223372036854775808"]); }
        if (type == "char")
        { edges.Add("55296"); }
        foreach (var edge in edges)
        {
            var number = BigInteger.Parse(edge, System.Globalization.CultureInfo.InvariantCulture);
            var expression = type == "bool" ? (number.IsZero ? "false" : "true") : "(" + type + ")(" + edge + ")";
            var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static long Check() { return System.Convert.ToInt64(value: " + expression + "); } }");
            var compilation = references.AddSyntaxTrees(tree);
            if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error))
            { return false; }
            var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var model = compilation.GetSemanticModel(tree);
            var invocation = (IInvocationOperation)model.GetOperation(syntax.Body!.Statements.OfType<ReturnStatementSyntax>().Single().Expression!)!;
            if (invocation.TargetMethod.GetDocumentationCommentId() != api)
            { return false; }
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
            var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
                static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
            if (!lowered.IsExact)
            { return false; }
            var primitive = invocation.TargetMethod.Parameters[0].Type.SpecialType.ToString();
            // This inventory probe selects an initialized normal path for
            // bounded semantic comparison. Only the native worker may grant
            // concrete replay authority; this choice remains approximate here.
            var options = new IrProgramReplayOptions(request =>
            {
                ArgumentNullException.ThrowIfNull(request);
                var description = factory.GetOperationInfo(request.Site).Description is { } id ? factory.GetString(id) : "";
                if (type == "ulong" && number > long.MaxValue && request.Origin == IrHavocOrigin.Approximation &&
                    factory.GetVariableInfo(request.Variable).Type == factory.BooleanType && description == "framework-integral-convert-overflow-ancillary")
                { return factory.CreateBooleanValue(false); }
                if (request.Origin != IrHavocOrigin.Approximation || factory.GetVariableInfo(request.Variable).Type != factory.BooleanType ||
                    description != "framework-integral-convert-ready:" + primitive)
                { return null; }
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Convert).TypeHandle);
                return RuntimeIntegralConvert(type, BigInteger.Zero) == 0 ? factory.CreateBooleanValue(false) : null;
            });
            var result = new IrProgramInterpreter(factory).Execute(lowered.Program, null, 5000, options);
            if (!result.ConsumedApproximation)
            { return false; }
            try
            {
                var expected = RuntimeIntegralConvert(type, number);
                if (result.Status != IrProgramExecutionStatus.Returned || result.ReturnValue!.IntegerNumericValue != expected + (wrongClaim ? 1 : 0) ||
                    factory.GetTypeInfo(result.ReturnValue.Type).Width != 64 || !factory.GetTypeInfo(result.ReturnValue.Type).Signed)
                { return false; }
            }
            catch (OverflowException)
            {
                if (result.Exception?.Kind != IrExceptionKind.Overflow)
                { return false; }
            }
        }
        return true;
    }

    private static long RuntimeIntegralConvert(string type, BigInteger value)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type switch
        {
            "bool" => Convert.ToInt64(!value.IsZero),
            "sbyte" => Convert.ToInt64((sbyte)value),
            "byte" => Convert.ToInt64((byte)value),
            "short" => Convert.ToInt64((short)value),
            "ushort" => Convert.ToInt64((ushort)value),
            "char" => Convert.ToInt64((char)(ushort)value),
            "int" => Convert.ToInt64((int)value),
            "uint" => Convert.ToInt64((uint)value),
            "long" => Convert.ToInt64((long)value),
            "ulong" => Convert.ToInt64((ulong)value),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }
}
