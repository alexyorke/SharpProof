using System.Numerics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ResourceBoundsFrontendBoundaryGapTests
{
    [Test]
    public void BodylessPartialDeclarationDoesNotInventZeroCost()
    {
        var result = Analyze("public static partial class C { static partial void Root(); }");
        AssertUnknown(result, "MissingBody");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReferencedSourceCallsRequireRootTreeOwnershipBeforePublishingPrefixCosts(bool referenced)
    {
        const string helperSource = "public static class Remote { public static void Leaf() { var inside=new int[4]; } }";
        const string callerSource = "public static class C { public static void Root() { var before=new int[2]; Remote.Leaf(); } }";
        var dependency = AnalyzerTestHost.CreateCompilation(helperSource, [], filePath: "ResourceDependency.cs")
            .WithAssemblyName("ResourceBoundaryDependency");
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(dependency.GetDiagnostics(cancellation).Where(static diagnostic => diagnostic is null ?
            throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var reference = dependency.ToMetadataReference();
        Assert.That(reference.Compilation, Is.SameAs(dependency));
        var compilation = AnalyzerTestHost.CreateCompilation(callerSource + (referenced ? "" : "\n" + helperSource), [],
            additionalReferences: referenced ? [reference] : [], filePath: "ResourceCaller.cs")
            .WithAssemblyName(referenced ? "ResourceBoundaryReferencedCaller" : "ResourceBoundaryOwnedCaller");
        var result = Analyze(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var invocation = tree.GetRoot(cancellation).DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(invocation, cancellation);
        Assert.That(operation, Is.InstanceOf<IInvocationOperation>());
        var target = ((IInvocationOperation)operation!).TargetMethod;
        Assert.That(target.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        var declaration = target.DeclaringSyntaxReferences.Single().GetSyntax(cancellation);
        Assert.That(declaration, Is.InstanceOf<MethodDeclarationSyntax>());
        Assert.That(compilation.SyntaxTrees.Contains(declaration.SyntaxTree), Is.EqualTo(!referenced));
        Assert.That(dependency.SyntaxTrees.Contains(declaration.SyntaxTree), Is.EqualTo(referenced));
        Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly), Is.EqualTo(!referenced));
        if (referenced)
        {
            AssertUnknown(result, "MissingCalleeBody");
            return;
        }
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        var prefixAllocation = new int[2];
        var calleeAllocation = new int[4];
        var independentlyAllocatedElements = prefixAllocation.Length + calleeAllocation.Length;
        Assert.That(independentlyAllocatedElements, Is.EqualTo(6));
        Assert.That(result.Bounds!.Arrays.Constant, Is.EqualTo(new BigInteger(2)));
        Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(new BigInteger(independentlyAllocatedElements)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void VariadicTailArgumentsCannotBorrowTheFixedArgumentRecurrenceRule(bool variadic)
    {
        var source = "public static class C { public static void Root(int n" + (variadic ? ",__arglist" : "") +
            ") { if(n<=0)return; var before=new int[2]; Root(n-1" + (variadic ? ",__arglist(1)" : "") + "); } }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(compilation.GetDiagnostics(cancellation).Where(static diagnostic => diagnostic is null ?
            throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot(cancellation);
        var model = compilation.GetSemanticModel(tree);
        var declaration = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration, cancellation)!;
        Assert.That(method.IsVararg, Is.EqualTo(variadic));
        var invocation = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single(static candidate => candidate is null ?
            throw new ArgumentNullException(nameof(candidate)) : candidate.Expression is IdentifierNameSyntax { Identifier.ValueText: "Root" });
        var operation = model.GetOperation(invocation, cancellation);
        Assert.That(operation, Is.InstanceOf<IInvocationOperation>());
        var tail = (IInvocationOperation)operation!;
        Assert.That(SymbolEqualityComparer.Default.Equals(tail.TargetMethod, method), Is.True);
        Assert.That(tail.Arguments.Length, Is.EqualTo(variadic ? 2 : 1));
        if (variadic)
        {
            var extra = tail.Arguments.Single(static argument => argument is null ?
                throw new ArgumentNullException(nameof(argument)) : argument.Parameter is null);
            Assert.That(extra.Value.Kind, Is.EqualTo(OperationKind.None));
        }
        else
        {
            Assert.That(tail.Arguments.All(static argument => argument is not null && argument.Parameter is not null), Is.True);
        }
        var result = Analyze(compilation);
        if (variadic)
        {
            AssertUnknown(result, "UnsupportedRecursiveArguments");
            return;
        }
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Text, Is.EqualTo("max(0, n)"));
        Assert.That(result.Bounds.Elements.Expression.Evaluate(static _ => 3, cancellation), Is.EqualTo(new BigInteger(6)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnrepresentedInitializerSyntaxDiscardsTheWholeConstructionBound(bool parenthesized)
    {
        var initializer = parenthesized ? "(new int[3])" : "new int[3]";
        var source = "public class Cell { public int[] Data = " + initializer + "; } " +
            "public static class C { public static void Root() { var before=new int[2]; var item=new Cell(); } }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(compilation.GetDiagnostics(cancellation).Where(static diagnostic => diagnostic is null ?
            throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var field = tree.GetRoot(cancellation).DescendantNodes().OfType<FieldDeclarationSyntax>().Single();
        var fieldInitializer = field.Declaration.Variables.Single().Initializer!;
        var operation = model.GetOperation(fieldInitializer, cancellation);
        Assert.That(operation, Is.InstanceOf<IFieldInitializerOperation>());
        var value = ((IFieldInitializerOperation)operation!).Value;
        Assert.That(value, Is.InstanceOf<IArrayCreationOperation>());
        Assert.That(model.GetOperation(fieldInitializer.Value, cancellation), parenthesized ? Is.Null : Is.SameAs(value));
        var expression = parenthesized ? ((ParenthesizedExpressionSyntax)fieldInitializer.Value).Expression : fieldInitializer.Value;
        Assert.That(model.GetOperation(expression, cancellation), Is.SameAs(value));
        var result = Analyze(compilation);
        if (parenthesized)
        {
            AssertUnknown(result, "MissingFieldInitializer");
            return;
        }
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Constant, Is.EqualTo(new BigInteger(2)));
        Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(new BigInteger(5)));
    }

    [TestCase(256)]
    [TestCase(257)]
    public void ExpressionBodiedClosuresKeepAllocationProofsWhenParameterStorageExceedsTheFrameLimit(int parameterCount)
    {
        var parameters = string.Join(",", Enumerable.Range(0, parameterCount).Select(static index =>
            "int p" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var compilation = AnalyzerTestHost.CreateCompilation("public static class C { public static System.Func<int> Root(" +
            parameters + ") => () => p0; }", []);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(compilation.GetDiagnostics(cancellation).Where(static diagnostic => diagnostic is null ?
            throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot(cancellation).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration, cancellation)!;
        Assert.That(method.Parameters.Length, Is.EqualTo(parameterCount));
        var operation = model.GetOperation(declaration.ExpressionBody!.Expression, cancellation);
        Assert.That(operation, Is.InstanceOf<IAnonymousFunctionOperation>());
        var anonymous = (IAnonymousFunctionOperation)operation!;
        var captured = anonymous.Body.DescendantsAndSelf().OfType<IParameterReferenceOperation>().Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(captured.Parameter, method.Parameters[0]), Is.True);
        var result = Analyze(compilation);
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Constant, Is.EqualTo(BigInteger.Zero));
        Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(BigInteger.Zero));
        var program = result.Bounds.Program;
        Assert.That(program.Kind, Is.EqualTo(ResourceProgramKind.Activation));
        Assert.That(program.Children.Single().Kind, Is.EqualTo(ResourceProgramKind.Sequence));
        if (parameterCount == 257)
        {
            Assert.That(program.Activation, Is.Null);
            return;
        }
        Assert.That(program.Activation, Is.Not.Null);
        Assert.That(program.Activation!.Variables, Has.Length.EqualTo(256));
        Assert.That(program.Activation.Variables.All(static storage => storage == ResourceStorageKind.Word32), Is.True);
    }

    [Test]
    public void UnknownFirstBranchDiscardsBothThePrefixAndTheCleanAlternative()
    {
        var result = Analyze("""
            public static class C {
                public static void Root(bool selected) {
                    var before = new int[2];
                    if(selected) { while(true) {} }
                    else { var alternative = new int[3]; }
                }
            }
            """);
        AssertUnknown(result, "UnsupportedLoop");
    }

    [TestCase(3000, true)]
    [TestCase(9000, false)]
    public void IndividuallyRepresentableLoopAndAllocationCostsShareTheOutputBudget(int nameLength, bool supported)
    {
        var count = "count" + new string('n', nameLength);
        var width = "width" + new string('m', nameLength);
        var result = Analyze("public static class C { public static void Root(int " + count + ", int " + width + ") { " +
            "for(int i=0;i<" + count + ";i++) {} var data = new int[" + width + "]; } }");
        if (!supported)
        {
            AssertUnknown(result, "AnalysisBudgetExceeded");
            return;
        }
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(result.Bounds!.LoopIterations.Expression.Evaluate(static _ => 2, cancellation), Is.EqualTo(new BigInteger(2)));
        Assert.That(result.Bounds.Arrays.Constant, Is.EqualTo(BigInteger.One));
        Assert.That(result.Bounds.Elements.Expression.Evaluate(static _ => 3, cancellation), Is.EqualTo(new BigInteger(3)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SequentialAllocationProgramsObeyTheConstructionBudget(bool exhausted)
    {
        var count = exhausted ? ResourceExpression.MaximumDepth + 1 : ResourceExpression.MaximumDepth / 2;
        var allocations = string.Concat(Enumerable.Repeat("_ = new int[2];", count));
        var result = Analyze("public static class C { public static void Root() { " + allocations + " } }");
        if (exhausted)
        {
            AssertUnknown(result, "AnalysisBudgetExceeded");
            return;
        }
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Constant, Is.EqualTo(new BigInteger(count)));
        Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(new BigInteger(count) * 2));
    }

    [TestCase(8, true)]
    [TestCase(9, false)]
    public void SeparatelyAdmittedBooleanBranchesShareTheCompositionBudget(int depth, bool supported)
    {
        var whenTrue = BalancedAllocations(depth, 2);
        var whenFalse = BalancedAllocations(depth, 3);
        foreach (var (body, elements) in new[] { (whenTrue, 2), (whenFalse, 3) })
        {
            var branch = Analyze("public static class C { public static void Root(bool branch) { " + body + " } }");
            Assert.That(branch.Proven, Is.True, branch.UnknownReason);
            Assert.That(branch.Bounds!.Arrays.Constant, Is.EqualTo(BigInteger.One));
            Assert.That(branch.Bounds.Elements.Constant, Is.EqualTo(new BigInteger(elements)));
        }
        var result = Analyze("using SharpProof.Attributes; public static class C { " +
            "public static void Root([ResourceBooleanInput(1,1)] bool selected, bool branch) { " +
            "if(selected) { " + whenTrue + " } else { " + whenFalse + " } } }");
        if (!supported)
        {
            AssertUnknown(result, "AnalysisBudgetExceeded");
            return;
        }
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        Assert.That(result.Bounds!.Arrays.Constant, Is.EqualTo(BigInteger.One));
        Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(new BigInteger(3)));
        Assert.That(result.Bounds.Program.Kind, Is.EqualTo(ResourceProgramKind.BooleanDistribution));
    }

    [TestCase(7, true)]
    [TestCase(8, false)]
    public void ExpandedScalarHelpersCannotBypassTheRecurrenceBudget(int levels, bool supported)
    {
        var source = new StringBuilder("public static class C { public static void Root() { M0(3,new int[3]); } ");
        for (var index = 0; index < levels; index++)
        {
            source.Append("static void M").Append(index).Append("(int n,int[] data) { int first=n,second=n; first+=second; M")
                .Append(index + 1).Append("(n,data); M").Append(index + 1).Append("(n,data); } ");
        }
        source.Append("static void M").Append(levels)
            .Append("(int n,int[] data) { if(n<=0)return; var a=new int[data.Length]; M")
            .Append(levels).Append("(n-1,data); } }");
        var result = Analyze(source.ToString());
        if (!supported)
        {
            AssertUnknown(result, "AnalysisBudgetExceeded");
            return;
        }
        Assert.That(result.Proven, Is.True, result.UnknownReason);
        var arrays = BigInteger.One + (BigInteger.One << levels) * 3;
        Assert.That(result.Bounds!.LoopIterations.Constant, Is.EqualTo(BigInteger.Zero));
        Assert.That(result.Bounds.Arrays.Constant, Is.EqualTo(arrays));
        Assert.That(result.Bounds.Elements.Constant, Is.EqualTo(arrays * 3));
    }

    private static string BalancedAllocations(int depth, int elements)
    {
        if (depth == 0)
        { return "_ = new int[" + elements.ToString(System.Globalization.CultureInfo.InvariantCulture) + "];"; }
        var child = BalancedAllocations(depth - 1, elements);
        return "if(branch) { " + child + " } else { " + child + " }";
    }

    private static ResourceBoundsAnalysis.Result Analyze(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Analyze(AnalyzerTestHost.CreateCompilation(source, []));
    }

    private static ResourceBoundsAnalysis.Result Analyze(Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(compilation.GetDiagnostics(cancellation).Where(static diagnostic => diagnostic is null ?
            throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot(cancellation).DescendantNodes().OfType<MethodDeclarationSyntax>().Single(static method =>
            method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Root");
        var model = compilation.GetSemanticModel(tree);
        var body = declaration.Body is { } block ? model.GetOperation(block, cancellation) :
            declaration.ExpressionBody is { } arrow ? model.GetOperation(arrow.Expression, cancellation) : null;
        return ResourceBoundsAnalysis.Analyze((IMethodSymbol)model.GetDeclaredSymbol(declaration, cancellation)!, body, cancellation);
    }

    private static void AssertUnknown(ResourceBoundsAnalysis.Result result, string reason)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(result.Proven, Is.False);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.Assumptions, Is.Empty);
    }
}
