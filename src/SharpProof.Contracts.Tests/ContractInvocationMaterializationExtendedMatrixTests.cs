using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractInvocationMaterializationExtendedMatrixTests
{
    private static readonly bool[] BooleanValues = [false, true];
    private static readonly BoundContractKind[] BooleanClauseKinds =
    [
        BoundContractKind.Requires,
        BoundContractKind.Assume,
        BoundContractKind.Ensures
    ];
    private static readonly BoundContractKind[] RequiresOnlyKinds = [BoundContractKind.Requires];
    private static readonly BoundContractKind[] CompanionClauseKinds =
    [
        BoundContractKind.Requires,
        BoundContractKind.Ensures
    ];
    private const string BooleanSource = """
        using SharpProof.Attributes;
        public static class BooleanFrames {
            public static bool Evaluate(bool left, bool right) {
                Contract.Requires(left && !right);
                Contract.Assume(left || right);
                Contract.Ensures(Contract.Result<bool>() == (Contract.Old(left) == Contract.Old(right)));
                return left;
            }
        }
        """;
    private const string CompanionSource = """
        using SharpProof.Attributes;
        public interface ITarget { bool Check(bool value); }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static bool Check(ITarget receiver, bool value) {
                Contract.Requires(value || !value);
                Contract.Ensures(Contract.Result<bool>() == Contract.Old(value));
                return value;
            }
        }
        """;
    private const string AtomicFailureSource = """
        using SharpProof.Attributes;
        public static class OrdinaryCalls {
            public static void Zero() {}
            public static void Two(bool first, bool second) {}
            public static int One(int value) => value;
        }
        public static class AtomicFrames {
            public static bool PrefixThenRaw(bool input) {
                Contract.Requires(input);
                OrdinaryCalls.Zero();
                OrdinaryCalls.Two(input, !input);
                _ = OrdinaryCalls.One(17);
                return input;
            }
        }
        """;
    private const string UnsupportedOperandSource = """
        using SharpProof.Attributes;
        public interface IUnsupportedTarget { bool Check(bool input); }
        [ContractFor(typeof(IUnsupportedTarget))]
        public static class UnsupportedContracts {
            public static bool Predicate(bool input) => input;
            public static bool Check(IUnsupportedTarget receiver, bool input) {
                Contract.Requires(input);
                Contract.Ensures(Predicate(Contract.Result<bool>()));
                return input;
            }
        }
        """;

    [Test]
    public void CanonicalKindsMaterializeSupportedBooleanOperatorsAndTruthTables()
    {
        var context = CreateDirectContext(
            "InvocationMaterializationBooleanMatrix",
            BooleanSource,
            "BooleanFrames",
            "Evaluate");
        var publicContracts = RequirePublicContracts(context);
        var candidates = RecognizedCandidates(context.Body, context.Api);

        Assert.That(context.PublicIsSuccess, Is.True, context.PublicFailure.ToString());
        Assert.That(publicContracts.UsesCompanion, Is.False);
        Assert.That(candidates.Select(candidate => candidate.Kind), Is.EqualTo(BooleanClauseKinds));
        Assert.That(publicContracts.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)),
            Is.EqualTo(BooleanClauseKinds));
        Assert.That(candidates.Select(candidate => ((candidate.Invocation is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(candidate)) : (candidate.Invocation.Arguments.Length))),
            Is.All.EqualTo(1));
        Assert.That(candidates.Select(candidate =>
                ((candidate.Invocation is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(candidate)) : (candidate.Invocation.Arguments[0].Value.Type?.SpecialType))),
            Is.All.EqualTo(SpecialType.System_Boolean));

        var result = ContractInvocationMaterialization.Bind(
            context.Factory,
            context.ExpressionBinder,
            candidates,
            publicContracts.UsesCompanion,
            requiresOnly: false);

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)), Is.EqualTo(BooleanClauseKinds));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Evidence)),
            Is.All.EqualTo(BoundContractEvidence.CompilerBoundInvocation));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.DiagnosticText)),
            Is.EqualTo(candidates.Select(candidate =>
                ((candidate.Invocation is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(candidate)) : (candidate.Invocation.Arguments[0].Value.Syntax.ToString())))));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.SourceSyntax!.Span)),
            Is.EqualTo(candidates.Select(candidate => ((candidate.Invocation is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(candidate)) : (candidate.Invocation.Syntax.Span)))));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (DescribeOperation(context.Factory, clause))),
            Is.EqualTo(candidates.Select(candidate =>
                ((candidate.Invocation is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(candidate)) : ("contract@" + candidate.Invocation.Syntax.SpanStart.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))))));

        var interpreter = new IrInterpreter(context.Factory);
        foreach (var left in BooleanValues)
        {
            foreach (var right in BooleanValues)
            {
                var currentValues = CreatePairValues(context, left, right);
                Assert.That(
                    interpreter.Evaluate(result.Clauses[0].Condition, currentValues).Value!.Boolean,
                    Is.EqualTo(left && !right));
                Assert.That(
                    interpreter.Evaluate(result.Clauses[1].Condition, currentValues).Value!.Boolean,
                    Is.EqualTo(left || right));

                foreach (var oldLeft in BooleanValues)
                {
                    foreach (var oldRight in BooleanValues)
                    {
                        foreach (var resultValue in BooleanValues)
                        {
                            var values = CreatePairValues(
                                context, left, right, oldLeft, oldRight, resultValue);
                            Assert.That(
                                interpreter.Evaluate(result.Clauses[2].Condition, values).Value!.Boolean,
                                Is.EqualTo(resultValue == (oldLeft == oldRight)));
                        }
                    }
                }
            }
        }
    }

    [Test]
    public void DirectRequiresOnlyMaterializationFiltersOtherActualCanonicalKinds()
    {
        var context = CreateDirectContext(
            "InvocationMaterializationRequiresOnlyMatrix",
            BooleanSource,
            "BooleanFrames",
            "Evaluate");
        var publicContracts = RequirePublicContracts(context);
        var candidates = RecognizedCandidates(context.Body, context.Api);

        Assert.That(candidates.Select(candidate => candidate.Kind), Is.EqualTo(BooleanClauseKinds));
        var result = ContractInvocationMaterialization.Bind(
            context.Factory,
            context.ExpressionBinder,
            candidates,
            publicContracts.UsesCompanion,
            requiresOnly: true);

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)), Is.EqualTo(RequiresOnlyKinds));
        Assert.That(context.ExpressionBinder.ResultVariable, Is.Null);
        Assert.That(context.ExpressionBinder.PreStateVariables, Is.Empty);

        var interpreter = new IrInterpreter(context.Factory);
        foreach (var left in BooleanValues)
        {
            foreach (var right in BooleanValues)
            {
                var values = CreatePairValues(context, left, right);
                Assert.That(
                    interpreter.Evaluate(result.Clauses.Single().Condition, values).Value!.Boolean,
                    Is.EqualTo(left && !right));
            }
        }
    }

    [Test]
    public void MaterializedEvidenceUsesTheActualPublicCompanionResolution()
    {
        var context = CreateCompanionContext();
        var publicContracts = RequirePublicContracts(context);
        var candidates = RecognizedCandidates(context.Body, context.Api);

        Assert.That(publicContracts.UsesCompanion, Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            publicContracts.Source, context.Method), Is.True);
        Assert.That(candidates.Select(candidate => candidate.Kind), Is.EqualTo(CompanionClauseKinds));
        var result = ContractInvocationMaterialization.Bind(
            context.Factory,
            context.ExpressionBinder,
            candidates,
            publicContracts.UsesCompanion,
            requiresOnly: false);

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)), Is.EqualTo(CompanionClauseKinds));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Evidence)),
            Is.All.EqualTo(BoundContractEvidence.Companion));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.SourceSyntax!.SyntaxTree)),
            Is.All.SameAs(context.Tree));

        var interpreter = new IrInterpreter(context.Factory);
        foreach (var value in BooleanValues)
        {
            var currentValues = CreateSingleValue(context, value);
            Assert.That(
                interpreter.Evaluate(result.Clauses[0].Condition, currentValues).Value!.Boolean,
                Is.True);
            foreach (var oldValue in BooleanValues)
            {
                foreach (var resultValue in BooleanValues)
                {
                    var values = CreateSingleValue(context, value, oldValue, resultValue);
                    Assert.That(
                        interpreter.Evaluate(result.Clauses[1].Condition, values).Value!.Boolean,
                        Is.EqualTo(resultValue == oldValue));
                }
            }
        }
    }

    [TestCase("Zero", ContractBindingFailure.InvalidIntrinsicSignature)]
    [TestCase("Two", ContractBindingFailure.InvalidIntrinsicSignature)]
    [TestCase("One", ContractBindingFailure.NonBooleanCondition)]
    public void RawShapeRejectionReturnsNoClausesAfterAValidCanonicalPrefix(
        string rawMethodName,
        ContractBindingFailure expectedFailure)
    {
        ArgumentNullException.ThrowIfNull(rawMethodName);
        var context = CreateDirectContext(
            "InvocationMaterializationAtomicPrefix_" + rawMethodName,
            AtomicFailureSource,
            "AtomicFrames",
            "PrefixThenRaw");
        var publicContracts = RequirePublicContracts(context);
        var candidates = RecognizedCandidates(context.Body, context.Api);
        var prefix = candidates.Single();
        var rawInvocation = context.Body.DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.TargetMethod.Name == rawMethodName));

        Assert.That(publicContracts.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)),
            Is.EqualTo(RequiresOnlyKinds));
        Assert.That(prefix.Kind, Is.EqualTo(BoundContractKind.Requires));
        Assert.That(context.Api.Clauses.GetClauseKind(rawInvocation.TargetMethod), Is.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            rawInvocation.TargetMethod.ContainingAssembly, context.Compilation.Assembly), Is.True);

        var withRawCandidate = ImmutableArray.Create(
            prefix,
            new ContractInvocationCandidate(BoundContractKind.Requires, rawInvocation));
        var result = ContractInvocationMaterialization.Bind(
            context.Factory,
            context.ExpressionBinder,
            withRawCandidate,
            publicContracts.UsesCompanion,
            requiresOnly: false);

        Assert.That(result.Failure, Is.EqualTo(expectedFailure));
        Assert.That(result.Clauses, Is.Empty);
    }

    [Test]
    public void GenuineUnsupportedMethodCallOperandIsRejectedByTheMaterializer()
    {
        var context = CreateCompanionContext(
            "InvocationMaterializationUnsupportedOperand",
            UnsupportedOperandSource,
            "IUnsupportedTarget",
            "UnsupportedContracts",
            requiresOnly: true);
        var publicContracts = RequirePublicContracts(context);
        var candidates = RecognizedCandidates(context.Body, context.Api);
        var candidate = candidates.Single(item => item.Kind == BoundContractKind.Ensures);
        var operand = candidate.Invocation.Arguments.Single().Value;

        Assert.That(publicContracts.UsesCompanion, Is.True);
        Assert.That(publicContracts.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)),
            Is.EqualTo(RequiresOnlyKinds));
        Assert.That(candidates.Select(item => item.Kind), Is.EqualTo(CompanionClauseKinds));
        Assert.That(operand, Is.InstanceOf<IInvocationOperation>());
        var operandInvocation = (IInvocationOperation)operand;
        Assert.That(operandInvocation.TargetMethod.Name, Is.EqualTo("Predicate"));
        Assert.That(operandInvocation.TargetMethod.ReturnType.SpecialType,
            Is.EqualTo(SpecialType.System_Boolean));
        Assert.That(SymbolEqualityComparer.Default.Equals(
            operandInvocation.TargetMethod.ContainingAssembly, context.Compilation.Assembly), Is.True);

        var result = ContractInvocationMaterialization.Bind(
            context.Factory,
            context.ExpressionBinder,
            candidates,
            publicContracts.UsesCompanion,
            requiresOnly: false);

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(result.Clauses, Is.Empty);
    }

    [Test]
    public void LongValidBooleanConditionUsesTheBoundedDiagnosticText()
    {
        const int commentLength = 2200;
        var comment = new string('x', commentLength);
        var source = $$"""
            using SharpProof.Attributes;
            public static class LongConditionFrame {
                public static bool Check(bool input) {
                    Contract.Requires(input && /*{{comment}}*/ input);
                    return input;
                }
            }
            """;
        var context = CreateDirectContext(
            "InvocationMaterializationLongDiagnostic",
            source,
            "LongConditionFrame",
            "Check");
        var publicContracts = RequirePublicContracts(context);
        var candidates = RecognizedCandidates(context.Body, context.Api);
        var candidate = candidates.Single();
        var argument = candidate.Invocation.Arguments.Single().Value;

        Assert.That(comment.Length, Is.GreaterThan(2048));
        Assert.That(argument.Syntax.Span.Length, Is.GreaterThan(2048));
        Assert.That(argument.Type?.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        var result = ContractInvocationMaterialization.Bind(
            context.Factory,
            context.ExpressionBinder,
            candidates,
            publicContracts.UsesCompanion,
            requiresOnly: false);

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(result.Clauses, Has.Length.EqualTo(1));
        Assert.That(result.Clauses[0].DiagnosticText,
            Is.EqualTo("[condition exceeds the display limit]"));
    }

    [Test]
    public void RequiredFactoryBinderAndInvocationArgumentsRejectNull()
    {
        var context = CreateDirectContext(
            "InvocationMaterializationNullArguments",
            BooleanSource,
            "BooleanFrames",
            "Evaluate");
        var publicContracts = RequirePublicContracts(context);
        var candidates = RecognizedCandidates(context.Body, context.Api);

        Assert.Throws<ArgumentNullException>(new Action(() =>
            ContractInvocationMaterialization.Bind(
                null!, context.ExpressionBinder, candidates,
                publicContracts.UsesCompanion, requiresOnly: false)));
        Assert.Throws<ArgumentNullException>(new Action(() =>
            ContractInvocationMaterialization.Bind(
                context.Factory, null!, candidates,
                publicContracts.UsesCompanion, requiresOnly: false)));
        Assert.Throws<ArgumentNullException>(new Action(() =>
            _ = new ContractInvocationCandidate(
                BoundContractKind.Requires, (IInvocationOperation)null!)));
    }

    private static DirectContext CreateDirectContext(
        string assemblyName,
        string sourceText,
        string typeName,
        string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(assemblyName);
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(sourceText);
        System.ArgumentNullException.ThrowIfNull(typeName);
        var compilation = CreateCompilation(assemblyName, sourceText);
        var tree = compilation.SyntaxTrees.Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        var syntax = FindMethodSyntax(semanticModel, tree, typeName, methodName);
        var source = (IMethodSymbol)semanticModel.GetDeclaredSymbol(syntax)!;
        AssertOwnedMethod(source, compilation);
        var body = semanticModel.GetOperation(syntax)!;
        Assert.That(body, Is.Not.Null);
        Assert.That(body.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(body.Syntax.SyntaxTree, Is.SameAs(tree));
        var api = ContractApiSymbols.TryCreate(compilation);
        Assert.That(api, Is.Not.Null);

        var factory = new IrFactory();
        var publicBinding = new ContractBinder(compilation, factory).Bind(source);
        Assert.That(publicBinding.IsSuccess, Is.True, publicBinding.Failure.ToString());
        var canonicalization = new ContractCanonicalization(compilation, factory);
        var expressionBinder = new ContractExpressionBinder(
            factory,
            api!,
            source,
            canonicalization.CreateTypeSpecializer(source));
        return new DirectContext(
            compilation,
            tree,
            source,
            body,
            api!,
            factory,
            expressionBinder,
            publicBinding.Contracts,
            publicBinding.IsSuccess,
            publicBinding.Failure);
    }

    private static DirectContext CreateCompanionContext(
        string assemblyName = "InvocationMaterializationCompanion",
        string sourceText = CompanionSource,
        string targetTypeName = "ITarget",
        string sourceTypeName = "TargetContracts",
        bool requiresOnly = false)
    {
        System.ArgumentNullException.ThrowIfNull(assemblyName);
        System.ArgumentNullException.ThrowIfNull(sourceText);
        System.ArgumentNullException.ThrowIfNull(sourceTypeName);
        System.ArgumentNullException.ThrowIfNull(targetTypeName);
        var compilation = CreateCompilation(assemblyName, sourceText);
        var tree = compilation.SyntaxTrees.Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        var sourceSyntax = FindMethodSyntax(semanticModel, tree, sourceTypeName, "Check");
        var source = (IMethodSymbol)semanticModel.GetDeclaredSymbol(sourceSyntax)!;
        var target = GetMethod(compilation, targetTypeName, "Check");
        AssertOwnedMethod(source, compilation);
        AssertOwnedMethod(target, compilation);
        var body = semanticModel.GetOperation(sourceSyntax)!;
        Assert.That(body, Is.Not.Null);
        Assert.That(body.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(body.Syntax.SyntaxTree, Is.SameAs(tree));
        var api = ContractApiSymbols.TryCreate(compilation);
        Assert.That(api, Is.Not.Null);

        var factory = new IrFactory();
        var binder = new ContractBinder(compilation, factory);
        var publicBinding = requiresOnly
            ? binder.BindRequires(target)
            : binder.Bind(target);
        Assert.That(publicBinding.IsSuccess, Is.True, publicBinding.Failure.ToString());
        Assert.That(publicBinding.Contracts, Is.Not.Null);
        Assert.That(publicBinding.Contracts!.UsesCompanion, Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            publicBinding.Contracts.Source, source), Is.True);
        var canonicalization = new ContractCanonicalization(compilation, factory);
        var expressionBinder = new ContractExpressionBinder(
            factory,
            api!,
            source,
            canonicalization.CreateTypeSpecializer(source));
        return new DirectContext(
            compilation,
            tree,
            source,
            body,
            api!,
            factory,
            expressionBinder,
            publicBinding.Contracts,
            publicBinding.IsSuccess,
            publicBinding.Failure);
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, string sourceText)
    {
        System.ArgumentNullException.ThrowIfNull(assemblyName);
        System.ArgumentNullException.ThrowIfNull(sourceText);
        var compilation = TestCompilation.Create(assemblyName, sourceText);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        return compilation;
    }

    private static MethodDeclarationSyntax FindMethodSyntax(
        SemanticModel semanticModel,
        SyntaxTree tree,
        string typeName,
        string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(semanticModel);
        System.ArgumentNullException.ThrowIfNull(tree);
        System.ArgumentNullException.ThrowIfNull(typeName);
        return tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : (syntax.Identifier.ValueText == methodName &&
                ((IMethodSymbol)semanticModel.GetDeclaredSymbol(syntax)!).ContainingType.Name == typeName));
    }

    private static IMethodSymbol GetMethod(
        CSharpCompilation compilation,
        string typeName,
        string methodName)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(typeName);
        return compilation.GetTypeByMetadataName(typeName)!
            .GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Single();
    }

    private static void AssertOwnedMethod(IMethodSymbol method, CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(method);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            method.ContainingAssembly, compilation.Assembly), Is.True);
    }

    private static BoundMethodContracts RequirePublicContracts(DirectContext context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        Assert.That(context.PublicIsSuccess, Is.True, context.PublicFailure.ToString());
        Assert.That(context.PublicContracts, Is.Not.Null);
        return context.PublicContracts!;
    }

    private static ImmutableArray<ContractInvocationCandidate> RecognizedCandidates(
        IOperation body,
        ContractApiSymbols api)
    {
        System.ArgumentNullException.ThrowIfNull(api);
        System.ArgumentNullException.ThrowIfNull(body);
        var candidates = ImmutableArray.CreateBuilder<ContractInvocationCandidate>();
        foreach (var invocation in body.DescendantsAndSelf().OfType<IInvocationOperation>())
        {
            var kind = api.Clauses.GetClauseKind(invocation.TargetMethod);
            if (kind.HasValue)
            {
                candidates.Add(new ContractInvocationCandidate(kind.Value, invocation));
            }
        }

        return [.. candidates.OrderBy(candidate => ((candidate.Invocation is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(candidate)) : (candidate.Invocation.Syntax.SpanStart)))];
    }

    private static Dictionary<IrVarId, IrValue> CreatePairValues(
        DirectContext context,
        bool left,
        bool right,
        bool? oldLeft = null,
        bool? oldRight = null,
        bool? result = null)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        var values = new Dictionary<IrVarId, IrValue>();
        AddParameterValue(context, values, "left", left);
        AddParameterValue(context, values, "right", right);
        if (oldLeft.HasValue)
        {
            AddPreStateValue(context, values, "left", oldLeft.Value);
        }
        if (oldRight.HasValue)
        {
            AddPreStateValue(context, values, "right", oldRight.Value);
        }
        if (result.HasValue)
        {
            AddResultValue(context, values, result.Value);
        }

        return values;
    }

    private static Dictionary<IrVarId, IrValue> CreateSingleValue(
        DirectContext context,
        bool value,
        bool? oldValue = null,
        bool? result = null)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        var values = new Dictionary<IrVarId, IrValue>();
        AddParameterValue(context, values, "value", value);
        if (oldValue.HasValue)
        {
            AddPreStateValue(context, values, "value", oldValue.Value);
        }
        if (result.HasValue)
        {
            AddResultValue(context, values, result.Value);
        }

        return values;
    }

    private static void AddParameterValue(
        DirectContext context,
        Dictionary<IrVarId, IrValue> values,
        string name,
        bool value)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        System.ArgumentNullException.ThrowIfNull(name);
        System.ArgumentNullException.ThrowIfNull(values);
        var binding = context.ExpressionBinder.VariableBindings.Single(candidate =>
            candidate.Symbol.Name == name);
        values[binding.Variable] = context.Factory.CreateBooleanValue(value);
    }

    private static void AddPreStateValue(
        DirectContext context,
        Dictionary<IrVarId, IrValue> values,
        string parameterName,
        bool value)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        System.ArgumentNullException.ThrowIfNull(parameterName);
        System.ArgumentNullException.ThrowIfNull(values);
        var current = context.ExpressionBinder.VariableBindings.Single(candidate =>
            candidate.Symbol.Name == parameterName).Variable;
        Assert.That(context.ExpressionBinder.PreStateVariables.TryGetValue(current, out var previous),
            Is.True);
        values[previous] = context.Factory.CreateBooleanValue(value);
    }

    private static void AddResultValue(
        DirectContext context,
        Dictionary<IrVarId, IrValue> values,
        bool value)
    {
        System.ArgumentNullException.ThrowIfNull(values);
        Assert.That(context.ExpressionBinder.ResultVariable.HasValue, Is.True);
        var result = context.ExpressionBinder.ResultVariable ?? throw new AssertionException("The genuine Ensures candidate must allocate its result slot.");
        values[result] =
            context.Factory.CreateBooleanValue(value);
    }

    private static string DescribeOperation(IrFactory factory, BoundContractClause clause)
    {
        System.ArgumentNullException.ThrowIfNull(clause);
        System.ArgumentNullException.ThrowIfNull(factory);
        var description = factory.GetOperationInfo(clause.SourceOperation).Description;
        Assert.That(description.HasValue, Is.True);
        return factory.GetString(description!.Value);
    }

    private sealed record DirectContext(
        CSharpCompilation Compilation,
        SyntaxTree Tree,
        IMethodSymbol Method,
        IOperation Body,
        ContractApiSymbols Api,
        IrFactory Factory,
        ContractExpressionBinder ExpressionBinder,
        BoundMethodContracts? PublicContracts,
        bool PublicIsSuccess,
        ContractBindingFailure PublicFailure)
    {
        public CSharpCompilation Compilation { get; init; } = Compilation ?? throw new System.ArgumentNullException(nameof(Compilation));
        public SyntaxTree Tree { get; init; } = Tree ?? throw new System.ArgumentNullException(nameof(Tree));
        public IMethodSymbol Method { get; init; } = Method ?? throw new System.ArgumentNullException(nameof(Method));
        public IOperation Body { get; init; } = Body ?? throw new System.ArgumentNullException(nameof(Body));
        public ContractApiSymbols Api { get; init; } = Api ?? throw new System.ArgumentNullException(nameof(Api));
        public IrFactory Factory { get; init; } = Factory ?? throw new System.ArgumentNullException(nameof(Factory));
        public ContractExpressionBinder ExpressionBinder { get; init; } = ExpressionBinder ?? throw new System.ArgumentNullException(nameof(ExpressionBinder));
    }
}
