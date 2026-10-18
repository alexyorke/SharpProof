using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275FrontendRequiredInputTests
{
    private static readonly CSharpCompilation ValidCompilation = TestCompilation.Create(
        "Pr275FrontendRequiredInput", ("Subject.cs", """
            using SharpProof.Attributes;

            internal static class Subject
            {
                [EnforcePure]
                internal static void ThrowException() => throw new System.Exception();
            }
            """));

    [Test]
    public void NullFrontendInputsReportTheirRequiredParameterNames()
    {
        var method = GetSubjectDeclaration();
        var exceptionType = ValidCompilation.GetTypeByMetadataName("System.Exception")!;
        var creation = GetExceptionCreation();
        var attributeType = ValidCompilation.GetTypeByMetadataName(
            ContractApiMetadata.EnforcePure)!;

        var tryCatchCompilationError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = CSharpOperationSemantics.TryCatchKinds(null!, exceptionType, out _)));
        var thrownTypeCompilationError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = CSharpOperationSemantics.ThrownType(null!, creation)));
        var thrownTypeOperandError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = CSharpOperationSemantics.ThrownType(ValidCompilation, null!)));
        var exceptionCreationError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = CSharpOperationSemantics.IsCoreExceptionCreation(null!)));
        var candidateNameError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = ContractApiMetadata.IsContractMethodCandidateName(null!)));
        var attributeError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = ContractApiMetadata.IsAttribute(null!, attributeType)));
        var sameSiteLeftError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = SyntaxSite.IsSame(null!, method)));
        var sameSiteRightError = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = SyntaxSite.IsSame(method, null!)));

        Assert.That(tryCatchCompilationError!.ParamName, Is.EqualTo("compilation"));
        Assert.That(thrownTypeCompilationError!.ParamName, Is.EqualTo("compilation"));
        Assert.That(thrownTypeOperandError!.ParamName, Is.EqualTo("operand"));
        Assert.That(exceptionCreationError!.ParamName, Is.EqualTo("creation"));
        Assert.That(candidateNameError!.ParamName, Is.EqualTo("name"));
        Assert.That(attributeError!.ParamName, Is.EqualTo("attribute"));
        Assert.That(sameSiteLeftError!.ParamName, Is.EqualTo("left"));
        Assert.That(sameSiteRightError!.ParamName, Is.EqualTo("right"));
    }

    [Test]
    public void ValidFrontendInputsUseRealCompilerAndContractSymbols()
    {
        var exceptionType = ValidCompilation.GetTypeByMetadataName("System.Exception")!;
        var catchKindsFound = CSharpOperationSemantics.TryCatchKinds(
            ValidCompilation, exceptionType, out var catchKinds);
        var creation = GetExceptionCreation();
        var thrownType = CSharpOperationSemantics.ThrownType(ValidCompilation, creation);
        var attribute = GetRealEnforcePureAttribute();
        var attributeType = ValidCompilation.GetTypeByMetadataName(
            ContractApiMetadata.EnforcePure)!;
        var sourceTree = ValidCompilation.SyntaxTrees.Single();
        var sourceMethod = GetSubjectDeclaration();
        var sameSpanMethodInOtherTree = CSharpSyntaxTree.ParseText(
                sourceTree.GetText().ToString())
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();

        Assert.That(catchKindsFound, Is.True);
        Assert.That(catchKinds, Does.Contain(IrExceptionKind.Unknown));
        Assert.That(catchKinds, Does.Contain(IrExceptionKind.Explicit));
        Assert.That(
            SymbolEqualityComparer.Default.Equals(thrownType, exceptionType),
            Is.True);
        Assert.That(CSharpOperationSemantics.IsCoreExceptionCreation(creation), Is.True);
        Assert.That(
            ContractApiMetadata.IsContractMethodCandidateName(
                ContractApiMetadata.RequiresMethodName),
            Is.True);
        Assert.That(
            ContractApiMetadata.IsContractMethodCandidateName("NotAContractMethod"),
            Is.False);
        Assert.That(ContractApiMetadata.IsAttribute(attribute, attributeType), Is.True);
        Assert.That(
            ContractApiMetadata.IsAttribute(
                attribute,
                ValidCompilation.GetTypeByMetadataName("System.ObsoleteAttribute")),
            Is.False);
        Assert.That(sourceMethod.Span, Is.EqualTo(sameSpanMethodInOtherTree.Span));
        Assert.That(SyntaxSite.IsSame(sourceMethod, sourceMethod), Is.True);
        Assert.That(SyntaxSite.IsSame(sourceMethod, sameSpanMethodInOtherTree), Is.False);
    }

    private static IMethodSymbol GetSubjectMethod()
    {
        return ValidCompilation.GetTypeByMetadataName("Subject")!
            .GetMembers("ThrowException")
            .OfType<IMethodSymbol>()
            .Single();
    }

    private static MethodDeclarationSyntax GetSubjectDeclaration()
    {
        return ValidCompilation.SyntaxTrees.Single().GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
    }

    private static IObjectCreationOperation GetExceptionCreation()
    {
        var tree = ValidCompilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot()
            .DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Single();
        return (IObjectCreationOperation)ValidCompilation
            .GetSemanticModel(tree)
            .GetOperation(syntax)!;
    }

    private static AttributeData GetRealEnforcePureAttribute()
    {
        return GetSubjectMethod().GetAttributes().Single();
    }
}
