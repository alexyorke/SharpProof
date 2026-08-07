namespace SharpProof.ContractForGenerator.Test;

[TestFixture]
public sealed class ContractForValidatorGeneratorTests
{
    [Test]
    public void InterfaceCompanionWithDirectClausesIsValid()
    {
        var run = Run(
            """
            #nullable enable
            using SharpProof.Attributes;

            public interface IService {
                string? Find(string key);
            }

            [ContractFor(typeof(IService))]
            public static class IServiceContracts {
                public static string? Find(IService receiver, string key) {
                    Contract.Requires(receiver is not null);
                    Contract.Requires(key.Length > 0);
                    Contract.Ensures(
                        Contract.Result<string?>() is null or not null);
                    return null;
                }
            }
            """);

        Assert.That(run.Diagnostics, Is.Empty);
        Assert.That(run.RunResult.GeneratedTrees, Is.Empty);
    }

    [Test]
    public void AbstractMemberPreservesRefKindsAndNullability()
    {
        var run = Run(
            """
            #nullable enable
            using SharpProof.Attributes;

            public abstract class Worker {
                public abstract int Compute(ref int value, in string? label);
            }

            [ContractFor(typeof(Worker))]
            public static class WorkerContracts {
                public static int Compute(
                    Worker receiver,
                    ref int value,
                    in string? label) {
                    Contract.Requires(receiver is not null);
                    return value;
                }
            }
            """);

        Assert.That(run.Diagnostics, Is.Empty);
    }

    [Test]
    public void OpenGenericTargetAndMethodConstraintsMatchStructurally()
    {
        var run = Run(
            """
            #nullable enable
            using SharpProof.Attributes;

            public interface IRepository<T>
                where T : class? {
                TResult Map<TResult>(T value, TResult fallback)
                    where TResult : notnull, new();
            }

            [ContractFor(typeof(IRepository<>))]
            public static class RepositoryContracts<T>
                where T : class? {
                public static TResult Map<TResult>(
                    IRepository<T> receiver,
                    T value,
                    TResult fallback)
                    where TResult : notnull, new() {
                    Contract.Requires(receiver is not null);
                    return fallback;
                }
            }
            """);

        Assert.That(run.Diagnostics, Is.Empty);
    }

    [Test]
    public void OpenGenericConstraintOrderIsSemanticallyMatched()
    {
        var run = Run(
            """
            using SharpProof.Attributes;

            public interface IFirst {
            }
            public interface ISecond {
            }
            public interface IRepository<T>
                where T : IFirst, ISecond {
                T Select(T value, bool ok);
            }

            [ContractFor(typeof(IRepository<>))]
            public static class RepositoryContracts<T>
                where T : ISecond, IFirst {
                public static T Select(
                    IRepository<T> receiver,
                    T value,
                    bool ok) => value;
            }
            """);

        Assert.That(run.Diagnostics, Is.Empty);
    }

    [Test]
    public void TupleElementNamesMatchExactly()
    {
        var run = Run(
            """
            #nullable enable
            using SharpProof.Attributes;

            public interface ITarget {
                (int Left, string? Right) Read(
                    (int Left, string? Right) value);
            }

            [ContractFor(typeof(ITarget))]
            public static class TargetContracts {
                public static (int Left, string? Right) Read(
                    ITarget receiver,
                    (int Left, string? Right) value) => value;
            }
            """);

        Assert.That(run.Diagnostics, Is.Empty);
    }

    [TestCase(
        """
        public interface ITarget<T> {
            static abstract void Ping();
        }
        [ContractFor(typeof(ITarget<>))]
        public static class TargetContracts {
            public static void Ping() {
            }
        }
        """)]
    [TestCase(
        """
        public interface ITarget<T>
            where T : class? {
            static abstract void Ping();
        }
        [ContractFor(typeof(ITarget<>))]
        public static class TargetContracts<T>
            where T : class {
            public static void Ping() {
            }
        }
        """)]
    public void OpenGenericCompanionTypeMustMatchArityAndConstraints(
        string declarations)
    {
        var run = Run(
            """
            #nullable enable
            using SharpProof.Attributes;
            """ +
            declarations);

        var diagnostic = AssertSingle(run, "SPCF0003");
        Assert.That(
            GetLocatedText(diagnostic),
            Does.Contain("ContractFor"));
    }

    [Test]
    public void EscapedIdentifiersBindBySymbolName()
    {
        var run = Run(
            """
            using SharpProof.Attributes;

            public interface IKeywords {
                int @class(string @event);
            }

            [ContractFor(typeof(IKeywords))]
            public static class KeywordContracts {
                public static int @class(
                    IKeywords receiver,
                    string @event) {
                    Contract.Requires(receiver is not null);
                    return @event.Length;
                }
            }
            """);

        Assert.That(run.Diagnostics, Is.Empty);
    }

    [Test]
    public void DuplicateCompanionsAreReportedAtBothAttributes()
    {
        var run = Run(
            """
            using SharpProof.Attributes;

            public interface ITarget {
                void Invoke();
            }

            [ContractFor(typeof(ITarget))]
            public static class FirstContracts {
                public static void Invoke(ITarget receiver) {
                }
            }

            [ContractFor(typeof(ITarget))]
            public static class SecondContracts {
                public static void Invoke(ITarget receiver) {
                }
            }
            """);

        Assert.That(
            run.Diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPCF0002", "SPCF0002"]));
        Assert.That(
            run.Diagnostics.All(diagnostic =>
                GetLocatedText(diagnostic).Contains(
                    "ContractFor",
                    StringComparison.Ordinal)),
            Is.True);
    }

    [Test]
    public void OpenAndClosedGenericCompanionsAreReportedAsOverlapping()
    {
        var run = Run(
            """
            using SharpProof.Attributes;

            public interface ITarget<T> {
                void Invoke(T value);
            }

            [ContractFor(typeof(ITarget<>))]
            public static class OpenContracts<T> {
                public static void Invoke(ITarget<T> receiver, T value) { }
            }

            [ContractFor(typeof(ITarget<int>))]
            public static class ClosedContracts {
                public static void Invoke(ITarget<int> receiver, int value) { }
            }
            """);

        Assert.That(
            run.Diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPCF0002", "SPCF0002"]));
    }

    [Test]
    public void DistinctClosedGenericCompanionsDoNotOverlap()
    {
        var run = Run(
            """
            using SharpProof.Attributes;

            public interface ITarget<T> {
                void Invoke(T value);
            }

            [ContractFor(typeof(ITarget<int>))]
            public static class IntContracts {
                public static void Invoke(ITarget<int> receiver, int value) { }
            }

            [ContractFor(typeof(ITarget<string>))]
            public static class StringContracts {
                public static void Invoke(ITarget<string> receiver, string value) { }
            }
            """);

        Assert.That(run.Diagnostics, Is.Empty);
    }

    [TestCase(
        "typeof(ITarget<>)",
        "public static class ReferencedContracts<T> { public static void Invoke(ITarget<T> receiver, T value) { } }",
        "typeof(ITarget<int>)",
        "public static class SourceContracts { public static void Invoke(ITarget<int> receiver, int value) { } }")]
    [TestCase(
        "typeof(ITarget<int>)",
        "public static class ReferencedContracts { public static void Invoke(ITarget<int> receiver, int value) { } }",
        "typeof(ITarget<>)",
        "public static class SourceContracts<T> { public static void Invoke(ITarget<T> receiver, T value) { } }")]
    public void ReferencedAndSourceGenericCompanionsReportSourceOverlap(
        string referencedTarget,
        string referencedCompanion,
        string sourceTarget,
        string sourceCompanion)
    {
        var compilation = GeneratorTestHost.CreateCompilationWithReference(
            $$"""
            using SharpProof.Attributes;
            public interface ITarget<T> { void Invoke(T value); }
            [ContractFor({{referencedTarget}})]
            {{referencedCompanion}}
            """,
            ("Subject.cs",
            $$"""
            using SharpProof.Attributes;
            [ContractFor({{sourceTarget}})]
            {{sourceCompanion}}
            """));

        var diagnostic = AssertSingle(
            GeneratorTestHost.Run(compilation),
            "SPCF0002");
        Assert.That(GetLocatedText(diagnostic), Does.Contain("ContractFor"));
    }

    [Test]
    public void ReferencedAndSourceDistinctClosedCompanionsDoNotOverlap()
    {
        var compilation = GeneratorTestHost.CreateCompilationWithReference(
            """
            using SharpProof.Attributes;
            public interface ITarget<T> { void Invoke(T value); }
            [ContractFor(typeof(ITarget<int>))]
            public static class ReferencedContracts {
                public static void Invoke(ITarget<int> receiver, int value) { }
            }
            """,
            ("Subject.cs",
            """
            using SharpProof.Attributes;
            [ContractFor(typeof(ITarget<string>))]
            public static class SourceContracts {
                public static void Invoke(ITarget<string> receiver, string value) { }
            }
            """));

        Assert.That(GeneratorTestHost.Run(compilation).Diagnostics, Is.Empty);
    }

    [Test]
    public void RepeatedContractForAttributesOnOneCompanionFailClosed()
    {
        var compilation =
            GeneratorTestHost.CreateCompilationWithoutAttributes(
                ("Subject.cs",
                """
                using System;
                using SharpProof.Attributes;

                namespace SharpProof.Attributes {
                    [AttributeUsage(
                        AttributeTargets.Class,
                        AllowMultiple = true)]
                    public sealed class ContractForAttribute(Type target)
                        : Attribute {
                    }
                }

                public interface ITarget {
                    void Invoke();
                }

                [ContractFor(typeof(ITarget))]
                [ContractFor(typeof(ITarget))]
                public static class TargetContracts {
                    public static void Invoke(ITarget receiver) {
                    }
                }
                """));

        var diagnostic = AssertSingle(
            GeneratorTestHost.Run(compilation),
            "SPCF0001");

        Assert.That(
            diagnostic.GetMessage(
                System.Globalization.CultureInfo.InvariantCulture),
            Does.Contain("TargetContracts"));
    }

    [Test]
    public void NullContractForTargetFailsClosed()
    {
        var run = Run(
            """
            #nullable enable
            using SharpProof.Attributes;

            [ContractFor(null)]
            public static class InvalidContracts {
            }
            """);

        var diagnostic = AssertSingle(run, "SPCF0001");
        Assert.That(
            GetLocatedText(diagnostic),
            Does.Contain("ContractFor"));
    }

    [TestCase("public struct Target { }")]
    [TestCase("public enum Target { Value }")]
    [TestCase("public delegate void Target();")]
    public void ContractForTargetMustBeAClassOrInterface(
        string targetDeclaration)
    {
        var run = Run(
            """
            using SharpProof.Attributes;
            """ +
            targetDeclaration +
            """

            [ContractFor(typeof(Target))]
            public static class TargetContracts {
            }
            """);

        var diagnostic = AssertSingle(run, "SPCF0001");
        Assert.That(
            GetLocatedText(diagnostic),
            Does.Contain("ContractFor"));
    }

    [Test]
    public void LookalikeAttributeIsNotTrusted()
    {
        var run = Run(
            """
            using System;
            using ContractForAttribute = Lookalike.ContractForAttribute;

            public interface ITarget {
                void Missing();
            }

            [ContractFor(typeof(ITarget))]
            public static class NotASharpProofCompanion {
            }

            namespace Lookalike {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class ContractForAttribute : Attribute {
                    public ContractForAttribute(Type target) {
                    }
                }
            }
            """);

        Assert.That(run.Diagnostics, Is.Empty);
    }

    [Test]
    public void MissingMemberIsReportedAtTargetIdentifier()
    {
        var run = Run(
            """
            using SharpProof.Attributes;

            public interface ITarget {
                void Invoke();
            }

            [ContractFor(typeof(ITarget))]
            public static class TargetContracts {
                public static void Other(ITarget receiver) {
                }
            }
            """);

        var diagnostic = AssertSingle(run, "SPCF0004");
        Assert.That(GetLocatedText(diagnostic), Is.EqualTo("Invoke"));
    }

    [TestCase(
        """
        public interface ITarget {
            void Read(string? value);
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static void Read(ITarget receiver, string value) {
            }
        }
        """)]
    [TestCase(
        """
        public interface ITarget {
            void Read(ref int value);
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static void Read(ITarget receiver, out int value) {
                value = 0;
            }
        }
        """)]
    [TestCase(
        """
        public interface ITarget {
            int Read();
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static long Read(ITarget receiver) => 0;
        }
        """)]
    [TestCase(
        """
        public interface ITarget {
            T Read<T>(T value) where T : class;
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static T Read<T>(ITarget receiver, T value) => value;
        }
        """)]
    [TestCase(
        """
        using System.Collections.Generic;
        public interface ITarget {
            void Read(List<string?> value);
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static void Read(
                ITarget receiver,
                List<string> value) {
            }
        }
        """)]
    [TestCase(
        """
        public interface ITarget {
            void Read();
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static void Read() {
            }
        }
        """)]
    [TestCase(
        """
        public interface ITarget {
            ref int Read(ref int value);
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static int Read(
                ITarget receiver,
                ref int value) => value;
        }
        """)]
    [TestCase(
        """
        public interface ITarget {
            void Read(int value = 1);
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static void Read(
                ITarget receiver,
                int value) {
            }
        }
        """)]
    [TestCase(
        """
        public interface ITarget {
            (int Left, int Right) Read((int Left, int Right) value);
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static (int Other, int Right) Read(
                ITarget receiver,
                (int Other, int Right) value) => value;
        }
        """)]
    [TestCase(
        """
        public sealed class Outer<T> {
            public sealed class Leaf {
            }
        }
        public interface ITarget {
            void Read(Outer<int>.Leaf value);
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static void Read(
                ITarget receiver,
                Outer<string>.Leaf value) {
            }
        }
        """)]
    public void ExactSignatureMismatchesFailClosed(string declarations)
    {
        var run = Run(
            """
            #nullable enable
            using SharpProof.Attributes;
            """ +
            declarations);

        var diagnostic = AssertSingle(run, "SPCF0005");
        Assert.That(GetLocatedText(diagnostic), Is.EqualTo("Read"));
        Assert.That(diagnostic.Descriptor.IsEnabledByDefault, Is.True);
        Assert.That(
            diagnostic.Descriptor.Category,
            Is.EqualTo("SharpProof.ContractFor.Usage"));
    }

    [Test]
    public void NestedGenericOwnerScopesDoNotAliasByOrdinal()
    {
        var run = Run(
            """
            using SharpProof.Attributes;

            public sealed class Outer<TOuter> {
                public interface ITarget<TInner> {
                    void Read(TOuter value);
                }
            }

            [ContractFor(typeof(Outer<>.ITarget<>))]
            public static class TargetContracts<TContract> {
                public static void Read(
                    Outer<TContract>.ITarget<TContract> receiver,
                    TContract value) {
                }
            }
            """);

        var diagnostic = AssertSingle(run, "SPCF0003");
        Assert.That(GetLocatedText(diagnostic), Does.Contain("ContractFor"));
    }

    [Test]
    public void StaticAndInstanceOverloadCollapseIsAmbiguous()
    {
        var run = Run(
            """
            using SharpProof.Attributes;

            public interface ITarget {
                void Act(int value);
                static abstract void Act(ITarget receiver, int value);
            }

            [ContractFor(typeof(ITarget))]
            public static class TargetContracts {
                public static void Act(ITarget receiver, int value) {
                }
            }
            """);

        Assert.That(
            run.Diagnostics.Any(static diagnostic =>
                diagnostic.Id == "SPCF0006"),
            Is.True);
        Assert.That(
            run.Diagnostics.All(static diagnostic =>
                diagnostic.Id == "SPCF0006"),
            Is.True);
    }

    [Test]
    public void BodylessCompanionMemberFailsClosed()
    {
        var run = Run(
            """
            using SharpProof.Attributes;

            public interface ITarget {
                void Invoke();
            }

            [ContractFor(typeof(ITarget))]
            public static class TargetContracts {
                public static extern void Invoke(ITarget receiver);
            }
            """);

        var diagnostic = AssertSingle(run, "SPCF0007");
        Assert.That(GetLocatedText(diagnostic), Is.EqualTo("Invoke"));
    }

    [Test]
    public void SourceDefinedContractForAttributeIsRejected()
    {
        var compilation =
            GeneratorTestHost.CreateCompilationWithoutAttributes(
                ("Subject.cs",
                """
                using System;
                using SharpProof.Attributes;

                namespace SharpProof.Attributes {
                    [AttributeUsage(AttributeTargets.Class)]
                    public sealed class ContractForAttribute(Type target)
                        : Attribute {
                    }
                }

                public interface ITarget {
                    void Invoke();
                }

                [ContractFor(typeof(ITarget))]
                public static class TargetContracts {
                    public static void Invoke(ITarget receiver) {
                    }
                }
                """));

        var diagnostic = AssertSingle(
            GeneratorTestHost.Run(compilation),
            "SPCF0001");
        Assert.That(
            diagnostic.GetMessage(
                System.Globalization.CultureInfo.InvariantCulture),
            Does.Contain("TargetContracts"));
    }

    [Test]
    public void ProjectShadowedContractForAttributeIsRejected()
    {
        var compilation = GeneratorTestHost.CreateCompilation(
            ("Subject.cs",
            """
            using System;
            using SharpProof.Attributes;

            namespace SharpProof.Attributes {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class ContractForAttribute(Type target)
                    : Attribute {
                }
            }

            public interface ITarget {
                void Invoke();
            }

            [ContractFor(typeof(ITarget))]
            public static class TargetContracts {
                public static void Invoke(ITarget receiver) {
                }
            }
            """));

        var diagnostic = AssertSingle(
            GeneratorTestHost.Run(compilation),
            "SPCF0001");
        Assert.That(
            diagnostic.GetMessage(
                System.Globalization.CultureInfo.InvariantCulture),
            Does.Contain("TargetContracts"));
    }

    [Test]
    public void EveryInvalidContractClausePlacementIsRejected()
    {
        var run = Run(
            """
            using SharpProof.Attributes;

            public interface ITarget {
                void Invoke();
            }

            [ContractFor(typeof(ITarget))]
            public static class TargetContracts {
                public static void Invoke(ITarget receiver) {
                    Contract.Requires(receiver is not null);
                    if (receiver is not null) {
                        Contract.Ensures(true);
                    }
                    void Local() {
                        Contract.Assume(true);
                    }
                    Local();
                    Contract.Ensures(true);
                    return;
                    Contract.Requires(true);
                }
            }
            """);

        Assert.That(
            run.Diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SPCF0008", 3)));
        string[] expectedMessages = [
            "Contract.Ensures in companion method 'Invoke' has invalid placement: Conditional",
            "Contract.Ensures in companion method 'Invoke' has invalid placement: Late",
            "Contract.Requires in companion method 'Invoke' has invalid placement: Unreachable"
        ];
        Assert.That(
            run.Diagnostics.Select(static diagnostic =>
                diagnostic.GetMessage(
                    System.Globalization.CultureInfo.InvariantCulture)),
            Is.EquivalentTo(expectedMessages));
    }

    [Test]
    public void NestedCallableContractBelongsToTheNestedCallable()
    {
        var run = Run(
            """
            using SharpProof.Attributes;

            public interface ITarget {
                void Invoke();
            }

            [ContractFor(typeof(ITarget))]
            public static class TargetContracts {
                public static void Invoke(ITarget receiver) {
                    void Local(bool condition) {
                        Contract.Requires(condition);
                    }
                    Local(true);
                }
            }
            """);

        Assert.That(run.Diagnostics, Is.Empty);
    }

    [Test]
    public void IncrementalRunsAndTreeOrderAreDeterministic()
    {
        const string target =
            """
            #nullable enable
            public interface ITarget {
                string? Read(string value);
            }
            """;
        const string companion =
            """
            #nullable enable
            using SharpProof.Attributes;

            [ContractFor(typeof(ITarget))]
            public static class TargetContracts {
                public static string Read(
                    ITarget receiver,
                    string value) => value;
            }
            """;
        const string duplicate =
            """
            #nullable enable
            using SharpProof.Attributes;

            [ContractFor(typeof(ITarget))]
            public static class OtherTargetContracts {
                public static string? Read(
                    ITarget receiver,
                    string value) => value;
            }
            """;
        var forwardCompilation = GeneratorTestHost.CreateCompilation(
            ("02_Target.cs", target),
            ("03_OtherCompanion.cs", duplicate),
            ("01_Companion.cs", companion));
        var reverseCompilation = GeneratorTestHost.CreateCompilation(
            ("01_Companion.cs", companion),
            ("02_Target.cs", target),
            ("03_OtherCompanion.cs", duplicate));

        var first = GeneratorTestHost.Run(forwardCompilation);
        var cached = GeneratorTestHost.Run(
            forwardCompilation,
            first.Driver);
        var reversed = GeneratorTestHost.Run(reverseCompilation);

        Assert.That(
            GeneratorTestHost.DiagnosticKeys(cached),
            Is.EqualTo(GeneratorTestHost.DiagnosticKeys(first)));
        Assert.That(
            GeneratorTestHost.DiagnosticKeys(reversed),
            Is.EqualTo(GeneratorTestHost.DiagnosticKeys(first)));
        Assert.That(
            first.Diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPCF0002", "SPCF0002"]));
        var cachedReasons = cached.RunResult.Results
            .SelectMany(static result => result.TrackedSteps.Values)
            .SelectMany(static steps => steps)
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason)
            .ToImmutableArray();
        Assert.That(
            cachedReasons,
            Does.Contain(IncrementalStepRunReason.Cached));
        Assert.That(
            cachedReasons.All(static reason =>
                reason is
                    IncrementalStepRunReason.Cached or
                    IncrementalStepRunReason.Unchanged),
            Is.True);
        Assert.That(first.RunResult.GeneratedTrees, Is.Empty);
        Assert.That(cached.RunResult.GeneratedTrees, Is.Empty);
    }

    [Test]
    public void GeneratorContainsNoTextualBindingOrSourceSynthesis()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "SharpProof.ContractForGenerator"),
            "*.cs",
            SearchOption.AllDirectories);
        string[] forbidden = [
            "ToDisplayString(",
            "SyntaxFactory.",
            "ParseExpression(",
            "ParseStatement(",
            "ParseTypeName(",
            "AddSource("
        ];

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden)
            {
                Assert.That(text, Does.Not.Contain(token), file);
            }
        }
    }

    private static GeneratorRun Run(string source)
    {
        return GeneratorTestHost.Run(
            GeneratorTestHost.CreateCompilation(("Subject.cs", source)));
    }

    private static Diagnostic AssertSingle(
        GeneratorRun run,
        string diagnosticId)
    {
        Assert.That(
            run.Diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo([diagnosticId]));
        return run.Diagnostics[0];
    }

    private static string GetLocatedText(Diagnostic diagnostic)
    {
        var tree = diagnostic.Location.SourceTree ??
                   throw new InvalidOperationException(
                       "Expected a source diagnostic.");
        return tree.GetText()
            .GetSubText(diagnostic.Location.SourceSpan)
            .ToString();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(
                    directory.FullName,
                    "SharpProof.ContractForGenerator")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Repository root was not found.");
    }
}
