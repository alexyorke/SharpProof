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
public sealed class FrontendLoweringTests {
    private static readonly long[] ElementValues = [4L, 8L, 15L];

    [TestCase(4L, true, 2L, ExpectedResult = 18L)]
    [TestCase(-7L, false, 3L, ExpectedResult = -2L)]
    public object? SupportedExpressionsMatchCompiledCSharp(
        long value,
        bool enabled,
        long divisor) {
        using var compiled = CompiledMethod.Create(
            """
            public static long Target(long value, bool enabled, long divisor) =>
                enabled ? checked((value + 2L) * 3L) : value / divisor;
            """);
        return compiled.CompareWithInterpreter(value, enabled, divisor);
    }

    [Test]
    public void BooleanNullAndStringExpressionsMatchCompiledCSharp() {
        using var boolean = CompiledMethod.Create(
            """
            public static bool Target(bool enabled, long value, string text) =>
                enabled && value > 0L && text != null;
            """);
        Assert.That(
            boolean.CompareWithInterpreter(true, 5L, "ok"),
            Is.EqualTo(true));
        Assert.That(
            boolean.CompareWithInterpreter(false, 5L, null),
            Is.EqualTo(false));

        using var concatenation = CompiledMethod.Create(
            """
            public static string Target(string text) => text + "proof";
            """);
        Assert.That(
            concatenation.CompareWithInterpreter((object?)null),
            Is.EqualTo("proof"));

        using var length = CompiledMethod.Create(
            """
            public static long Target(string text) => (long)text.Length;
            """);
        Assert.That(
            length.CompareWithInterpreter("sharp"),
            Is.EqualTo(5L));

        using var nullable = CompiledMethod.Create(
            """
            public static long Target(string? text) =>
                text == null ? 0L : text.Length;
            """);
        Assert.That(
            nullable.CompareWithInterpreter((object?)null),
            Is.EqualTo(0L));
        Assert.That(
            nullable.CompareWithInterpreter("proof"),
            Is.EqualTo(5L));
    }

    [Test]
    public void ArrayLengthAndElementAccessMatchCompiledCSharp() {
        using var element = CompiledMethod.Create(
            """
            public static long Target(long[] values, int index) =>
                checked(values[index] + values.LongLength);
            """);
        Assert.That(
            element.CompareWithInterpreter(ElementValues, 1),
            Is.EqualTo(11L));
    }

    [Test]
    public void InterpreterPreservesLeftToRightAndShortCircuitEvaluation() {
        using var order = CompiledMethod.Create(
            """
            public static long Target(long left, long firstDivisor, long right, long secondDivisor) =>
                checked(left / firstDivisor + right / secondDivisor);
            """);
        var loweredOrder = order.Lower();
        var environment = order.CreateEnvironment(
            loweredOrder,
            10L,
            0L,
            long.MinValue,
            -1L);
        var orderResult = new IrInterpreter(order.Factory)
            .Evaluate(loweredOrder.Term, environment);
        Assert.That(orderResult.Status, Is.EqualTo(IrEvaluationStatus.Exception));
        Assert.That(
            orderResult.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.DivideByZero));

        using var shortCircuit = CompiledMethod.Create(
            """
            public static bool Target(bool enabled, long divisor) =>
                enabled && 10L / divisor > 0L;
            """);
        Assert.That(
            shortCircuit.CompareWithInterpreter(false, 0L),
            Is.EqualTo(false));
    }

    [Test]
    public void OverflowAndConversionShapesAreExactOnlyWhenRepresentable() {
        AssertClassification(
            """
            public static long Target(long value) => checked(value + 1L);
            """,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None);
        AssertClassification(
            """
            public static long Target(long value) => unchecked(value + 1L);
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UncheckedOverflowSemantics);
        AssertClassification(
            """
            public static int Target(int value) => checked(value + 1);
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedType);
        AssertClassification(
            """
            public static int Target(int left, int right) => left / right;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedType);
        AssertClassification(
            """
            public static uint Target(uint left, uint right) => left % right;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedType);
        AssertClassification(
            """
            public static long Target(int value) => value;
            """,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None);
        AssertClassification(
            """
            public static int Target(long value) => checked((int)value);
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.ConversionMayChangeValue);
        AssertClassification(
            """
            public static string Target(object value) => (string)value;
            """,
            FrontendSubsetDecision.Exact,
            FrontendAbstention.None);
    }

    [Test]
    public void UnsupportedIntegralDomainsCannotMasqueradeAsReferenceEquality() {
        AssertClassification(
            """
            public static bool Target(ulong left, ulong right) => left == right;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedType);
        AssertClassification(
            """
            public static bool Target(nint left, nint right) => left == right;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedType);
        AssertClassification(
            """
            public static bool Target(nuint left, nuint right) => left == right;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedType);
    }

    [Test]
    public void UnsupportedValueDomainsCannotMasqueradeAsReferenceEquality() {
        AssertClassification(
            """
            public static bool Target(double left, double right) => left == right;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedType);
        AssertClassification(
            """
            public enum Choice {
                First,
                Second
            }
            public static bool Target(Choice left, Choice right) => left == right;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedType);
    }

    [Test]
    public void SupportedUnsignedComparisonsRetainTheirNumericMeaning() {
        using var compiled = CompiledMethod.Create(
            """
            public static bool Target(uint left, uint right) => left > right;
            """);

        Assert.That(
            compiled.CompareWithInterpreter(uint.MaxValue, uint.MinValue),
            Is.EqualTo(true));
        Assert.That(
            compiled.CompareWithInterpreter(uint.MinValue, uint.MaxValue),
            Is.EqualTo(false));
    }

    [Test]
    public void NullableAndEnumConstantsCannotBypassClosedTypeAbstention() {
        AssertClassification(
            """
            public static long? Target() => (long?)1L;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedType);
        AssertClassification(
            """
            public enum Choice {
                First = 1
            }
            public static Choice Target() => Choice.First;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.UnsupportedType);
    }

    [Test]
    public void LiftedUnaryOperatorsUseTheLiftedOperatorAbstention() =>
        AssertClassification(
            """
            public static long? Target(long? value) => -value;
            """,
            FrontendSubsetDecision.ClosedAbstention,
            FrontendAbstention.LiftedOperator);

    [Test]
    public void NamedOptionalAndExtensionInvocationsCloseTheSubset() {
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
    public void PureOpaqueIdentityIsStructuralAndImpureIdentityIsPerOccurrence() {
        using var pure = CompiledMethod.Create(
            """
            public static bool Target(long value) =>
                (value & 1L) == (value & 1L);
            """);
        var pureOperations = pure.TargetExpression
            .DescendantsAndSelf()
            .OfType<IBinaryOperation>()
            .Where(static operation =>
                operation.OperatorKind == BinaryOperatorKind.And)
            .ToArray();
        Assert.That(pureOperations, Has.Length.EqualTo(2));
        var pureLowerer = new RoslynOperationLowerer(pure.Factory);
        var firstPure = pureLowerer.Lower(pureOperations[0]);
        var secondPure = pureLowerer.Lower(pureOperations[1]);
        Assert.That(firstPure.Term, Is.TypeOf<IrOpaqueTerm>());
        Assert.That(((IrOpaqueTerm)firstPure.Term).Purity, Is.EqualTo(IrOpaquePurity.Pure));
        Assert.That(secondPure.Term, Is.SameAs(firstPure.Term));

        using var impure = CompiledMethod.Create(
            """
            private static long Next() => 1L;
            public static bool Target() => Next() == Next();
            """);
        var invocations = impure.TargetExpression
            .DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .ToArray();
        Assert.That(invocations, Has.Length.EqualTo(2));
        var impureLowerer = new RoslynOperationLowerer(impure.Factory);
        var firstImpure = impureLowerer.Lower(invocations[0]);
        var secondImpure = impureLowerer.Lower(invocations[1]);
        Assert.That(((IrOpaqueTerm)firstImpure.Term).Purity, Is.EqualTo(IrOpaquePurity.Impure));
        Assert.That(secondImpure.Term, Is.Not.SameAs(firstImpure.Term));
        Assert.That(secondImpure.Term.Id, Is.Not.EqualTo(firstImpure.Term.Id));

        var knownPureLowerer = new RoslynOperationLowerer(
            impure.Factory,
            static method => method.Name == "Next");
        var firstKnownPure = knownPureLowerer.Lower(invocations[0]);
        var secondKnownPure = knownPureLowerer.Lower(invocations[1]);
        Assert.That(
            ((IrOpaqueTerm)firstKnownPure.Term).Purity,
            Is.EqualTo(IrOpaquePurity.Pure));
        Assert.That(secondKnownPure.Term, Is.SameAs(firstKnownPure.Term));
    }

    [Test]
    public void PureOpaqueIdentitySeparatesDifferentOperatorSemantics() {
        using var compiled = CompiledMethod.Create(
            """
            public static bool Target(long value) =>
                (value & 1L) == (value | 1L);
            """);
        var operations = compiled.TargetExpression
            .DescendantsAndSelf()
            .OfType<IBinaryOperation>()
            .Where(static operation =>
                operation.OperatorKind is
                    BinaryOperatorKind.And or
                    BinaryOperatorKind.Or)
            .ToArray();
        Assert.That(operations, Has.Length.EqualTo(2));
        var lowerer = new RoslynOperationLowerer(compiled.Factory);

        var first = lowerer.Lower(operations[0]).Term;
        var second = lowerer.Lower(operations[1]).Term;

        Assert.That(first, Is.TypeOf<IrOpaqueTerm>());
        Assert.That(second, Is.TypeOf<IrOpaqueTerm>());
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.Id, Is.Not.EqualTo(first.Id));
    }

    [Test]
    public void CompilerIdentitySeparatesSameNamedTypesFromDifferentAssemblies() {
        var leftReference = CreateAliasedTypeReference(
            "Collision.Left",
            "left");
        var rightReference = CreateAliasedTypeReference(
            "Collision.Right",
            "right");
        var tree = CSharpSyntaxTree.ParseText(
            """
            extern alias left;
            extern alias right;
            public static class Subject {
                public static bool Target(
                    left::Collision.Widget first,
                    right::Collision.Widget second) =>
                    first is null || second is null;
            }
            """,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create(
            "Collision.Consumer",
            [tree],
            PlatformReferences.Add(leftReference).Add(rightReference),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic => diagnostic.ToString())));

        var model = compilation.GetSemanticModel(tree);
        var references = tree.GetRoot()
            .DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(node => model.GetOperation(node))
            .OfType<IParameterReferenceOperation>()
            .OrderBy(static operation => operation.Parameter.Ordinal)
            .ToArray();
        Assert.That(references, Has.Length.EqualTo(2));

        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        var first = lowerer.Lower(references[0]);
        var second = lowerer.Lower(references[1]);

        Assert.That(first.Term.Type, Is.Not.EqualTo(second.Term.Type));
        Assert.That(
            CompilerIdentityBridge.InternType(
                factory,
                references[0].Parameter.Type),
            Is.Not.EqualTo(
                CompilerIdentityBridge.InternType(
                    factory,
                    references[1].Parameter.Type)));
    }

    [Test]
    public void InheritedInstanceInvocationsLowerTotallyWithTypedReceivers() {
        using var compiled = CompiledMethod.Create(
            """
            private class Base {
                public long Read() => 1L;
            }
            private sealed class Derived : Base {
            }
            private static long Target(Derived value) => value.Read();
            """);

        var result = compiled.Lower();

        AssertOpaque(
            result,
            IrOpaquePurity.Impure,
            FrontendAbstention.UnsupportedInvocationShape);
        var opaque = (IrOpaqueTerm)result.Term;
        Assert.That(
            compiled.Factory.GetMemberInfo(opaque.Member).DeclaringType,
            Is.EqualTo(opaque.Receiver!.Type));
    }

    [Test]
    public void EveryOperationInUnsupportedProgramLowersWithoutThrowing() {
        using var compiled = CompiledMethod.Create(
            """
            private sealed class Box {
                public long Value { get; set; }
            }
            public static async System.Threading.Tasks.Task<long> Target(long value) {
                var box = new Box { Value = value };
                await System.Threading.Tasks.Task.Yield();
                System.Func<long> read = () => box.Value;
                return read();
            }
            """,
            returnExpressionOnly: false);
        var lowerer = new RoslynOperationLowerer(compiled.Factory);
        var operations = compiled.TargetRoot.DescendantsAndSelf().ToArray();
        Assert.That(operations.Length, Is.GreaterThan(8));
        foreach (var operation in operations) {
            FrontendLoweringResult? result = null;
            Assert.DoesNotThrow(
                (Action)(() => result = lowerer.Lower(operation)));
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Term, Is.Not.Null);
            if (!result.IsExact)
                Assert.That(
                    result.Classification.Abstention,
                    Is.Not.EqualTo(FrontendAbstention.None));
        }
    }

    [Test]
    public void OperationKindClassifierSnapshotIsClosedAndExhaustive() {
        var kinds = OperationSubsetClassifier.GetKnownOperationKinds();
        Assert.That(kinds, Is.Not.Empty);
        Assert.That(kinds, Is.Ordered);
        Assert.That(kinds.Distinct().Count(), Is.EqualTo(kinds.Length));
        foreach (var kind in kinds) {
            var classification = OperationSubsetClassifier.Classify(kind);
            Assert.That(
                classification.Decision,
                Is.AnyOf(
                    FrontendSubsetDecision.Exact,
                    FrontendSubsetDecision.ClosedAbstention));
            Assert.That(
                classification.IsExact
                    ? classification.Abstention == FrontendAbstention.None
                    : classification.Abstention != FrontendAbstention.None,
                Is.True,
                kind.ToString());
        }
        Assert.That(
            OperationSubsetClassifier.Classify((OperationKind)int.MaxValue).Abstention,
            Is.EqualTo(FrontendAbstention.UnknownOperationKind));
        var snapshot = OperationSubsetClassifier.CreateSnapshot();
        Assert.That(snapshot.Split('\n'), Has.Length.GreaterThan(100));
        Assert.That(snapshot, Does.Contain("|Invocation|ClosedAbstention|"));
        Assert.That(snapshot, Does.Contain("|Literal|Exact|None"));
        var snapshotHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(snapshot)));
        Assert.That(
            snapshotHash,
            Is.EqualTo(
                "4C2849F3D16A580C09BBB46C9526EBC1404405C9FA54A4056D631269AE2BC736"));
    }

    private static void AssertClassification(
        string members,
        FrontendSubsetDecision decision,
        FrontendAbstention abstention) {
        using var compiled = CompiledMethod.Create(members);
        var result = compiled.Lower();
        Assert.That(result.Classification.Decision, Is.EqualTo(decision));
        Assert.That(result.Classification.Abstention, Is.EqualTo(abstention));
    }

    private static void AssertOpaque(
        FrontendLoweringResult result,
        IrOpaquePurity purity,
        FrontendAbstention abstention) {
        Assert.That(result.Term, Is.TypeOf<IrOpaqueTerm>());
        Assert.That(((IrOpaqueTerm)result.Term).Purity, Is.EqualTo(purity));
        Assert.That(result.Classification.Decision, Is.EqualTo(
            FrontendSubsetDecision.ClosedAbstention));
        Assert.That(result.Classification.Abstention, Is.EqualTo(abstention));
    }

    private sealed class CompiledMethod : IDisposable {
        private readonly AssemblyLoadContextHandle _assembly;
        private readonly MethodInfo? _method;
        private readonly IMethodSymbol _methodSymbol;

        private CompiledMethod(
            AssemblyLoadContextHandle assembly,
            MethodInfo? method,
            IMethodSymbol methodSymbol,
            IOperation targetRoot,
            IOperation targetExpression) {
            _assembly = assembly;
            _method = method;
            _methodSymbol = methodSymbol;
            TargetRoot = targetRoot;
            TargetExpression = targetExpression;
            Factory = new IrFactory();
        }

        internal IrFactory Factory { get; }
        internal IOperation TargetRoot { get; }
        internal IOperation TargetExpression { get; }

        internal static CompiledMethod Create(
            string members,
            bool returnExpressionOnly = true) {
            var source =
                """
                #nullable enable
                public static class Subject {
                """ +
                Environment.NewLine +
                members +
                Environment.NewLine +
                "}";
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp12));
            var compilation = CSharpCompilation.Create(
                "FrontendDifferential_" + Guid.NewGuid().ToString("N"),
                [syntaxTree],
                PlatformReferences,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    checkOverflow: false,
                    nullableContextOptions: NullableContextOptions.Enable));
            var diagnostics = compilation.GetDiagnostics()
                .Where(static diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            Assert.That(
                diagnostics,
                Is.Empty,
                string.Join(Environment.NewLine, diagnostics.Select(static value => value.ToString())));

            var model = compilation.GetSemanticModel(syntaxTree);
            var methodSyntax = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(static method => method.Identifier.ValueText == "Target");
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
            var assembly = AssemblyLoadContextHandle.Load(stream.ToArray());
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

        internal FrontendLoweringResult Lower() =>
            new RoslynOperationLowerer(Factory).Lower(TargetExpression);

        internal object? CompareWithInterpreter(params object?[] arguments) {
            var actual = _method!.Invoke(null, arguments);
            var lowered = Lower();
            Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
            var environment = CreateEnvironment(lowered, arguments);
            var evaluated = new IrInterpreter(Factory).Evaluate(lowered.Term, environment);
            Assert.That(evaluated.Status, Is.EqualTo(IrEvaluationStatus.Value));
            var interpreted = evaluated.Value!.Kind switch {
                IrValueKind.Boolean => evaluated.Value.Boolean,
                IrValueKind.Integer => evaluated.Value.Integer,
                IrValueKind.String => evaluated.Value.String,
                IrValueKind.Null => null,
                _ => actual
            };
            Assert.That(interpreted, Is.EqualTo(actual));
            return interpreted;
        }

        internal Dictionary<IrVarId, IrValue> CreateEnvironment(
            FrontendLoweringResult lowering,
            params object?[] arguments) {
            var values = new Dictionary<IrVarId, IrValue>();
            foreach (var binding in lowering.Variables) {
                if (binding.Symbol is not IParameterSymbol parameter ||
                    !SymbolEqualityComparer.Default.Equals(
                        parameter.ContainingSymbol,
                        _methodSymbol))
                    continue;
                values.Add(
                    binding.Variable,
                    CreateValue(
                        Factory,
                        Factory.GetVariableInfo(binding.Variable).Type,
                        arguments[parameter.Ordinal]));
            }
            return values;
        }

        public void Dispose() => _assembly.Dispose();

        private static IOperation FindReturnExpression(
            SemanticModel model,
            MethodDeclarationSyntax method) {
            if (method.ExpressionBody != null)
                return GetExpressionOperation(
                    model,
                    method.ExpressionBody.Expression);
            var returnStatement = method.Body!
                .DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Last();
            return GetExpressionOperation(model, returnStatement.Expression!);
        }

        private static IOperation GetExpressionOperation(
            SemanticModel model,
            ExpressionSyntax expression) {
            var operation = model.GetOperation(expression);
            if (operation != null) return operation;
            return expression switch {
                CheckedExpressionSyntax checkedExpression =>
                    GetExpressionOperation(model, checkedExpression.Expression),
                ParenthesizedExpressionSyntax parenthesized =>
                    GetExpressionOperation(model, parenthesized.Expression),
                _ => throw new InvalidOperationException(
                    "Roslyn did not expose an operation for expression kind " +
                    expression.Kind() + ".")
            };
        }

        private static IrValue CreateValue(
            IrFactory factory,
            IrTypeId type,
            object? value) {
            var info = factory.GetTypeInfo(type);
            if (value == null) return factory.CreateNullValue(type);
            return info.Kind switch {
                IrTypeKind.Boolean => factory.CreateBooleanValue((bool)value),
                IrTypeKind.Integer => factory.CreateIntegerValue(
                    Convert.ToInt64(value, CultureInfo.InvariantCulture)),
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

    private sealed class AssemblyLoadContextHandle : IDisposable {
        private readonly System.Runtime.Loader.AssemblyLoadContext _context;

        private AssemblyLoadContextHandle(
            System.Runtime.Loader.AssemblyLoadContext context,
            Assembly assembly) {
            _context = context;
            Assembly = assembly;
        }

        internal Assembly Assembly { get; }

        internal static AssemblyLoadContextHandle Load(byte[] image) {
            var context = new System.Runtime.Loader.AssemblyLoadContext(
                "SharpProof.Frontend.Test." + Guid.NewGuid().ToString("N"),
                isCollectible: true);
            using var stream = new MemoryStream(image, writable: false);
            return new AssemblyLoadContextHandle(
                context,
                context.LoadFromStream(stream));
        }

        public void Dispose() => _context.Unload();
    }

    private static PortableExecutableReference CreateAliasedTypeReference(
        string assemblyName,
        string alias) {
        var tree = CSharpSyntaxTree.ParseText(
            """
            namespace Collision {
                public sealed class Widget {
                }
            }
            """,
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            PlatformReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.That(
            emit.Success,
            Is.True,
            string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(
            stream.ToArray().ToImmutableArray(),
            new MetadataReferenceProperties(
                MetadataImageKind.Assembly,
                aliases: [alias]));
    }

    private static ImmutableArray<MetadataReference> PlatformReferences { get; } =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];
}
