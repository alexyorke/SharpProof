using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class PartialMethodContractTests
{
    [Test]
    public void DirectContractsBindFromTheImplementationAcrossSyntaxTrees()
    {
        var compilation = CreateCompilation(
            (
                "Definition.cs",
                """
                public static partial class Subject {
                    public static partial long Identity(long value);
                }
                """),
            (
                "Implementation.cs",
                """
                using SharpProof.Attributes;
                public static partial class Subject {
                    public static partial long Identity(long value) {
                        Contract.Requires(value >= 0);
                        Contract.Ensures(
                            Contract.Result<long>() == value);
                        return value;
                    }
                }
                """));
        var definition = GetMethod(compilation, "Subject", "Identity");

        var result = new ContractBinder(compilation, new IrFactory())
            .Bind(definition);

        AssertSuccessfulImplementationBinding(
            result,
            definition,
            usesCompanion: false);
    }

    [Test]
    public void ClauseInventoryNormalizesDefinitionToImplementation()
    {
        var compilation = CreateCompilation(
            (
                "Definition.cs",
                """
                public static partial class Subject {
                    public static partial long Identity(long value);
                }
                """),
            (
                "Implementation.cs",
                """
                using SharpProof.Attributes;
                public static partial class Subject {
                    public static partial long Identity(long value) {
                        Contract.Requires(value >= 0);
                        return value;
                    }
                }
                """));
        var definition = GetMethod(compilation, "Subject", "Identity");

        var inventory = new ContractClauseInventoryBuilder(compilation)
            .Create(definition);

        Assert.That(inventory.ImplementationBody, Is.Not.Null);
        Assert.That(inventory.Clauses, Has.Length.EqualTo(1));
        Assert.That(inventory.Clauses[0].IsValid, Is.True);
    }

    [Test]
    public void ParameterClosedAttributesBindFromEitherPartialPart()
    {
        var compilation = CreateCompilation(
            (
                "Definition.cs",
                """
                using SharpProof.Attributes;
                public static partial class Subject {
                    public static partial long Identity(
                        [Positive] long value);
                }
                """),
            (
                "Implementation.cs",
                """
                public static partial class Subject {
                    public static partial long Identity(long value) => value;
                }
                """));
        var definition = GetMethod(compilation, "Subject", "Identity");
        var implementation = definition.PartialImplementationPart!;

        var fromDefinition = new ContractBinder(compilation, new IrFactory())
            .Bind(definition);
        var fromImplementation = new ContractBinder(compilation, new IrFactory())
            .Bind(implementation);

        AssertSingleClosedClause(fromDefinition, BoundContractKind.Requires);
        AssertSingleClosedClause(fromImplementation, BoundContractKind.Requires);
    }

    [Test]
    public void ReturnClosedAttributesBindFromEitherPartialPart()
    {
        var compilation = CreateCompilation(
            (
                "Definition.cs",
                """
                public static partial class Subject {
                    public static partial string Identity(string value);
                }
                """),
            (
                "Implementation.cs",
                """
                using SharpProof.Attributes;
                public static partial class Subject {
                    [return: NotNull]
                    public static partial string Identity(string value) => value;
                }
                """));
        var definition = GetMethod(compilation, "Subject", "Identity");
        var implementation = definition.PartialImplementationPart!;

        var fromDefinition = new ContractBinder(compilation, new IrFactory())
            .Bind(definition);
        var fromImplementation = new ContractBinder(compilation, new IrFactory())
            .Bind(implementation);

        AssertSingleClosedClause(fromDefinition, BoundContractKind.Ensures);
        AssertSingleClosedClause(fromImplementation, BoundContractKind.Ensures);
    }

    [TestCase(MethodKind.PropertyGet)]
    [TestCase(MethodKind.PropertySet)]
    public void PartialPropertyAccessorsUseImplementationBodies(
        MethodKind accessorKind)
    {
        var compilation = CreateCompilation(
            (
                "Definition.cs",
                """
                public partial class Subject {
                    public partial int Value { get; set; }
                }
                """),
            (
                "Implementation.cs",
                """
                using SharpProof.Attributes;
                public partial class Subject {
                    public partial int Value {
                        get {
                            Contract.Requires(true);
                            return 1;
                        }
                        set {
                            Contract.Requires(value >= 0);
                        }
                    }
                }
                """));
        var property = compilation.GetTypeByMetadataName("Subject")!
            .GetMembers("Value")
            .OfType<IPropertySymbol>()
            .Single(static property =>
                property.PartialImplementationPart != null);
        var definitionAccessor = accessorKind == MethodKind.PropertyGet
            ? property.GetMethod!
            : property.SetMethod!;

        var inventory = new ContractClauseInventoryBuilder(compilation)
            .Create(definitionAccessor);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inventory.ImplementationBody, Is.Not.Null);
            Assert.That(inventory.Clauses, Has.Length.EqualTo(1));
            Assert.That(inventory.Clauses[0].IsValid, Is.True);
        }
    }

    [TestCase(MethodKind.PropertyGet)]
    [TestCase(MethodKind.PropertySet)]
    public void ConstructedPartialPropertyAccessorsKeepSpecialization(
        MethodKind accessorKind)
    {
        var compilation = CreateCompilation(
            (
                "Definition.cs",
                """
                public partial class Subject<T> where T : class {
                    public partial T Value { get; set; }
                }
                """),
            (
                "Implementation.cs",
                """
                using SharpProof.Attributes;
                public partial class Subject<T> where T : class {
                    public partial T Value {
                        get {
                            Contract.Requires(true);
                            return null!;
                        }
                        set {
                            Contract.Requires(value != null);
                        }
                    }
                }
                """));
        var generic = compilation.GetTypeByMetadataName("Subject`1")!;
        var constructed = generic.Construct(
            compilation.GetSpecialType(SpecialType.System_String));
        var property = constructed.GetMembers("Value")
            .OfType<IPropertySymbol>()
            .Single();
        var accessor = accessorKind == MethodKind.PropertyGet
            ? property.GetMethod!
            : property.SetMethod!;

        var result = new ContractBinder(compilation, new IrFactory())
            .BindRequires(accessor);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
            Assert.That(result.Contracts!.Clauses, Has.Length.EqualTo(1));
            Assert.That(
                result.Contracts.Source.ContainingType.TypeArguments[0]
                    .SpecialType,
                Is.EqualTo(SpecialType.System_String));
        }
    }

    [TestCase(MethodKind.EventAdd)]
    [TestCase(MethodKind.EventRemove)]
    public void ReferencedPartialEventAccessorsUseImplementationBodies(
        MethodKind accessorKind)
    {
        var compilation = CreateCompilation(
            (
                "Definition.cs",
                """
                public partial class Subject {
                    public partial event System.Action Changed;
                }
                """),
            (
                "Implementation.cs",
                """
                using SharpProof.Attributes;
                public partial class Subject {
                    public partial event System.Action Changed {
                        add {
                            Contract.Requires(value != null);
                        }
                        remove {
                            Contract.Requires(value != null);
                            Contract.Requires(true);
                        }
                    }
                }
                """),
            (
                "Consumer.cs",
                """
                public static class Consumer {
                    public static void Subscribe(
                        Subject subject,
                        System.Action handler) {
                        subject.Changed += handler;
                    }

                    public static void Unsubscribe(
                        Subject subject,
                        System.Action handler) {
                        subject.Changed -= handler;
                    }
                }
                """));
        var definition = compilation.GetTypeByMetadataName("Subject")!
            .GetMembers("Changed")
            .OfType<IEventSymbol>()
            .Single(static @event =>
                @event.PartialImplementationPart != null);
        var implementation = definition.PartialImplementationPart!;
        var consumerTree = compilation.SyntaxTrees.Single(static tree =>
            Path.GetFileName(tree.FilePath) == "Consumer.cs");
        var assignmentKind = accessorKind == MethodKind.EventAdd
            ? SyntaxKind.AddAssignmentExpression
            : SyntaxKind.SubtractAssignmentExpression;
        var eventReference = consumerTree.GetRoot().DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Single(assignment => assignment.IsKind(assignmentKind))
            .Left;
        var referencedEvent = (IEventSymbol)compilation
            .GetSemanticModel(consumerTree)
            .GetSymbolInfo(eventReference)
            .Symbol!;
        var definitionAccessor = accessorKind == MethodKind.EventAdd
            ? referencedEvent.AddMethod!
            : referencedEvent.RemoveMethod!;
        var implementationAccessor = accessorKind == MethodKind.EventAdd
            ? implementation.AddMethod!
            : implementation.RemoveMethod!;
        var builder = new ContractClauseInventoryBuilder(compilation);
        var expectedClauseCount = accessorKind == MethodKind.EventAdd ? 1 : 2;

        var fromDefinition = builder.Create(definitionAccessor);
        var fromImplementation = builder.Create(implementationAccessor);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                SymbolEqualityComparer.Default.Equals(
                    referencedEvent,
                    definition),
                Is.True);
            Assert.That(
                SymbolEqualityComparer.Default.Equals(
                    definitionAccessor,
                    implementationAccessor),
                Is.False);
            Assert.That(fromDefinition.ImplementationBody, Is.Not.Null);
            Assert.That(
                fromDefinition.Clauses,
                Has.Length.EqualTo(expectedClauseCount));
            Assert.That(
                fromDefinition.Clauses.All(static clause => clause.IsValid),
                Is.True);
            Assert.That(fromImplementation.ImplementationBody, Is.Not.Null);
            Assert.That(
                fromImplementation.Clauses,
                Has.Length.EqualTo(expectedClauseCount));
            Assert.That(
                fromImplementation.Clauses.All(static clause => clause.IsValid),
                Is.True);
            Assert.That(
                SymbolEqualityComparer.Default.Equals(
                    fromDefinition.Callable,
                    implementationAccessor),
                Is.True);
            Assert.That(
                SymbolEqualityComparer.Default.Equals(
                    fromImplementation.Callable,
                    implementationAccessor),
                Is.True);
        }
    }

    [Test]
    public void CompanionContractsBindFromTheImplementationAcrossSyntaxTrees()
    {
        var compilation = CreateCompilation(
            (
                "Target.cs",
                """
                public interface ISubject {
                    long Identity(long value);
                }
                """),
            (
                "CompanionDefinition.cs",
                """
                using SharpProof.Attributes;
                [ContractFor(typeof(ISubject))]
                public static partial class SubjectContracts {
                    public static partial long Identity(
                        ISubject receiver,
                        long value);
                }
                """),
            (
                "CompanionImplementation.cs",
                """
                using SharpProof.Attributes;
                public static partial class SubjectContracts {
                    public static partial long Identity(
                        ISubject receiver,
                        long value) {
                        Contract.Requires(value >= 0);
                        Contract.Ensures(
                            Contract.Result<long>() == value);
                        return value;
                    }
                }
                """));
        var target = GetMethod(compilation, "ISubject", "Identity");

        var result = new ContractBinder(compilation, new IrFactory())
            .Bind(target);

        AssertSuccessfulImplementationBinding(
            result,
            target,
            usesCompanion: true);
    }

    private static void AssertSuccessfulImplementationBinding(
        ContractBindingResult result,
        IMethodSymbol target,
        bool usesCompanion)
    {
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        var contracts = result.Contracts!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contracts.Target, Is.EqualTo(target));
            Assert.That(contracts.UsesCompanion, Is.EqualTo(usesCompanion));
            Assert.That(contracts.Source.PartialDefinitionPart, Is.Not.Null);
            Assert.That(
                contracts.Source.PartialImplementationPart,
                Is.Null);
            Assert.That(contracts.Clauses, Has.Length.EqualTo(2));
            Assert.That(
                Path.GetFileName(
                    contracts.Source.DeclaringSyntaxReferences
                        .Single()
                        .SyntaxTree.FilePath),
                Does.Contain("Implementation.cs"));
        }
    }

    private static void AssertSingleClosedClause(
        ContractBindingResult result,
        BoundContractKind kind)
    {
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        var clause = result.Contracts!.Clauses.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(clause.Kind, Is.EqualTo(kind));
            Assert.That(
                clause.Evidence,
                Is.EqualTo(BoundContractEvidence.ClosedAttribute));
        }
    }

    private static IMethodSymbol GetMethod(
        CSharpCompilation compilation,
        string typeName,
        string methodName)
    {
        var type = compilation.GetTypeByMetadataName(typeName) ??
                   throw new InvalidOperationException(typeName);
        return type.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Single();
    }

    private static CSharpCompilation CreateCompilation(
        params (string FileName, string Source)[] sources)
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Preview,
            preprocessorSymbols: ["SHARPPROOF_CONTRACTS"]);
        var compilation = CSharpCompilation.Create(
            "PartialContracts_" + Guid.NewGuid().ToString("N"),
            sources.Select(source => CSharpSyntaxTree.ParseText(
                source.Source,
                parseOptions,
                source.FileName)),
            ContractTestMetadataReferences.WithSharpProof,
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
                errors.Select(static diagnostic =>
                    diagnostic.ToString())));
        return compilation;
    }

}
