using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;
namespace SharpProof.Test;
[TestFixture]
public sealed class ContractConditionHelperTests {
    [TestCase("TryParse", typeof(IfStatementSyntax), 1)]
    [TestCase("TryParse", typeof(ExpressionSyntax), 2)]
    [TestCase("TryCreateSpeculativeModel", typeof(Microsoft.CodeAnalysis.SemanticModel), 3)]
    public void TryHelper_FailureOutputHasNullableFlowContract(
        string methodName,
        Type parameterType,
        int parameterIndex) {
        var helperType = typeof(SharpProofAnalyzer).Assembly.GetType(
            "SharpProof.Analyzer.ContractConditionHelpers",
            throwOnError: true)!;
        var method = helperType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
        var parameter = method.GetParameters()[parameterIndex];
        var notNullWhen = parameter.CustomAttributes.SingleOrDefault(attribute =>
            attribute.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute");
        Assert.Multiple(() => {
            Assert.That(parameter.ParameterType, Is.EqualTo(parameterType.MakeByRefType()));
            Assert.That(new NullabilityInfoContext().Create(parameter).WriteState, Is.EqualTo(NullabilityState.Nullable));
            Assert.That(notNullWhen?.ConstructorArguments.Single().Value, Is.True);
        });
    }
    [TestCase("TryCreateOperationBlockContext")]
    [TestCase("TryCreateSyntaxContext")]
    public void PipelineTryHelper_FailureOutputHasNullableFlowContract(string methodName) {
        var analyzerAssembly = typeof(SharpProofAnalyzer).Assembly;
        var pipelineType = analyzerAssembly.GetType(
            "SharpProof.Analyzer.AnalyzerFeaturePipeline",
            throwOnError: true)!;
        var contextType = analyzerAssembly.GetType(
            "SharpProof.Analyzer.MethodBodyAnalysisContext",
            throwOnError: true)!;
        var method = pipelineType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
        var parameter = method.GetParameters()[2];
        var notNullWhen = parameter.CustomAttributes.SingleOrDefault(attribute =>
            attribute.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute");
        Assert.Multiple(() => {
            Assert.That(parameter.ParameterType, Is.EqualTo(contextType.MakeByRefType()));
            Assert.That(new NullabilityInfoContext().Create(parameter).WriteState, Is.EqualTo(NullabilityState.Nullable));
            Assert.That(notNullWhen?.ConstructorArguments.Single().Value, Is.True);
        });
    }
    [TestCase(
        "SharpProof.Analyzer.MethodEnsuresAnalyzer",
        "TryRewriteConditionForCompletionSite",
        4,
        "Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax")]
    [TestCase(
        "SharpProof.Analyzer.MethodEnsuresAnalyzer",
        "TryCreateEntrySnapshotProofCondition",
        5,
        "SharpProof.Symbolic.Ir.SymbolicCondition")]
    [TestCase(
        "SharpProof.Analyzer.MethodEnsuresAnalyzer+OldValueSnapshotBuilder",
        "TryLowerInvocationTerm",
        2,
        "SharpProof.Symbolic.Ir.SymbolicTerm")]
    public void MethodEnsuresTryHelper_FailureOutputHasNullableFlowContract(
        string typeName,
        string methodName,
        int parameterIndex,
        string parameterTypeName) {
        var analyzerAssembly = typeof(SharpProofAnalyzer).Assembly;
        var declaringType = analyzerAssembly.GetType(typeName, throwOnError: true)!;
        var method = declaringType.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var parameter = method.GetParameters()[parameterIndex];
        var notNullWhen = parameter.CustomAttributes.SingleOrDefault(attribute =>
            attribute.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute");
        Assert.Multiple(() => {
            Assert.That(parameter.ParameterType.IsByRef, Is.True);
            Assert.That(parameter.ParameterType.GetElementType()?.FullName, Is.EqualTo(parameterTypeName));
            Assert.That(new NullabilityInfoContext().Create(parameter).WriteState, Is.EqualTo(NullabilityState.Nullable));
            Assert.That(notNullWhen?.ConstructorArguments.Single().Value, Is.True);
        });
    }
    [Test]
    public void SymbolicInvocationTermLowerer_FailureOutputHasNullableFlowContract() {
        var symbolicAssembly = typeof(SharpProof.Symbolic.MethodEffects).Assembly;
        var delegateType = symbolicAssembly.GetType(
            "SharpProof.Symbolic.Ir.SymbolicInvocationTermLowerer",
            throwOnError: true)!;
        var parameter = delegateType.GetMethod("Invoke")!.GetParameters()[2];
        var notNullWhen = parameter.CustomAttributes.SingleOrDefault(attribute =>
            attribute.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute");
        Assert.Multiple(() => {
            Assert.That(new NullabilityInfoContext().Create(parameter).WriteState, Is.EqualTo(NullabilityState.Nullable));
            Assert.That(notNullWhen?.ConstructorArguments.Single().Value, Is.True);
        });
    }
}
