using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class InterceptorDispatchLoweringRegressionTests
{
    [Test]
    public void ProgramLoweringUsesTheBoundInterceptorMethod()
    {
        const string callerSource = """
            public static class Subject
            {
                public static long Helper(long value) => value;
                public static long Target(long value) => Helper(value);
            }
            """;
        var options = new CSharpParseOptions(LanguageVersion.CSharp13)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "InterceptorsNamespaces", "Interception")]);
        var callerTree = CSharpSyntaxTree.ParseText(
            callerSource,
            options,
            path: "Caller.cs");
        var attributeTree = CSharpSyntaxTree.ParseText(
            """
            namespace System.Runtime.CompilerServices
            {
                [global::System.AttributeUsage(
                    global::System.AttributeTargets.Method,
                    AllowMultiple = true)]
                public sealed class InterceptsLocationAttribute :
                    global::System.Attribute
                {
                    public InterceptsLocationAttribute(int version, string data) { }
                }
            }
            """,
            options,
            path: "InterceptsLocationAttribute.cs");
        var locationCompilation = CSharpCompilation.Create(
            "InterceptorDispatchLocation",
            [callerTree, attributeTree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var callerModel = locationCompilation.GetSemanticModel(callerTree);
        var invocationSyntax = callerTree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single();
        var location = callerModel.GetInterceptableLocation(invocationSyntax);
        Assert.That(location!.Data!, Is.Not.Null.And.Not.Empty);

        var interceptorTree = CSharpSyntaxTree.ParseText(
            """
            using System.Runtime.CompilerServices;

            namespace Interception
            {
                public static class HelperInterceptors
                {
                    [InterceptsLocation(VERSION, "DATA")]
                    public static long Replace(long value) => value + 100L;
                }
            }
            """.Replace(
                "VERSION",
                location!.Version.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal).Replace(
                "DATA",
                EscapeStringLiteral(location!.Data!),
                StringComparison.Ordinal),
            options,
            path: "Interceptors.cs");
        var compilation = CSharpCompilation.Create(
            "InterceptorDispatchLowering",
            [callerTree, attributeTree, interceptorTree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(Environment.NewLine, errors.Select(static value => value.ToString())));

        var model = compilation.GetSemanticModel(callerTree);
        var operation = (IInvocationOperation)model.GetOperation(invocationSyntax)!;
        Assert.That(operation.TargetMethod.Name, Is.EqualTo("Helper"));
        Assert.That(
            model.GetInterceptorMethod(invocationSyntax)?.Name,
            Is.EqualTo("Replace"));

        var method = callerTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static declaration => declaration.Identifier.ValueText == "Target");
        var graph = ControlFlowGraph.Create(method, model);
        var factory = new IrFactory();
        var lowered = new RoslynProgramLowerer(factory).Lower(graph!);
        var call = lowered.Program.Blocks
            .SelectMany(static block => block.Instructions)
            .OfType<IrCallInstruction>()
            .Single();

        Assert.That(
            factory.GetString(factory.GetMemberInfo(call.Member).Name),
            Does.Contain("Replace"));
    }

    private static string EscapeStringLiteral(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
