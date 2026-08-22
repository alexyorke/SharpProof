using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic =>
                    diagnostic.ToString())));
        return compilation;
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        var paths = ((string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Contract).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return [.. paths.Select(static path =>
            MetadataReference.CreateFromFile(path))];
    }
}
