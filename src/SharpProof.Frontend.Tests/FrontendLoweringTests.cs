using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
internal sealed class FrontendLoweringTests
{

    [TestCase(4L, true, 2L, ExpectedResult = 18L)]
    [TestCase(-7L, false, 3L, ExpectedResult = -2L)]
    public object? SupportedExpressionsMatchCompiledCSharp(
        long value,
        bool enabled,
        long divisor)
    {
        using var compiled = CompiledMethod.Create(
            """
            public static long Target(long value, bool enabled, long divisor) =>
                enabled ? checked((value + 2L) * 3L) : value / divisor;
            """);
        return compiled.CompareWithInterpreter([value, enabled, divisor]);
    }

    [Test]
    public void BooleanNullAndStringExpressionsMatchCompiledCSharp()
    {
        using var boolean = CompiledMethod.Create(
            """
            public static bool Target(bool enabled, long value, string text) =>
                enabled && value > 0L && text != null;
            """);
        Assert.That(
            boolean.CompareWithInterpreter([true, 5L, "ok"]),
            Is.EqualTo(true));
        Assert.That(
            boolean.CompareWithInterpreter([false, 5L, null]),
            Is.EqualTo(false));

        using var concatenation = CompiledMethod.Create(
            """
            public static string Target(string text) => text + "proof";
            """);
        Assert.That(
            concatenation.CompareWithInterpreter([(object?)null]),
            Is.EqualTo("proof"));

        using var length = CompiledMethod.Create(
            """
            public static long Target(string text) => (long)text.Length;
            """);
        Assert.That(
            length.CompareWithInterpreter(["sharp"]),
            Is.EqualTo(5L));

        using var nullable = CompiledMethod.Create(
            """
            public static long Target(string? text) =>
                text == null ? 0L : text.Length;
            """);
        Assert.That(
            nullable.CompareWithInterpreter([(object?)null]),
            Is.EqualTo(0L));
        Assert.That(
            nullable.CompareWithInterpreter(["proof"]),
            Is.EqualTo(5L));
    }

    [TestCaseSource(nameof(ClassificationCases))]
    public void ClassificationCasesRemainClosedAndExact(
        string members,
        FrontendSubsetDecision decision,
        FrontendAbstention abstention)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        AssertClassification(members, decision, abstention);
    }

    [Test]
    public void InterpreterPreservesLeftToRightAndShortCircuitEvaluation()
    {
        using var order = CompiledMethod.Create(
            """
            public static long Target(long left, long firstDivisor, long right, long secondDivisor) =>
                checked(left / firstDivisor + right / secondDivisor);
            """);
        var loweredOrder = order.Lower();
        var environment = order.CreateEnvironment(
            loweredOrder,
            [10L,
            0L,
            long.MinValue,
            -1L]);
        // The Total lowerer keeps faults in the safe condition.
        var orderResult = new IrInterpreter(order.Factory)
            .Evaluate(loweredOrder.SafeCondition, environment);
        Assert.That(orderResult.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(orderResult.Value!.Boolean, Is.False);

        using var shortCircuit = CompiledMethod.Create(
            """
            public static bool Target(bool enabled, long divisor) =>
                enabled && 10L / divisor > 0L;
            """);
        Assert.That(
            shortCircuit.CompareWithInterpreter([false, 0L]),
            Is.EqualTo(false));
    }

    [Test]
    public void DefaultAndUnknownSubsetDecisionsCannotBecomeExact()
    {
        var classification = default(FrontendSubsetClassification);

        Assert.That(classification.IsExact, Is.False);
        Assert.Throws<ArgumentException>((Action)(() =>
        {
            _ = new FrontendSubsetClassification(
                FrontendSubsetDecision.Unspecified,
                FrontendAbstention.None);
        }));
        Assert.Throws<ArgumentException>((Action)(() =>
        {
            _ = new FrontendSubsetClassification(
                (FrontendSubsetDecision)int.MaxValue,
                FrontendAbstention.UnsupportedType);
        }));
    }

    [Test]
    public void SupportedUnsignedComparisonsRetainTheirNumericMeaning()
    {
        using var compiled = CompiledMethod.Create(
            """
            public static bool Target(uint left, uint right) => left > right;
            """);

        Assert.That(
            compiled.CompareWithInterpreter([uint.MaxValue, uint.MinValue]),
            Is.EqualTo(true));
        Assert.That(
            compiled.CompareWithInterpreter([uint.MinValue, uint.MaxValue]),
            Is.EqualTo(false));
    }

    [Test]
    public void NamedOptionalAndExtensionInvocationsCloseTheSubset()
    {
        using var named = CompiledMethod.Create(
            """
            private static long Add(long first, long second = 7L) => first + second;
            public static long Target(long value) => Add(second: value, first: 3L);
            """);
        AssertOpaque(
            named.Lower(),
            IrOpaquePurity.Impure,
            FrontendAbstention.UnsupportedInvocationShape);

        using var optional = CompiledMethod.Create(
            """
            private static long Add(long first, long second = 7L) => first + second;
            public static long Target(long value) => Add(value);
            """);
        AssertOpaque(
            optional.Lower(),
            IrOpaquePurity.Impure,
            FrontendAbstention.UnsupportedInvocationShape);

        using var extension = CompiledMethod.Create(
            """
            private static long Twice(this long value) => value * 2L;
            public static long Target(long value) => value.Twice();
            """);
        AssertOpaque(
            extension.Lower(),
            IrOpaquePurity.Impure,
            FrontendAbstention.UnsupportedInvocationShape);
    }

    [Test]
    public void DocumentationReferenceIdsCoverCompilerIdentityCallShapes()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            #nullable enable
            public sealed class Subject<T> {
                private int _value;
                public ref int Value => ref _value;
                public U Echo<U>(ref U value, U[] values) => value;
                public static Subject<T> operator +(Subject<T> left, Subject<T> right) => left;
                public void Overload(int value) { }
                public void Overload(ref int value) { }
                public object Anonymous() => new { Value = 1 };
                public void Nullable(string? optional, string required) {
                    static int Local(int[] values) => values.Length;
                    _ = Local([]);
                }
            }
            """,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create(
            "Identity.Shapes",
            [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .ToImmutableArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));

        var subject = compilation.GetTypeByMetadataName("Subject`1")!;
        var echo = subject.GetMembers("Echo").OfType<IMethodSymbol>().Single();
        var value = subject.GetMembers("Value").OfType<IPropertySymbol>().Single();
        var addition = subject.GetMembers("op_Addition").OfType<IMethodSymbol>().Single();
        var nullable = subject.GetMembers("Nullable").OfType<IMethodSymbol>().Single();
        var overloads = subject.GetMembers("Overload")
            .OfType<IMethodSymbol>()
            .OrderBy(static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Parameters[0].RefKind))
            .ToImmutableArray();
        var local = tree.GetRoot().DescendantNodes()
            .OfType<LocalFunctionStatementSyntax>()
            .Select(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (compilation.GetSemanticModel(tree).GetDeclaredSymbol(node)))
            .Single()!;
        var anonymousType = tree.GetRoot().DescendantNodes()
            .OfType<AnonymousObjectCreationExpressionSyntax>()
            .Select(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (compilation.GetSemanticModel(tree).GetTypeInfo(node).Type))
            .Single()!;
        var constructed = subject.Construct(compilation.GetSpecialType(SpecialType.System_String));
        ImmutableArray<ITypeSymbol> types = [
            subject,
            constructed,
            subject.TypeParameters[0],
            compilation.CreateArrayTypeSymbol(subject.TypeParameters[0]),
            compilation.CreateArrayTypeSymbol(
                compilation.GetSpecialType(SpecialType.System_Int32),
                rank: 2),
            compilation.CreatePointerTypeSymbol(
                compilation.GetSpecialType(SpecialType.System_Int32)),
            nullable.Parameters[0].Type,
            nullable.Parameters[1].Type
        ];

        foreach (var symbol in ImmutableArray.Create<ISymbol>(echo, value, addition))
        {
            Assert.That(
                DocumentationCommentId.CreateDeclarationId(symbol),
                Is.Not.Null.And.Not.Empty,
                symbol.MetadataName);
        }

        foreach (var type in types)
        {
            Assert.That(
                DocumentationCommentId.CreateReferenceId(type),
                Is.Not.Null.And.Not.Empty,
                type.MetadataName);
        }

        Assert.That(
            DocumentationCommentId.CreateDeclarationId(local),
            Is.Not.Null.And.Not.Empty);
        Assert.That(
            DocumentationCommentId.CreateReferenceId(anonymousType),
            Is.Null.Or.Empty,
            "Anonymous types require an explicit stable fallback.");
        Assert.That(
            CompilerIdentityBridge.CreateSymbolDisplay(overloads[0]),
            Is.Not.EqualTo(CompilerIdentityBridge.CreateSymbolDisplay(overloads[1])));
        Assert.That(
            CompilerIdentityBridge.CreateTypeDisplay(types[3]),
            Is.Not.EqualTo(CompilerIdentityBridge.CreateTypeDisplay(types[4])));
        Assert.That(
            SymbolEqualityComparer.Default.Equals(
                nullable.Parameters[0].Type,
                nullable.Parameters[1].Type),
            Is.True);
        Assert.That(
            CompilerIdentityBridge.CreateTypeDisplay(nullable.Parameters[0].Type),
            Is.EqualTo(CompilerIdentityBridge.CreateTypeDisplay(nullable.Parameters[1].Type)),
            "Display identity follows the compiler-symbol comparer used for interning.");
        var fallback = CompilerIdentityBridge.CreateTypeDisplay(anonymousType);
        var equivalentCompilation = CSharpCompilation.Create(
            "Identity.Shapes",
            [tree],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var equivalentAnonymousType = tree.GetRoot().DescendantNodes()
            .OfType<AnonymousObjectCreationExpressionSyntax>()
            .Select(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (equivalentCompilation.GetSemanticModel(tree).GetTypeInfo(node).Type))
            .Single()!;
        Assert.That(
            fallback,
            Is.EqualTo(CompilerIdentityBridge.CreateTypeDisplay(
                equivalentAnonymousType)));
        Assert.That(
            fallback,
            Does.StartWith(compilation.Assembly.Identity + "::"));
    }

    [Test]
    public void FieldReferencesAreAdmittedToContractExpressionStage()
    {
        Assert.That(
            CSharpOperationSemantics.IsSupported(
                OperationSupportStage.ContractExpressionLowering,
                OperationKind.FieldReference),
            Is.True);
    }

    [Test]
    public void EffectDiscoveryAdmitsCaughtExceptionsAndInterpolatedStrings()
    {
        Assert.That(
            CSharpOperationSemantics.IsSupported(
                OperationSupportStage.EffectDiscovery,
                OperationKind.CaughtException),
            Is.True);
        Assert.That(
            CSharpOperationSemantics.IsSupported(
                OperationSupportStage.EffectDiscovery,
                OperationKind.InterpolatedString),
            Is.True);
    }

    [Test]
    public void DeepExactExpressionAbstainsBeforeRecursiveLoweringExhaustsTheStack()
    {
        var expression = string.Join(
            " + ",
            Enumerable.Repeat("1L", 300).Prepend("value"));
        using var compiled = CompiledMethod.Create(
            "public static long Target(long value) => checked(" +
            expression +
            ");");

        var result = compiled.Lower();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Classification.Decision,
                Is.EqualTo(FrontendSubsetDecision.ClosedAbstention));
            Assert.That(
                result.Classification.Abstention,
                Is.EqualTo(FrontendAbstention.ExpressionDepthLimit));
            Assert.That(result.Term, Is.TypeOf<IrOpaqueTerm>());
        }
    }

    private static System.Collections.Generic.IEnumerable<TestCaseData> ClassificationCases()
    {
        yield return new TestCaseData(
            """
            public static long Target(long value) => checked(value + 1L);
            """,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None)
            .SetName("OverflowAndConversionShapes_CheckedLongAddition");
        yield return new TestCaseData(
            """
            public static long Target(int value) => value;
            """,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None)
            .SetName("OverflowAndConversionShapes_WideningIntToLong");
        yield return new TestCaseData(
            """
            public static bool Target(double left, double right) => left == right;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedType)
            .SetName("UnsupportedValueDomains_Double");
        yield return new TestCaseData(
            """
            public abstract class Base {}
            public static bool Target(Base left, Base right) => left == right;
            """,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None)
            .SetName("AbstractAndInterfaceReferenceEquality_AbstractClass");
        yield return new TestCaseData(
            """
            public interface IItem {}
            public static bool Target(IItem left, IItem right) => left == right;
            """,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None)
            .SetName("AbstractAndInterfaceReferenceEquality_Interface");
        yield return new TestCaseData(
            """
            public static bool Target(object left, string right) =>
                left == right;
            """,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None)
            .SetName("AssignableReferenceEquality_ObjectAndString");
        yield return new TestCaseData(
            """
            public class Base {}
            public sealed class Derived : Base {}
            public static bool Target(Base left, Derived right) =>
                left != right;
            """,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None)
            .SetName("AssignableReferenceEquality_BaseAndDerived");
        yield return new TestCaseData(
            """
            public static long? Target() => (long?)1L;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedType)
            .SetName("NullableAndEnumConstants_NullableLong");
        yield return new TestCaseData(
            """
            public static string Target() => nameof(Subject);
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedOperationKind)
            .SetName("ConstantFolding_Nameof");
        yield return new TestCaseData(
            """
            public static int Target() => sizeof(int);
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedOperationKind)
            .SetName("ConstantFolding_Sizeof");
        yield return new TestCaseData(
            """
            public static string Target() => $"proof";
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedOperationKind)
            .SetName("ConstantFolding_InterpolatedString");
        yield return new TestCaseData(
            """
            public static string Target() =>
                true ? nameof(Subject) : "other";
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedOperationKind)
            .SetName("ConstantFolding_Conditional");
        yield return new TestCaseData(
            """
            public static bool Target(string input) =>
                nameof(Subject) == input;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedOperationKind)
            .SetName("ConstantFolding_NameofComparison");
        yield return new TestCaseData(
            """
            public static long Target() => long.MinValue;
            """,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None)
            .SetName("CatalogIntegerBoundaries_LongMinValue");
        yield return new TestCaseData(
            """
            public static int Target() => int.MaxValue;
            """,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None)
            .SetName("CatalogIntegerBoundaries_IntMaxValue");
        yield return new TestCaseData(
            """
            public static long Target() => -1L;
            """,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None)
            .SetName("NegativeIntegerLiterals_LongNegativeOne");
    }

    private static void AssertClassification(
        string members,
        FrontendSubsetDecision decision,
        FrontendAbstention abstention)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        using var compiled = CompiledMethod.Create(members);
        var result = compiled.Lower();
        Assert.That(
            result.Classification.Decision,
            Is.EqualTo(decision),
            result.Classification.Abstention.ToString());
        Assert.That(result.Classification.Abstention, Is.EqualTo(abstention));
    }

    private static void AssertOpaque(
        SharpProof.Fuzz.FrontendTotalLoweringResult result,
        IrOpaquePurity purity,
        FrontendAbstention abstention)
    {
        System.ArgumentNullException.ThrowIfNull(result);
        Assert.That(result.Term, Is.TypeOf<IrOpaqueTerm>());
        Assert.That(((IrOpaqueTerm)result.Term).Purity, Is.EqualTo(purity));
        Assert.That(result.Classification.Decision, Is.EqualTo(
            FrontendSubsetDecision.ClosedAbstention));
        Assert.That(result.Classification.Abstention, Is.EqualTo(abstention));
    }

    private static int MeasureOperationDepth(IOperation operation, int depth)
    {
        System.ArgumentNullException.ThrowIfNull(operation);
        var children = operation.ChildOperations.ToImmutableArray();
        return children.Length == 0
            ? depth
            : children.Max(child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (MeasureOperationDepth(child, depth + 1)));
    }
    private sealed class CompiledMethod : IDisposable
    {
        private readonly AssemblyLoadContextHandle _assembly;
        private readonly MethodInfo? _method;
        private readonly IMethodSymbol _methodSymbol;

        private CompiledMethod(
            AssemblyLoadContextHandle assembly,
            MethodInfo? method,
            IMethodSymbol methodSymbol,
            IOperation targetRoot,
            IOperation targetExpression)
        {
            System.ArgumentNullException.ThrowIfNull(assembly);
            System.ArgumentNullException.ThrowIfNull(methodSymbol);
            System.ArgumentNullException.ThrowIfNull(targetExpression);
            System.ArgumentNullException.ThrowIfNull(targetRoot);
            _assembly = assembly;
            _method = method;
            _methodSymbol = methodSymbol;
            TargetRoot = targetRoot;
            TargetExpression = targetExpression;
            Factory = new IrFactory(IrExecutionSemantics.Total);
        }

        internal IrFactory Factory
        {
            get;
        }
        internal IOperation TargetRoot
        {
            get;
        }
        internal IOperation TargetExpression
        {
            get;
        }

        internal static CompiledMethod Create(
            string members,
            bool returnExpressionOnly = true,
            bool allowUnsafe = false)
        {
            System.ArgumentNullException.ThrowIfNull(members);
            var source = FrontendTestHelpers.WrapSubjectMembers(members);
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp12));
            var compilation = CSharpCompilation.Create(
                "FrontendDifferential_" + Guid.NewGuid().ToString("N"),
                [syntaxTree],
                TestMetadataReferences.Platform,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    checkOverflow: false,
                    allowUnsafe: allowUnsafe,
                    nullableContextOptions: NullableContextOptions.Enable));
            var diagnostics = compilation.GetDiagnostics()
                .Where(static diagnostic =>
                    diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
                .ToImmutableArray();
            Assert.That(
                diagnostics,
                Is.Empty,
                string.Join(Environment.NewLine, diagnostics.Select(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.ToString()))));

            var model = compilation.GetSemanticModel(syntaxTree);
            var methodSyntax = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
            var methodSymbol = (IMethodSymbol)model.GetDeclaredSymbol(methodSyntax)!;
            var root = model.GetOperation(methodSyntax) ??
                       model.GetOperation(
                           (SyntaxNode?)methodSyntax.ExpressionBody?.Expression ??
                           methodSyntax.Body!) ??
                       throw new InvalidOperationException(
                           "Roslyn did not expose an operation root for Target.");
            var expression = returnExpressionOnly
                ? methodSyntax.ExpressionBody == null
                    ? FindReturnExpression(model, methodSyntax)
                    : GetExpressionOperation(
                        model,
                        methodSyntax.ExpressionBody.Expression)
                : root;

            using var stream = new MemoryStream();
            var emit = compilation.Emit(stream);
            Assert.That(
                emit.Success,
                Is.True,
                string.Join(Environment.NewLine, emit.Diagnostics));
            var assembly = AssemblyLoadContextHandle.Load(ImmutableArray.Create<byte>(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
            var method = assembly.Assembly
                .GetType("Subject")!
                .GetMethod(
                    "Target",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return new CompiledMethod(
                assembly,
                method,
                methodSymbol,
                root,
                expression);
        }

        internal object? Invoke(ImmutableArray<object?> arguments)
        {
            if (arguments.IsDefault)
            { throw new System.ArgumentNullException(nameof(arguments)); }
            return _method!.Invoke(null, arguments.ToArray());
        }

        internal SharpProof.Fuzz.FrontendTotalLoweringResult Lower()
        {
            return SharpProof.Fuzz.FrontendTotalLowering.Lower(Factory, TargetExpression);
        }

        internal object? CompareWithInterpreter(ImmutableArray<object?> arguments)
        {
            if (arguments.IsDefault)
            { throw new System.ArgumentNullException(nameof(arguments)); }
            var actual = _method!.Invoke(null, arguments.ToArray());
            var lowered = Lower();
            Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
            var environment = CreateEnvironment(lowered, arguments);
            var evaluated = new IrInterpreter(Factory).Evaluate(lowered.Term, environment);
            Assert.That(evaluated.Status, Is.EqualTo(IrEvaluationStatus.Value));
            var interpreted = evaluated.Value!.Kind switch
            {
                IrValueKind.Boolean => evaluated.Value.Boolean,
                IrValueKind.Integer => evaluated.Value.Integer,
                IrValueKind.String => evaluated.Value.String,
                IrValueKind.Null => null,
                _ => actual
            };
            Assert.That(interpreted, Is.EqualTo(actual));
            return interpreted;
        }

        internal ImmutableDictionary<IrVarId, IrValue> CreateEnvironment(
            SharpProof.Fuzz.FrontendTotalLoweringResult lowering,
             ImmutableArray<object?> arguments)
        {
            if (arguments.IsDefault)
            { throw new System.ArgumentNullException(nameof(arguments)); }
            System.ArgumentNullException.ThrowIfNull(lowering);
            var values = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
            foreach (var binding in lowering.Variables)
            {
                if (binding.Symbol is not IParameterSymbol parameter ||
                    !SymbolEqualityComparer.Default.Equals(
                        parameter.ContainingSymbol,
                        _methodSymbol))
                {
                    continue;
                }

                values.Add(
                    binding.Variable,
                    CreateValue(
                        Factory,
                        Factory.GetVariableInfo(binding.Variable).Type,
                        arguments[parameter.Ordinal]));
            }
            return values.ToImmutable();
        }

        public void Dispose()
        {
            _assembly.Dispose();
        }

        private static IOperation FindReturnExpression(
            SemanticModel model,
            MethodDeclarationSyntax method)
        {
            System.ArgumentNullException.ThrowIfNull(model);
            if (method.ExpressionBody != null)
            {
                return GetExpressionOperation(
                    model,
                    method.ExpressionBody.Expression);
            }

            var returnStatement = method.Body!
                .DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Last();
            return GetExpressionOperation(model, returnStatement.Expression!);
        }

        private static IOperation GetExpressionOperation(
            SemanticModel model,
            ExpressionSyntax expression)
        {
            return FrontendTestHelpers.TryGetExpressionOperation(
                    model,
                    expression) ??
                throw new InvalidOperationException(
                    "Roslyn did not expose an operation for expression kind " +
                    expression.Kind() + ".");
        }

        private static IrValue CreateValue(
            IrFactory factory,
            IrTypeId type,
            object? value)
        {
            if (type.IsDefault)
            {
                throw new System.ArgumentOutOfRangeException(nameof(type));
            }
            System.ArgumentNullException.ThrowIfNull(factory);
            var info = factory.GetTypeInfo(type);
            if (value == null)
            {
                return factory.CreateNullValue(type);
            }

            return info.Kind switch
            {
                IrTypeKind.Boolean => factory.CreateBooleanValue((bool)value),
                IrTypeKind.Integer => value is ulong unsigned
                    ? factory.CreateIntegerValue(type, unsigned)
                    : factory.CreateIntegerValue(type, Convert.ToInt64(value, CultureInfo.InvariantCulture)),
                IrTypeKind.String => factory.CreateStringValue((string)value),
                IrTypeKind.Sequence => factory.CreateSequenceValue(
                    type,
                    ((Array)value)
                        .Cast<object?>()
                        .Select(element => CreateValue(
                            factory,
                            info.ElementType!.Value,
                            element))),
                IrTypeKind.Reference => factory.CreateReferenceValue(type, value),
                _ => throw new InvalidOperationException(
                    "Unsupported test IR type: " + info.Kind + ".")
            };
        }
    }

    private sealed class AssemblyLoadContextHandle : IDisposable
    {
        private readonly System.Runtime.Loader.AssemblyLoadContext _context;

        private AssemblyLoadContextHandle(
            System.Runtime.Loader.AssemblyLoadContext context,
            Assembly assembly)
        {
            System.ArgumentNullException.ThrowIfNull(assembly);
            System.ArgumentNullException.ThrowIfNull(context);
            _context = context;
            Assembly = assembly;
        }

        internal Assembly Assembly
        {
            get;
        }

        internal static AssemblyLoadContextHandle Load(ImmutableArray<byte> image)
        {
            if (image.IsDefault)
            {
                throw new System.ArgumentException("Array must be initialized.", nameof(image));
            }
            var context = new System.Runtime.Loader.AssemblyLoadContext(
                "SharpProof.Frontend.Test." + Guid.NewGuid().ToString("N"),
                isCollectible: true);
            using var stream = new MemoryStream(image.ToArray(), writable: false);
            return new AssemblyLoadContextHandle(
                context,
                context.LoadFromStream(stream));
        }

        public void Dispose()
        {
            _context.Unload();
        }
    }

}
