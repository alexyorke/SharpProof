using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractBinderTests {
    [Test]
    public void DirectCompilerBoundContractWinsAndSymbolIdentityRejectsShadows() {
        var source =
            """
            using SharpProof.Attributes;
            public static class Shadow {
                public static class Contract {
                    public static void Requires(bool value) { }
                }
            }
            public static class Target {
                public static T @Select<T>(T value, bool ok) {
                    Contract.Requires(ok);
                    Shadow.Contract.Requires(false);
                    void Nested() { Shadow.Contract.Requires(false); }
                    Nested();
                    return value;
                }
            }
            [ContractFor(typeof(Target))]
            public static class BrokenCompanion {
                public static int Select(int wrong) => wrong;
            }
            """;
        using var subject = ContractSubject.Create(source);
        var result = subject.Bind("Target", "Select");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.False);
        Assert.That(result.Contracts.Source.Arity, Is.EqualTo(1));
        Assert.That(result.Contracts.Clauses.Length, Is.EqualTo(1));
        Assert.That(
            result.Contracts.Clauses[0].Kind,
            Is.EqualTo(BoundContractKind.Requires));
    }

    [Test]
    public void ResultAndOldBindToTypedResultAndPreStateVariables() {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Advance(long value) {
                    Contract.Ensures(
                        Contract.Result<long>() > Contract.Old(value));
                    return checked(value + 1L);
                }
            }
            """;
        using var subject = ContractSubject.Create(source);
        var result = subject.Bind("Target", "Advance");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        var contracts = result.Contracts!;
        Assert.That(contracts.Clauses.Length, Is.EqualTo(1));
        Assert.That(contracts.Clauses[0].Condition, Is.TypeOf<IrBinaryTerm>());
        var binary = (IrBinaryTerm)contracts.Clauses[0].Condition;
        Assert.That(binary.Left, Is.TypeOf<IrVariableTerm>());
        Assert.That(binary.Right, Is.TypeOf<IrVariableTerm>());
        var resultVariable = contracts.Variables.Single(
            static variable => variable.Role == BoundContractVariableRole.Result);
        var preState = contracts.Variables.Single(
            static variable => variable.Role == BoundContractVariableRole.PreState);
        Assert.That(
            ((IrVariableTerm)binary.Left).Variable,
            Is.EqualTo(resultVariable.Variable));
        Assert.That(
            ((IrVariableTerm)binary.Right).Variable,
            Is.EqualTo(preState.Variable));
        Assert.That(preState.CurrentStateVariable, Is.Not.Null);
        Assert.That(
            preState.CurrentStateVariable,
            Is.EqualTo(contracts.Variables.Single(
                static variable =>
                    variable.Role == BoundContractVariableRole.Parameter).Variable));
    }

    [Test]
    public void ArrayLengthOverResultBindsToSequenceLength() {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static int[] Empty() {
                    Contract.Ensures(
                        Contract.Result<int[]>() != null);
                    Contract.Ensures(
                        Contract.Result<int[]>().Length == 0);
                    return System.Array.Empty<int>();
                }
            }
            """;
        using var subject = ContractSubject.Create(source);
        var result = subject.Bind("Target", "Empty");

        Assert.That(
            result.IsSuccess,
            Is.True,
            result.Failure.ToString());
        var equality = (IrBinaryTerm)result.Contracts!.Clauses
            .Last()
            .Condition;
        Assert.That(result.Contracts.Clauses, Has.Length.EqualTo(2));
        Assert.That(equality.Left, Is.TypeOf<IrLengthTerm>());
    }

    [TestCase("int", "long")]
    [TestCase("uint", "int")]
    [TestCase("string?", "object?")]
    [TestCase("string?", "string")]
    public void ResultTypeMustExactlyMatchTheCallableReturnType(
        string returnType,
        string resultType) {
        var source =
            """
            #nullable enable
            using SharpProof.Attributes;
            public static class Target {
                public static RETURN Invalid(RETURN value) {
                    Contract.Ensures(
                        Contract.Result<RESULT>() ==
                        Contract.Result<RESULT>());
                    return value;
                }
            }
            """
            .Replace("RETURN", returnType, StringComparison.Ordinal)
            .Replace("RESULT", resultType, StringComparison.Ordinal);
        using var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Target", "Invalid").Failure,
            Is.EqualTo(ContractBindingFailure.InvalidIntrinsicSignature));
    }

    [TestCase(
        "Contract.Result<long>() > 0L",
        ContractBindingFailure.ResultOutsideEnsures)]
    [TestCase(
        "Contract.Old(value) > 0L",
        ContractBindingFailure.OldOutsideEnsures)]
    public void ResultAndOldOutsideEnsuresFailClosed(
        string expression,
        ContractBindingFailure expected) {
        var source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Invalid(long value) {
                    Contract.Requires(
            """ +
            expression +
            """
                    );
                    return value;
                }
            }
            """;
        using var subject = ContractSubject.Create(source);
        var result = subject.Bind("Target", "Invalid");
        Assert.That(result.Failure, Is.EqualTo(expected));
    }

    [Test]
    public void NestedOldFailsClosed() {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Invalid(long value) {
                    Contract.Ensures(Contract.Old(Contract.Old(value)) > 0L);
                    return value;
                }
            }
            """;
        using var subject = ContractSubject.Create(source);
        Assert.That(
            subject.Bind("Target", "Invalid").Failure,
            Is.EqualTo(ContractBindingFailure.NestedOld));
    }

    [Test]
    public void AssumeIsExplicitEvidenceAndNotARequirement() {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Read(long value, bool established) {
                    Contract.Assume(established);
                    return value;
                }
            }
            """;
        using var subject = ContractSubject.Create(source);
        var result = subject.Bind("Target", "Read");
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        var clause = result.Contracts!.Clauses.Single();
        Assert.That(clause.Kind, Is.EqualTo(BoundContractKind.Assume));
        Assert.That(clause.IsAssumptionEvidence, Is.True);
    }

    [Test]
    public void RequiresOnlyBindingIgnoresUnsupportedEnsuresAndAssumeClauses() {
        const string source =
            """
            using System;
            using SharpProof.Attributes;
            public static class Target {
                public static long Read(long value) {
                    Contract.Requires(value > 0L);
                    Contract.Ensures(DateTime.Now.Ticks > value);
                    Contract.Assume(DateTime.Now.Ticks != value);
                    return value;
                }
            }
            """;
        using var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        var result = subject.BindRequires("Target", "Read");
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.Clauses, Has.Length.EqualTo(1));
        Assert.That(
            result.Contracts.Clauses[0].Kind,
            Is.EqualTo(BoundContractKind.Requires));
        Assert.That(
            result.Contracts.Variables,
            Has.None.Matches<BoundContractVariable>(
                static variable =>
                    variable.Role == BoundContractVariableRole.Result ||
                    variable.Role == BoundContractVariableRole.PreState));
    }

    [Test]
    public void ClosedAttributesProduceTypedConditionsAndPureFacet() {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                [return: NotNull]
                [Pure]
                public static string Read(
                    [NotNull] string text,
                    [Positive, InRange(1L, 10L)] long count) => text;
            }
            """;
        using var subject = ContractSubject.Create(source);
        var result = subject.Bind("Target", "Read");
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.IsPure, Is.True);
        Assert.That(result.Contracts.Clauses.Length, Is.EqualTo(4));
        Assert.That(
            result.Contracts.Clauses.Count(static clause =>
                clause.Kind == BoundContractKind.Requires),
            Is.EqualTo(3));
        Assert.That(
            result.Contracts.Clauses.Count(static clause =>
                clause.Kind == BoundContractKind.Ensures),
            Is.EqualTo(1));
        Assert.That(
            result.Contracts.Clauses.All(static clause =>
                clause.Evidence == BoundContractEvidence.ClosedAttribute),
            Is.True);
    }

    [Test]
    public void ExactGenericCompanionIsDiscoveredAndBound() {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static T Select<T>(T value, bool ok) => value;
            }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static T Select<T>(T value, bool ok) {
                    Contract.Requires(ok);
                    return value;
                }
            }
            """;
        using var subject = ContractSubject.Create(source);
        var result = subject.Bind("Target", "Select");
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.True);
        Assert.That(
            result.Contracts.Clauses.Single().Evidence,
            Is.EqualTo(BoundContractEvidence.Companion));
    }

    [TestCase(
        """
        [ContractFor(typeof(Target))]
        public static class TargetContracts {
            public static long Other(long value) => value;
        }
        """,
        ContractBindingFailure.MissingCompanion)]
    [TestCase(
        """
        [ContractFor(typeof(Target))]
        public static class TargetContracts {
            public static long Read(string value) => 0L;
        }
        """,
        ContractBindingFailure.CompanionSignatureMismatch)]
    [TestCase(
        """
        [ContractFor(typeof(Target))]
        public static class FirstContracts {
            public static long Read(long value) => value;
        }
        [ContractFor(typeof(Target))]
        public static class SecondContracts {
            public static long Read(long value) => value;
        }
        """,
        ContractBindingFailure.AmbiguousCompanion)]
    public void InvalidCompanionShapesFailClosed(
        string companion,
        ContractBindingFailure expected) {
        var source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Read(long value) => value;
            }
            """ +
            companion;
        using var subject = ContractSubject.Create(source);
        Assert.That(subject.Bind("Target", "Read").Failure, Is.EqualTo(expected));
    }

    [Test]
    public void ProductionBinderContainsNoTextualOrSpeculativeBindingEscapeHatches() {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "SharpProof.Contracts"),
            "*.cs",
            SearchOption.AllDirectories);
        var forbidden = new[] {
            "SyntaxFactory.",
            "ParseExpression(",
            "ParseStatement(",
            "GetSpeculative",
            "ToDisplayString("
        };
        foreach (var file in files) {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden)
                Assert.That(text, Does.Not.Contain(token), file);
        }
    }

    private static string FindRepositoryRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null) {
            if (Directory.Exists(Path.Combine(directory.FullName, "SharpProof.Contracts")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class ContractSubject : IDisposable {
        private ContractSubject(CSharpCompilation compilation) =>
            Compilation = compilation;

        private CSharpCompilation Compilation { get; }

        internal static ContractSubject Create(string source) {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(
                    LanguageVersion.CSharp12,
                    preprocessorSymbols: ["SHARPPROOF_CONTRACTS"]));
            var compilation = CSharpCompilation.Create(
                "Contracts_" + Guid.NewGuid().ToString("N"),
                [syntaxTree],
                GetReferences(),
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
                string.Join(Environment.NewLine, errors.Select(
                    static diagnostic => diagnostic.ToString())));
            return new ContractSubject(compilation);
        }

        internal ContractBindingResult Bind(
            string typeName,
            string methodName) {
            var method = GetMethod(typeName, methodName);
            return new ContractBinder(Compilation, new IrFactory()).Bind(method);
        }

        internal ContractBindingResult BindRequires(
            string typeName,
            string methodName) {
            var method = GetMethod(typeName, methodName);
            return new ContractBinder(
                Compilation,
                new IrFactory()).BindRequires(method);
        }

        private IMethodSymbol GetMethod(
            string typeName,
            string methodName) {
            var type = Compilation.GetTypeByMetadataName(typeName) ??
                       throw new InvalidOperationException(typeName);
            return type.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .Single();
        }

        public void Dispose() {
        }

        private static ImmutableArray<MetadataReference> GetReferences() {
            var paths = ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Append(typeof(Contract).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return [.. paths.Select(static path =>
                MetadataReference.CreateFromFile(path))];
        }
    }
}
