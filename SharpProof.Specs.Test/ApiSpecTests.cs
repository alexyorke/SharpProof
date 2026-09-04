using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Ir;
using SharpProof.Specs;
using static SharpProof.Testing.ApiSpecTestFacets;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class ApiSpecTests
{
    [Test]
    public void TablesAssignDeterministicLocalIdsButKeepScopesDistinct()
    {
        var first = ApiSpecTable.Create([
            Declaration("z-row", "M:Missing.Z.Run", "Missing.Z"),
            Declaration("a-row", "M:Missing.A.Run", "Missing.A")
        ]);
        var second = ApiSpecTable.Create([
            Declaration("a-row", "M:Missing.A.Run", "Missing.A"),
            Declaration("z-row", "M:Missing.Z.Run", "Missing.Z")
        ]);
        var firstA = first.Templates.Single(static row => row.Target.WitnessIdentifier == "a-row");
        var secondA = second.Templates.Single(static row => row.Target.WitnessIdentifier == "a-row");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstA.Id.Value, Is.Zero);
            Assert.That(secondA.Id.Value, Is.Zero);
            Assert.That(firstA.Id, Is.Not.EqualTo(secondA.Id));
            Assert.That(firstA.Parameters.Single().Value, Is.Zero);
            Assert.That(firstA.Result!.Value.Value, Is.EqualTo(1));
            Assert.That(firstA.Parameters.Single().Spec, Is.EqualTo(firstA.Id));
            Assert.That(first.ContentSha256, Is.EqualTo(second.ContentSha256));
            Assert.That(first.ContentSha256, Does.Match("^[0-9a-f]{64}$"));
        }
    }

    [Test]
    public void ContentDigestCoversTrustedAssemblyIdentity()
    {
        var declaration = Declaration("row", "M:Missing.Row.Run", "Missing.Row");
        var changed = declaration with
        {
            Target = declaration.Target with
            {
                ApprovedAssemblies = [new ApiSpecAssemblyIdentity("Different", string.Empty)]
            }
        };

        Assert.That(
            ApiSpecTable.Create([declaration]).ContentSha256,
            Is.Not.EqualTo(ApiSpecTable.Create([changed]).ContentSha256));

        changed = declaration with
        {
            Target = declaration.Target with
            {
                ApprovedAssemblies = [
                    RuntimeAssemblyIdentity() with {
                        ReferenceFamily =
                            ApiSpecReferenceFamily.MicrosoftNetCoreReferencePack
                    }
                ]
            }
        };
        Assert.That(
            ApiSpecTable.Create([declaration]).ContentSha256,
            Is.Not.EqualTo(ApiSpecTable.Create([changed]).ContentSha256));
    }

    [Test]
    public void ApprovedReferenceFamiliesMustBeDefined()
    {
        var declaration = Declaration("row", "M:Missing.Row.Run", "Missing.Row");
        declaration = declaration with
        {
            Target = declaration.Target with
            {
                ApprovedAssemblies = [
                    RuntimeAssemblyIdentity() with {
                        ReferenceFamily = (ApiSpecReferenceFamily)int.MaxValue
                    }
                ]
            }
        };

        Assert.Throws<ArgumentException>(() => ApiSpecTable.Create([declaration]));
    }

    [Test]
    public void ApprovedAssemblyTokensAreUniqueIgnoringHexCase()
    {
        var declaration = Declaration("duplicate-token", "M:Missing.Row.Run", "Missing.Row");
        var identity = RuntimeAssemblyIdentity();
        declaration = declaration with
        {
            Target = declaration.Target with
            {
                ApprovedAssemblies = [
                    identity,
                    identity with { PublicKeyToken = identity.PublicKeyToken.ToUpperInvariant() }
                ]
            }
        };

        Assert.Throws<ArgumentException>(() => ApiSpecTable.Create([declaration]));
    }

    [Test]
    public void SpecTypesAndOperatorsFailClosedOnUndefinedIrVocabulary()
    {
        var invalidType = Declaration(
            "invalid-type",
            "M:Missing.InvalidType.Run(System.Int32)",
            "Missing.InvalidType",
            parameterTypes: [(IrTypeKind)int.MaxValue]);
        var invalidOperator = Declaration(
            "invalid-operator",
            "M:Missing.InvalidOperator.Run(System.Int32)",
            "Missing.InvalidOperator") with
        {
            Postconditions = [
                new SpecPostconditionDeclaration(
                    new SpecBinaryDeclaration(
                        (IrBinaryOperator)int.MaxValue,
                        new SpecIntegerDeclaration(1),
                        new SpecIntegerDeclaration(1),
                        IrTypeKind.Boolean),
                    new SpecEvidence(
                        SpecEvidenceKind.Observed,
                        "invalid-operator"))
            ]
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ApiSpecTable.Create([invalidType]));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ApiSpecTable.Create([invalidOperator]));
        }
    }

    [Test]
    public void DefaultBclCatalogApprovesOnlyObservedFrameworkIdentities()
    {
        var expected = new[] {
            "System.Collections|b03f5f7f11d50a3a|MicrosoftNetCoreReferencePack",
            "System.Collections|b03f5f7f11d50a3a|NetFrameworkReferenceAssemblies",
            "System.Collections|b03f5f7f11d50a3a|NetStandardReferencePack",
            "System.Core|b77a5c561934e089|MicrosoftNetCoreReferencePack",
            "System.Core|b77a5c561934e089|NetFrameworkReferenceAssemblies",
            "System.Core|b77a5c561934e089|NetStandardReferencePack",
            "System.Linq|b03f5f7f11d50a3a|MicrosoftNetCoreReferencePack",
            "System.Linq|b03f5f7f11d50a3a|MicrosoftNetCoreRuntime",
            "System.Linq|b03f5f7f11d50a3a|NetFrameworkReferenceAssemblies",
            "System.Linq|b03f5f7f11d50a3a|NetStandardReferencePack",
            "System.Private.CoreLib|7cec85d7bea7798e|MicrosoftNetCoreRuntime",
            "System.Runtime|b03f5f7f11d50a3a|MicrosoftNetCoreReferencePack",
            "System.Runtime|b03f5f7f11d50a3a|NetFrameworkReferenceAssemblies",
            "System.Runtime|b03f5f7f11d50a3a|NetStandardReferencePack",
            "mscorlib|b77a5c561934e089|MicrosoftNetCoreReferencePack",
            "mscorlib|b77a5c561934e089|NetFrameworkReferenceAssemblies",
            "mscorlib|b77a5c561934e089|NetStandardReferencePack",
            "netstandard|cc7b13ffcd2ddd51|MicrosoftNetCoreReferencePack",
            "netstandard|cc7b13ffcd2ddd51|NetStandardReferencePack"
        };

        foreach (var template in ApiSpecTable.Default.Templates.Where(
                     static value => value.Target.WitnessIdentifier.StartsWith(
                         "bcl.",
                         StringComparison.Ordinal)))
        {
            Assert.That(
                template.Target.ApprovedAssemblies
                    .Select(static value =>
                        value.Name + "|" + value.PublicKeyToken + "|" +
                        value.ReferenceFamily)
                    .OrderBy(static value => value, StringComparer.Ordinal),
                Is.EqualTo(expected),
                template.Target.WitnessIdentifier);
        }
    }

    [Test]
    public void TemplatesInstantiateIntoIndependentIrFactoriesBySpecVariableIdentity()
    {
        var template = ApiSpecTable.Default.Templates.Single(
            static row => row.Target.WitnessIdentifier == "bcl.string.length");
        var firstFactory = new IrFactory();
        var secondFactory = new IrFactory();
        var first = InstantiateStringLength(template, firstFactory);
        var second = InstantiateStringLength(template, secondFactory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
            Assert.That(second.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
            Assert.That(first.Postconditions.Single().Id, Is.Not.EqualTo(second.Postconditions.Single().Id));
            Assert.That(
                new IrPrinter(firstFactory).Print(first.Postconditions.Single()),
                Is.EqualTo("(v1 == len(v0))"));
            Assert.That(
                new IrPrinter(secondFactory).Print(second.Postconditions.Single()),
                Is.EqualTo("(v1 == len(v0))"));
            Assert.That(
                typeof(ApiSpecTemplate).GetProperties()
                    .Any(static property => property.PropertyType.Assembly == typeof(IrTerm).Assembly),
                Is.False);
        }
    }

    [Test]
    public void InstantiationFailsClosedWhenAReferencedVariableIsMissing()
    {
        var template = ApiSpecTable.Default.Templates.Single(
            static row => row.Target.WitnessIdentifier == "bcl.math.abs.int32");
        var result = ApiSpecInstantiator.InstantiatePostconditions(
            template,
            new IrFactory(),
            ImmutableDictionary<SpecVarId, IrTerm>.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(SpecInstantiationStatus.Failed));
            Assert.That(result.Failure!.Kind, Is.EqualTo(SpecInstantiationFailureKind.MissingSubstitution));
            Assert.That(result.Postconditions, Is.Empty);
        }
    }

    [Test]
    public void DefaultRowsResolveOnceToOriginalFrameworkDefinitions()
    {
        var compilation = CreatePlatformCompilation();
        var resolver = new ApiSpecResolver(ApiSpecTable.Default);
        var first = resolver.Resolve(compilation);
        var second = resolver.Resolve(compilation);
        var length = compilation.GetSpecialType(SpecialType.System_String)
            .GetMembers("Length")
            .OfType<IPropertySymbol>()
            .Single()
            .GetMethod!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.IsComplete, Is.True, string.Join(
                Environment.NewLine,
                first.Failures.Select(static failure => failure.WitnessIdentifier + ": " + failure.Detail)));
            Assert.That(first.Specs.Length, Is.EqualTo(ApiSpecTable.Default.Templates.Length));
            Assert.That(first.TryGet(length, out var spec), Is.True);
            Assert.That(spec!.Template.Target.WitnessIdentifier, Is.EqualTo("bcl.string.length"));
            Assert.That(spec.Symbol, Is.EqualTo(length.OriginalDefinition).Using(SymbolEqualityComparer.Default));
        }
    }

    [Test]
    public void ImpossibleConstructorAndPropertyShapesAreRejectedByTheTable()
    {
        var constructor = ApiSpecTable.Default.Templates.Single(
            static row => row.Target.WitnessIdentifier == "bcl.exception.ctor");
        var property = ApiSpecTable.Default.Templates.Single(
            static row => row.Target.WitnessIdentifier == "bcl.string.length");
        var staticConstructor = DeclarationWithTarget(
            constructor,
            constructor.Target with
            {
                IsStatic = true,
                ReceiverType = null
            });
        var genericProperty = DeclarationWithTarget(
            property,
            property.Target with { GenericArity = 1 });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () => ApiSpecTable.Create([staticConstructor]),
                Throws.ArgumentException.With.Message.Contains(
                    "constructors must be instance members"));
            Assert.That(
                () => ApiSpecTable.Create([genericProperty]),
                Throws.ArgumentException.With.Message.Contains(
                    "properties cannot declare generic arity"));
        }
    }

    [Test]
    public void ResolverDoesNotMatchImpossibleConstructorAndPropertyShapes()
    {
        var compilation = CreatePlatformCompilation();
        var constructor = compilation.GetTypeByMetadataName("System.Exception")!
            .InstanceConstructors.Single(static method => method.Parameters.Length == 0);
        var property = compilation.GetSpecialType(SpecialType.System_String)
            .GetMembers("Length")
            .OfType<IPropertySymbol>()
            .Single();
        var constructorTarget = ApiSpecTable.Default.Templates.Single(
            static row => row.Target.WitnessIdentifier == "bcl.exception.ctor")
            .Target with
        {
            IsStatic = true,
            ReceiverType = null
        };
        var propertyTarget = ApiSpecTable.Default.Templates.Single(
            static row => row.Target.WitnessIdentifier == "bcl.string.length")
            .Target with
        { GenericArity = 1 };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ResolverMatchesTarget(constructor, constructorTarget), Is.False);
            Assert.That(ResolverMatchesTarget(property, propertyTarget), Is.False);
        }
    }

    [Test]
    public void SharpProofPackageSpecsResolveAgainstGenuineAttributes()
    {
        var reference = MetadataReference.CreateFromFile(
            typeof(Contract).Assembly.Location);

        var resolved = ResolveContractRequires(reference, string.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Failures, Is.Empty);
            Assert.That(
                resolved.Specs.Single().Template.Target.WitnessIdentifier,
                Is.EqualTo("contract.requires.test"));
        }
    }

    [Test]
    public void AuthenticatedPackageSpecResolutionChecksConditionalElisionShape()
    {
        var reference = MetadataReference.CreateFromFile(
            typeof(Contract).Assembly.Location);
        var compilation = CSharpCompilation.Create(
            "AuthenticatedContractSpecConsumer",
            references: PlatformReferences().Append(reference),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var resolved = new ApiSpecResolver(ApiSpecTable.Create([
            ContractRequiresDeclaration(string.Empty)
        ])).Resolve(compilation);
        var contract = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.Contract");
        var requires = contract!.GetMembers("Requires")
            .OfType<IMethodSymbol>()
            .Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Failures, Is.Empty);
            Assert.That(resolved.Specs, Has.Length.EqualTo(1));
            Assert.That(
                requires.GetAttributes()
                    .Where(attribute => attribute.AttributeClass?.ToDisplayString() ==
                        "System.Diagnostics.ConditionalAttribute")
                    .SelectMany(attribute => attribute.ConstructorArguments)
                    .Select(argument => argument.Value),
                Is.EquivalentTo(new object[] { Contract.ConditionalSymbol }));
        }
    }

    [Test]
    public void SharpProofPackageSpecsRejectContractWithoutConditionalElision()
    {
        AssertSharpProofPackageSpecRejected(
            () => CreateSharpProofPackageReference(
                CreateContractSource(
                    typeof(Contract).Assembly.GetName().Version!,
                    includeConditionalAttributes: false)));
    }

    [Test]
    public void SharpProofPackageSpecsRejectVersionMismatch()
    {
        AssertSharpProofPackageSpecRejected(
            () => CreateSharpProofPackageReference(
                CreateContractSource(
                    new Version(9, 0, 0, 0),
                    includeConditionalAttributes: true)));
    }

    [Test]
    public void SharpProofPackageSpecsRejectPublicKeyMismatch()
    {
        var publicKey = typeof(object).Assembly.GetName().GetPublicKey();
        Assert.That(publicKey, Is.Not.Null.And.Not.Empty);
        AssertSharpProofPackageSpecRejected(
            () => CreateSharpProofPackageReference(
                CreateContractSource(
                    typeof(Contract).Assembly.GetName().Version!,
                    includeConditionalAttributes: true),
                [.. publicKey!]),
            GetPublicKeyToken);
    }

    [Test]
    public void SharpProofPackageSpecsRejectMatchingIdentityAndContractShapeFromAnotherPayload()
    {
        AssertSharpProofPackageSpecRejected(
            () => CreateSharpProofPackageReference(
                CreateContractSource(
                    typeof(Contract).Assembly.GetName().Version!,
                    includeConditionalAttributes: true)));
    }

    [TestCase("netstandard2.0")]
    [TestCase("net8.0")]
    [TestCase("net472")]
    public void DefaultRowsResolveAgainstEverySupportedReferenceSurface(
        string targetFramework)
    {
        var resolved = new ApiSpecResolver(ApiSpecTable.Default).Resolve(
            CreateTargetFrameworkCompilation(targetFramework));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                resolved.Failures,
                Is.Empty,
                targetFramework + Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    resolved.Failures.Select(static failure =>
                        failure.WitnessIdentifier + ": " + failure.Detail)));
            Assert.That(
                resolved.Specs.Length,
                Is.EqualTo(ApiSpecTable.Default.Templates.Length),
                targetFramework);
        }
    }

    [Test]
    public void MissingTypesAndMembersProduceTypedResolutionFailures()
    {
        var compilation = CreatePlatformCompilation();
        var missingType = new ApiSpecResolver(ApiSpecTable.Create([
            Declaration("missing-type", "M:Missing.Widget.Run", "Missing.Widget")
        ])).Resolve(compilation);
        var missingMember = new ApiSpecResolver(ApiSpecTable.Create([
            Declaration("missing-member", "M:System.Object.NotReal", "System.Object")
        ])).Resolve(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                missingType.Failures.Single().Kind,
                Is.EqualTo(ApiSpecResolutionFailureKind.MissingContainingType));
            Assert.That(
                missingMember.Failures.Single().Kind,
                Is.EqualTo(ApiSpecResolutionFailureKind.MissingMember));
            Assert.That(missingType.Specs, Is.Empty);
            Assert.That(missingMember.Specs, Is.Empty);
        }
    }

    [Test]
    public void DuplicateMetadataTypesProduceAnAmbiguousFailure()
    {
        var first = CreateReference(
            "DuplicateOne",
            "namespace Duplicate { public static class Widget { public static int Run() => 1; } }");
        var second = CreateReference(
            "DuplicateTwo",
            "namespace Duplicate { public static class Widget { public static int Run() => 2; } }");
        var compilation = CSharpCompilation.Create(
            "AmbiguousConsumer",
            references: [CoreReference, first, second],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var table = ApiSpecTable.Create([
            Declaration("ambiguous", "M:Duplicate.Widget.Run", "Duplicate.Widget", parameterTypes: [])
        ]);
        var resolved = new ApiSpecResolver(table).Resolve(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Specs, Is.Empty);
            Assert.That(
                resolved.Failures.Single().Kind,
                Is.EqualTo(ApiSpecResolutionFailureKind.AmbiguousContainingType));
        }
    }

    [Test]
    public void ResolverRejectsATypeFromAnUnapprovedAssemblyIdentity()
    {
        var reference = CreateReference(
            "UnapprovedApi",
            "namespace Trusted { public static class Widget { public static int Run(int value) => value; } }");
        var compilation = CSharpCompilation.Create(
            "IdentityConsumer",
            references: [CoreReference, reference],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var declaration = Declaration(
            "trusted-row",
            "M:Trusted.Widget.Run(System.Int32)",
            "Trusted.Widget",
            approvedAssemblies: [new ApiSpecAssemblyIdentity("ApprovedApi", string.Empty)]);

        var resolved = new ApiSpecResolver(ApiSpecTable.Create([declaration]))
            .Resolve(compilation);

        Assert.That(resolved.Specs, Is.Empty);
        Assert.That(resolved.Failures, Has.One.Items);
        Assert.That(
            resolved.Failures[0].Kind,
            Is.EqualTo(ApiSpecResolutionFailureKind.UnapprovedContainingAssembly));
    }

    [Test]
    public void ResolverRejectsAnApprovedIdentityFromAnUnapprovedReferenceFamily()
    {
        var reference = CreateReference(
            "ApprovedApi",
            "namespace Trusted { public static class Widget { public static int Run(int value) => value; } }");
        var compilation = CSharpCompilation.Create(
            "FamilyConsumer",
            references: [CoreReference, reference],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var declaration = Declaration(
            "trusted-row",
            "M:Trusted.Widget.Run(System.Int32)",
            "Trusted.Widget",
            approvedAssemblies: [
                new ApiSpecAssemblyIdentity(
                    "ApprovedApi",
                    string.Empty,
                    ApiSpecReferenceFamily.MicrosoftNetCoreReferencePack)
            ]);

        var resolved = new ApiSpecResolver(ApiSpecTable.Create([declaration]))
            .Resolve(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Specs, Is.Empty);
            Assert.That(
                resolved.Failures.Single().Kind,
                Is.EqualTo(ApiSpecResolutionFailureKind.UnapprovedReferenceFamily));
        }
    }

    [Test]
    public void ResolverRejectsARuntimeAssemblyCopiedIntoAReferencePackPath()
    {
        var root = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "packs",
            "Microsoft.NETCore.App.Ref",
            Guid.NewGuid().ToString("N"));
        var referenceDirectory = Path.Combine(root, "ref", "net8.0");
        Directory.CreateDirectory(referenceDirectory);
        var path = Path.Combine(referenceDirectory, "System.Private.CoreLib.dll");
        File.Copy(typeof(object).Assembly.Location, path);
        try
        {
            var compilation = CSharpCompilation.Create(
                "SpoofedReferenceFamily",
                references: [MetadataReference.CreateFromFile(path)],
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var declaration = Declaration(
                "spoofed-reference-family",
                "M:System.Math.Abs(System.Int32)",
                "System.Math",
                memberName: "Abs",
                approvedAssemblies: [
                    RuntimeAssemblyIdentity() with {
                        ReferenceFamily =
                            ApiSpecReferenceFamily.MicrosoftNetCoreReferencePack
                    }
                ]);

            var resolved = new ApiSpecResolver(ApiSpecTable.Create([declaration]))
                .Resolve(compilation);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resolved.Specs, Is.Empty);
                Assert.That(
                    resolved.Failures.Single().Kind,
                    Is.EqualTo(ApiSpecResolutionFailureKind.UnapprovedReferenceFamily));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void TrustedPostconditionsMustBeTotalUnderNormalReturnFacts()
    {
        var evidence = new SpecEvidence(SpecEvidenceKind.Documented, "totality-test");
        var parameter = new SpecVariableDeclaration(
            SpecVariableRole.Parameter, 0, IrTypeKind.Integer);
        var result = new SpecVariableDeclaration(
            SpecVariableRole.Result, -1, IrTypeKind.Integer);
        var partial = new SpecBinaryDeclaration(
            IrBinaryOperator.Equal,
            result,
            new SpecBinaryDeclaration(
                IrBinaryOperator.Add,
                parameter,
                new SpecIntegerDeclaration(1),
                IrTypeKind.Integer),
            IrTypeKind.Boolean);
        var declaration = Declaration("partial", "M:Missing.Partial.Run(System.Int32)", "Missing.Partial")
            with
        {
            Postconditions = [new SpecPostconditionDeclaration(partial, evidence)]
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            ApiSpecTable.Create([declaration]));

        Assert.That(exception!.Message, Does.Contain("must be total"));
    }

    [Test]
    public void ConstantArithmeticPostconditionsAreAcceptedOnlyWhenDefined()
    {
        var evidence = new SpecEvidence(SpecEvidenceKind.Documented, "totality-test");
        ApiSpecDeclaration WithRight(long divisor)
        {
            var quotient = new SpecBinaryDeclaration(
                IrBinaryOperator.Divide,
                new SpecIntegerDeclaration(12),
                new SpecIntegerDeclaration(divisor),
                IrTypeKind.Integer);
            var condition = new SpecBinaryDeclaration(
                IrBinaryOperator.Equal,
                quotient,
                new SpecIntegerDeclaration(3),
                IrTypeKind.Boolean);
            return Declaration(
                "constant-" + divisor,
                "M:Missing.Constant.Run(System.Int32)",
                "Missing.Constant") with
            {
                Postconditions = [new SpecPostconditionDeclaration(condition, evidence)]
            };
        }

        Assert.That(ApiSpecTable.Create([WithRight(4)]).Templates, Has.Length.EqualTo(1));
        Assert.Throws<ArgumentException>(() => ApiSpecTable.Create([WithRight(0)]));
    }

    [Test]
    public void DuplicateRowsForOneSymbolAreRemovedRatherThanTrusted()
    {
        var table = ApiSpecTable.Create([
            Declaration(
                "abs-first",
                "M:System.Math.Abs(System.Int32)",
                "System.Math",
                memberName: "Abs"),
            Declaration(
                "abs-second",
                "M:System.Math.Abs(System.Int32)",
                "System.Math",
                memberName: "Abs")
        ]);
        var resolved = new ApiSpecResolver(table).Resolve(CreatePlatformCompilation());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Specs, Is.Empty);
            Assert.That(resolved.Failures.Length, Is.EqualTo(2));
            Assert.That(
                resolved.Failures,
                Has.All.Property(nameof(ApiSpecResolutionFailure.Kind))
                    .EqualTo(ApiSpecResolutionFailureKind.DuplicateResolvedSymbol));
        }
    }

    [Test]
    public void UnspecifiedMembersAndUncertainFacetsRemainConservativeUnknowns()
    {
        var compilation = CreatePlatformCompilation();
        var resolved = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        var toUpper = compilation.GetSpecialType(SpecialType.System_String)
            .GetMembers("ToUpper")
            .OfType<IMethodSymbol>()
            .Single(static method => method.Parameters.Length == 0);
        var lookup = resolved.Lookup(toUpper);
        var empty = ApiSpecTable.Default.Templates.Single(
            static row => row.Target.WitnessIdentifier == "bcl.enumerable.empty");
        var cachedEmptyRows = ApiSpecTable.Default.Templates.Where(static row =>
            row.Target.WitnessIdentifier is "bcl.array.empty" or
                "bcl.enumerable.empty");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lookup.Status, Is.EqualTo(ApiSpecLookupStatus.Unknown));
            Assert.That(lookup.Failure!.Kind, Is.EqualTo(ApiSpecLookupFailureKind.UnspecifiedMember));
            Assert.That(
                empty.Facets.Allocation.Behavior,
                Is.EqualTo(SpecAllocationBehavior.Unknown));
            Assert.That(
                cachedEmptyRows.Select(static row =>
                    row.Facets.Effects.Effects),
                Is.All.EqualTo(SpecEffect.Unknown));
        }
    }

    [Test]
    public void ExceptionConstructorSpecsRequireAnExactMemberMatch()
    {
        var compilation = CreatePlatformCompilation();
        var resolved = new ApiSpecResolver(ApiSpecTable.Default)
            .Resolve(compilation);
        var exception = compilation.GetTypeByMetadataName("System.Exception")!;
        var invalidOperation = compilation.GetTypeByMetadataName(
            "System.InvalidOperationException")!;
        var aggregate = compilation.GetTypeByMetadataName(
            "System.AggregateException")!;
        var supported = exception.InstanceConstructors
            .Concat(invalidOperation.InstanceConstructors)
            .Where(static constructor =>
                constructor.Parameters.Length == 0 ||
                constructor.Parameters is [
                {
                    Type.SpecialType: SpecialType.System_String
                }])
            .ToArray();
        var aggregateEnumerable = aggregate.InstanceConstructors.Single(
            static constructor =>
                constructor.Parameters is [
                    {
                        Type: INamedTypeSymbol
                        {
                            MetadataName: "IEnumerable`1"
                        }
                    }]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(supported, Has.Length.EqualTo(4));
            Assert.That(
                supported.All(constructor =>
                    resolved.TryGet(constructor, out var spec) &&
                    spec.Template.Facets.Throws.Behavior ==
                    SpecThrowBehavior.DoesNotThrow &&
                    spec.Template.Facets.Termination?.Behavior ==
                    SpecTerminationBehavior.Terminates &&
                    spec.Template.Facets.Effects.Effects ==
                    SpecEffect.WritesReceiverState),
                Is.True);
            Assert.That(
                resolved.Lookup(aggregateEnumerable).Status,
                Is.EqualTo(ApiSpecLookupStatus.Unknown));
        }
    }

    [Test]
    public void PureOpaqueEligibilityComesOnlyFromResolvedSpecFacets()
    {
        var compilation = CreatePlatformCompilation();
        var resolved = new ApiSpecResolver(ApiSpecTable.Default)
            .Resolve(compilation);
        var abs = compilation.GetTypeByMetadataName("System.Math")!
            .GetMembers("Abs")
            .OfType<IMethodSymbol>()
            .Single(static method =>
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType ==
                    SpecialType.System_Int32);
        var concat = compilation.GetSpecialType(SpecialType.System_String)
            .GetMembers("Concat")
            .OfType<IMethodSymbol>()
            .Single(static method =>
                method.Parameters.Length == 2 &&
                method.Parameters.All(parameter =>
                    parameter.Type.SpecialType ==
                        SpecialType.System_String));
        var arrayEmpty = compilation.GetTypeByMetadataName("System.Array")!
            .GetMembers("Empty")
            .OfType<IMethodSymbol>()
            .Single(static method =>
                method.IsGenericMethod &&
                method.Parameters.Length == 0);
        var enumerableEmpty = compilation
            .GetTypeByMetadataName("System.Linq.Enumerable")!
            .GetMembers("Empty")
            .OfType<IMethodSymbol>()
            .Single(static method =>
                method.IsGenericMethod &&
                method.Parameters.Length == 0);
        var toUpper = compilation.GetSpecialType(SpecialType.System_String)
            .GetMembers("ToUpper")
            .OfType<IMethodSymbol>()
            .Single(static method => method.Parameters.Length == 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.IsPureAndAllocationFree(abs), Is.True);
            Assert.That(resolved.IsPureAndAllocationFree(concat), Is.False);
            Assert.That(
                resolved.IsPureAndAllocationFree(arrayEmpty),
                Is.False);
            Assert.That(
                resolved.IsPureAndAllocationFree(enumerableEmpty),
                Is.False);
            Assert.That(resolved.IsPureAndAllocationFree(toUpper), Is.False);
            Assert.That(resolved.IsSideEffectFree(abs), Is.True);
            Assert.That(resolved.IsSideEffectFree(concat), Is.True);
            Assert.That(resolved.IsSideEffectFree(arrayEmpty), Is.False);
            Assert.That(resolved.IsSideEffectFree(enumerableEmpty), Is.False);
            Assert.That(resolved.IsSideEffectFree(toUpper), Is.False);
        }
    }

    [Test]
    public void EverySeedHasAUniqueWitnessAndResolvableDocumentationIdentifier()
    {
        var templates = ApiSpecTable.Default.Templates;
        var compilation = CreatePlatformCompilation();
        var resolved = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(templates.Length, Is.EqualTo(16));
            Assert.That(
                templates.Select(static row => row.Target.WitnessIdentifier),
                Is.Unique.And.All.Not.Empty);
            Assert.That(
                templates.Select(static row => row.Target.DocumentationCommentId),
                Has.All.Not.Empty);
            Assert.That(resolved.Failures, Is.Empty);
        }
    }

    [Test]
    public void SeedFacetsRetainDocumentedAndObservedEvidence()
    {
        var evidence = ApiSpecTable.Default.Templates.SelectMany(static row => new[] {
            row.Facets.Effects.Evidence,
            row.Facets.Allocation.Evidence,
            row.Facets.Throws.Evidence,
            row.Facets.Nullness.Evidence,
            row.Facets.Cardinality.Evidence
        }).Concat(ApiSpecTable.Default.Templates
            .Where(static row => row.Facets.Termination != null)
            .Select(static row => row.Facets.Termination!.Evidence))
        .Concat(ApiSpecTable.Default.Templates.SelectMany(
            static row => row.Postconditions.Select(static postcondition => postcondition.Evidence)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(evidence.Select(static item => item.Kind), Does.Contain(SpecEvidenceKind.Documented));
            Assert.That(evidence.Select(static item => item.Kind), Does.Contain(SpecEvidenceKind.Observed));
            Assert.That(evidence.Select(static item => item.Source), Has.All.Not.Empty);
        }
    }

    private static SpecInstantiationResult InstantiateStringLength(
        ApiSpecTemplate template,
        IrFactory factory)
    {
        var receiver = factory.CreateVariable("receiver", factory.StringType);
        var result = factory.CreateVariable("result", factory.IntegerType);
        return ApiSpecInstantiator.InstantiatePostconditions(
            template,
            factory,
            new Dictionary<SpecVarId, IrTerm>
            {
                [template.Receiver!.Value] = factory.Variable(receiver),
                [template.Result!.Value] = factory.Variable(result)
            });
    }

    private static ApiSpecDeclaration Declaration(
        string witness,
        string documentationId,
        string containingType,
        string memberName = "Run",
        ImmutableArray<IrTypeKind>? parameterTypes = null,
        ImmutableArray<ApiSpecAssemblyIdentity>? approvedAssemblies = null)
    {
        var evidence = new SpecEvidence(SpecEvidenceKind.Observed, "test-witness");
        return new ApiSpecDeclaration(
            new ApiSpecTarget(
                witness,
                documentationId,
                containingType,
                SpecTargetMemberKind.Method,
                memberName,
                true,
                0,
                null,
                parameterTypes ?? [IrTypeKind.Integer],
                IrTypeKind.Integer,
                approvedAssemblies ?? [RuntimeAssemblyIdentity()]),
            new ApiSpecFacets(
                new SpecEffectFacet(SpecEffect.Unknown, evidence),
                new SpecAllocationFacet(SpecAllocationBehavior.Unknown, evidence),
                new SpecThrowFacet(SpecThrowBehavior.Unknown, [], evidence),
                new SpecNullnessFacet(SpecNullness.Unknown, evidence),
                new SpecCardinalityFacet(SpecCardinality.Unknown, null, evidence)),
            []);
    }

    private static ApiSpecDeclaration DeclarationWithTarget(
        ApiSpecTemplate template,
        ApiSpecTarget target)
    {
        return new ApiSpecDeclaration(
            target,
            template.Facets,
            [.. template.Postconditions.Select(static postcondition =>
                new SpecPostconditionDeclaration(
                    postcondition.Condition,
                    postcondition.Evidence))]);
    }

    private static bool ResolverMatchesTarget(ISymbol symbol, ApiSpecTarget target)
    {
        var method = typeof(ApiSpecResolver).GetMethod(
            "MatchesTarget",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        return (bool)method!.Invoke(null, [symbol, target])!;
    }

    private static void AssertSharpProofPackageSpecRejected(
        Func<(string Root, PortableExecutableReference Reference)> createPackage,
        Func<PortableExecutableReference, string>? getPublicKeyToken = null)
    {
        var package = createPackage();
        try
        {
            var publicKeyToken = getPublicKeyToken is null
                ? string.Empty
                : getPublicKeyToken(package.Reference);
            if (getPublicKeyToken is not null)
            {
                Assert.That(publicKeyToken, Is.Not.Empty);
            }

            var resolved = ResolveContractRequires(
                package.Reference,
                publicKeyToken);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resolved.Specs, Is.Empty);
                Assert.That(
                    resolved.Failures.Single().Kind,
                    Is.EqualTo(
                        ApiSpecResolutionFailureKind
                            .UnapprovedReferenceFamily));
            }
        }
        finally
        {
            Directory.Delete(package.Root, recursive: true);
        }
    }

    private static ApiSpecAssemblyIdentity RuntimeAssemblyIdentity()
    {
        var name = typeof(object).Assembly.GetName();
        return new ApiSpecAssemblyIdentity(
            name.Name!,
            HashEncoding.ToLowerHex(name.GetPublicKeyToken() ?? []));
    }

    private static ResolvedApiSpecTable ResolveContractRequires(
        MetadataReference attributesReference,
        string publicKeyToken)
    {
        var compilation = CSharpCompilation.Create(
            "ContractSpecConsumer",
            references: PlatformReferences().Append(
                attributesReference),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        return new ApiSpecResolver(ApiSpecTable.Create([
            ContractRequiresDeclaration(publicKeyToken)
        ])).Resolve(compilation);
    }

    private static ApiSpecDeclaration ContractRequiresDeclaration(
        string publicKeyToken)
    {
        var evidence = new SpecEvidence(
            SpecEvidenceKind.Documented,
            "test-contract-semantics");
        return new ApiSpecDeclaration(
            new ApiSpecTarget(
                "contract.requires.test",
                "M:SharpProof.Attributes.Contract.Requires(System.Boolean)",
                "SharpProof.Attributes.Contract",
                SpecTargetMemberKind.Method,
                "Requires",
                true,
                0,
                null,
                [IrTypeKind.Boolean],
                null,
                [
                    new ApiSpecAssemblyIdentity(
                        "SharpProof.Attributes",
                        publicKeyToken,
                        ApiSpecReferenceFamily.SharpProofPackage)
                ]),
            NeutralFacets(evidence),
            []);
    }

    private static string CreateContractSource(
        Version assemblyVersion,
        bool includeConditionalAttributes)
    {
        var conditional = includeConditionalAttributes
            ? "[Conditional(ConditionalSymbol)]"
            : string.Empty;
        return $$"""
            using System.Diagnostics;
            using System.Reflection;

            [assembly: AssemblyVersion("{{assemblyVersion}}")]

            namespace SharpProof.Attributes;

            public static class Contract
            {
                public const string ConditionalSymbol =
                    "SHARPPROOF_CONTRACTS";

                {{conditional}}
                public static void Requires(bool condition)
                {
                }

                {{conditional}}
                public static void Ensures(bool condition)
                {
                }

                {{conditional}}
                public static void Assume(bool condition)
                {
                }

                public static T Result<T>()
                {
                    return default;
                }

                public static T Old<T>(T value)
                {
                    return value;
                }
            }
            """;
    }

    private static (
        string Root,
        PortableExecutableReference Reference)
        CreateSharpProofPackageReference(
            string source,
            ImmutableArray<byte> publicKey = default)
    {
        var root = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "SharpProofPackage",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "SharpProof.Attributes.dll");
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary);
        if (!publicKey.IsDefaultOrEmpty)
        {
            options = options
                .WithCryptoPublicKey(publicKey)
                .WithDelaySign(true);
        }

        var compilation = CSharpCompilation.Create(
            "SharpProof.Attributes",
            [CSharpSyntaxTree.ParseText(source)],
            [CoreReference],
            options);
        var emit = compilation.Emit(path);
        if (!emit.Success)
        {
            Directory.Delete(root, recursive: true);
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                emit.Diagnostics.Select(static diagnostic =>
                    diagnostic.ToString())));
        }

        return (
            root,
            MetadataReference.CreateFromFile(path));
    }

    private static string GetPublicKeyToken(
        MetadataReference reference)
    {
        var compilation = CSharpCompilation.Create(
            "AssemblyIdentityProbe",
            references: [CoreReference, reference],
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var symbol = compilation.GetAssemblyOrModuleSymbol(reference)
            as IAssemblySymbol ??
            throw new InvalidOperationException(
                "The test reference did not resolve to an assembly.");
        return string.Concat(symbol.Identity.PublicKeyToken.Select(
            static value => value.ToString(
                "x2",
                System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static CSharpCompilation CreatePlatformCompilation()
    {
        var references = PlatformReferences().Append(
            MetadataReference.CreateFromFile(
                typeof(Contract).Assembly.Location));
        return CSharpCompilation.Create(
            "ApiSpecTests",
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    private static IEnumerable<MetadataReference> PlatformReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(static path => !string.Equals(
                Path.GetFileName(path),
                "SharpProof.Attributes.dll",
                StringComparison.OrdinalIgnoreCase))
            .Select(static path =>
                (MetadataReference)MetadataReference.CreateFromFile(path));
    }

    private static CSharpCompilation CreateTargetFrameworkCompilation(
        string targetFramework)
    {
        var referenceDirectory = targetFramework switch
        {
            "netstandard2.0" or "net472" => Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "ReferencePacks",
                targetFramework),
            "net8.0" => FindNet8ReferenceDirectory(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(targetFramework),
                targetFramework,
                "Unsupported target framework.")
        };
        if (!Directory.Exists(referenceDirectory))
        {
            throw new InvalidOperationException(
                "Reference directory was not found: " + referenceDirectory);
        }

        var references = Directory
            .EnumerateFiles(
                referenceDirectory,
                "*.dll",
                SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(
                typeof(Contract).Assembly.Location));
        return CSharpCompilation.Create(
            "ApiSpec." + targetFramework,
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    private static string FindNet8ReferenceDirectory()
    {
        var runtimeDirectory = new DirectoryInfo(
            RuntimeEnvironment.GetRuntimeDirectory());
        var dotnetRoot = runtimeDirectory.Parent?.Parent?.Parent ??
            throw new InvalidOperationException(
                "The dotnet installation root could not be located.");
        var packRoot = Path.Combine(
            dotnetRoot.FullName,
            "packs",
            "Microsoft.NETCore.App.Ref");
        var packageRoot = Environment.GetEnvironmentVariable(
            "NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            packageRoot = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        }
        var candidates = new[]
            {
                packRoot,
                Path.Combine(packageRoot, "microsoft.netcore.app.ref")
            }
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateDirectories)
            .Select(path => new
            {
                Path = path,
                Version = Version.TryParse(
                    Path.GetFileName(path),
                    out var version)
                    ? version
                    : null
            })
            .Where(static candidate =>
                candidate.Version?.Major == 8)
            .OrderByDescending(static candidate => candidate.Version)
            .Select(static candidate => Path.Combine(
                candidate.Path,
                "ref",
                "net8.0"))
            .Where(Directory.Exists)
            .FirstOrDefault();
        return candidates ??
            throw new InvalidOperationException(
                "A .NET 8 reference pack could not be located.");
    }

    private static PortableExecutableReference CreateReference(
        string assemblyName,
        string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            [CoreReference],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                emit.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        }

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static MetadataReference CoreReference
    {
        get;
    } =
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
}
