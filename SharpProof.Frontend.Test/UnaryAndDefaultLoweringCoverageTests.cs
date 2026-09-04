using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class UnaryAndDefaultLoweringCoverageTests
{
    [Test]
    public void BooleanAndIntegerDefaultsLowerToExactScalarConstants()
    {
        var boolean = Lower(
            "private static bool Target() => default(bool);");
        var integer = Lower(
            "private static long Target() => default(long);");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(boolean.IsExact, Is.True);
            Assert.That(
                ((IrBooleanTerm)boolean.Term).Value,
                Is.False);
            Assert.That(integer.IsExact, Is.True);
            Assert.That(
                ((IrIntegerTerm)integer.Term).Value,
                Is.Zero);
        }
    }

    [TestCase("private static string Target() => default(string);")]
    [TestCase("private sealed class Item {} private static Item Target() => default(Item);")]
    [TestCase("private static int[] Target() => default(int[]);")]
    [TestCase("private static T Target<T>() where T : class => default(T);")]
    public void ReferenceDefaultsLowerToExactNullConstants(string source)
    {
        var result = Lower(source);

        Assert.That(result.IsExact, Is.True);
        Assert.That(result.Term, Is.TypeOf<IrNullTerm>());
    }

    [TestCase("private static decimal Target() => default(decimal);")]
    [TestCase("private static System.DateTime Target() => default(System.DateTime);")]
    [TestCase("private static nint Target() => default(nint);")]
    [TestCase("private readonly struct Item {} private static Item Target() => default(Item);")]
    [TestCase("private enum State { None } private static State Target() => default(State);")]
    [TestCase("private static int? Target() => default(int?);")]
    [TestCase("private static T Target<T>() => default(T);")]
    public void UnsupportedValueTypeDefaultsFailClosed(string source)
    {
        AssertAbstention(Lower(source), FrontendAbstention.UnsupportedType);
    }

    [Test]
    public void SpecializedTypeParameterDefaultsUseTheConstructedDomain()
    {
        var text = Lower(
            "private static T Target<T>() => default(T);",
            SpecialType.System_String);
        var integer = Lower(
            "private static T Target<T>() => default(T);",
            SpecialType.System_Int64);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.IsExact, Is.True);
            Assert.That(text.Term, Is.TypeOf<IrNullTerm>());
            Assert.That(integer.IsExact, Is.True);
            Assert.That(
                ((IrIntegerTerm)integer.Term).Value,
                Is.Zero);
        }
    }

    [Test]
    public void SpecializedStringTypeParameterEqualityDoesNotChangeToValueEquality()
    {
        var result = Lower(
            """
            private static bool Target<T>(T left, T right)
                where T : class => left == right;
            """,
            SpecialType.System_String);

        AssertAbstention(result, FrontendAbstention.UnsupportedType);
    }

    [Test]
    public void UnaryScalarPoliciesDistinguishExactAndFailClosedCases()
    {
        var identity = Lower(
            "private static long Target(long value) => +value;");
        var checkedNegation = Lower(
            "private static long Target(long value) => checked(-value);");
        var uncheckedNegation = Lower(
            "private static long Target(long value) => -value;");
        var narrowerNegation = Lower(
            "private static int Target(int value) => checked(-value);");
        var unsupportedOperator = Lower(
            "private static long Target(long value) => ~value;");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(identity.IsExact, Is.True);
            Assert.That(identity.Term, Is.TypeOf<IrVariableTerm>());
            Assert.That(checkedNegation.IsExact, Is.True);
            Assert.That(checkedNegation.Term, Is.TypeOf<IrUnaryTerm>());
            AssertAbstention(
                uncheckedNegation,
                FrontendAbstention.UncheckedOverflowSemantics);
            AssertAbstention(
                narrowerNegation,
                FrontendAbstention.UnsupportedType);
            AssertAbstention(
                unsupportedOperator,
                FrontendAbstention.UnsupportedOperationKind);
        }
    }

    [Test]
    public void DirectLongMinValueLiteralNegationLowersToExactConstant()
    {
        var result = Lower(
            "private static long Target() => -9223372036854775808L;",
            concreteReplay: true);

        Assert.That(result.IsExact, Is.True);
        Assert.That(((IrIntegerTerm)result.Term).Value, Is.EqualTo(long.MinValue));
    }

    [TestCase("decimal")]
    [TestCase("float")]
    [TestCase("double")]
    [TestCase("nint")]
    [TestCase("nuint")]
    public void UnaryPlusOnUnsupportedScalarTypesFailsClosed(string type)
    {
        AssertAbstention(
            Lower($"private static {type} Target({type} value) => +value;"),
            FrontendAbstention.UnsupportedType);
    }

    [Test]
    public void UserDefinedAndNestedOpaqueOperatorsPreserveTypedReasons()
    {
        var userDefinedUnary = Lower(
            """
            private sealed class Box {
                public static Box operator -(Box value) => value;
            }
            private static Box Target(Box value) => -value;
            """);
        var userDefinedBinary = Lower(
            """
            private sealed class Box {
                public static Box operator +(Box left, Box right) => left;
            }
            private static Box Target(Box left, Box right) => left + right;
            """);
        var opaqueUnaryOperand = Lower(
            """
            private static long Read() => 1;
            private static long Target() => +Read();
            """);
        var opaqueConditionalArm = Lower(
            """
            private static long Read() => 1;
            private static long Target(bool condition) =>
                condition ? Read() : 0;
            """);

        using (Assert.EnterMultipleScope())
        {
            AssertAbstention(
                userDefinedUnary,
                FrontendAbstention.UserDefinedOperator);
            AssertAbstention(
                userDefinedBinary,
                FrontendAbstention.UserDefinedOperator);
            AssertAbstention(
                opaqueUnaryOperand,
                FrontendAbstention.UnsupportedInvocationShape);
            AssertAbstention(
                opaqueConditionalArm,
                FrontendAbstention.UnsupportedInvocationShape);
        }
    }

    private static void AssertAbstention(
        FrontendLoweringResult result,
        FrontendAbstention abstention)
    {
        Assert.That(result.IsExact, Is.False);
        Assert.That(
            result.Classification.Abstention,
            Is.EqualTo(abstention));
    }

    private static FrontendLoweringResult Lower(
        string members,
        SpecialType? specializedType = null,
        bool concreteReplay = false)
    {
        var tree = CSharpSyntaxTree.ParseText(
            FrontendTestHelpers.WrapSubjectMembers(members),
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create(
            "UnaryCoverage_" + Guid.NewGuid().ToString("N"),
            [tree],
            PlatformReferences,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                checkOverflow: false,
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
        var expression = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static method =>
                method.Identifier.ValueText == "Target")
            .ExpressionBody!
            .Expression;
        var operation = GetExpressionOperation(
            compilation.GetSemanticModel(tree),
            expression);
        var lowerer = concreteReplay
            ? RoslynOperationLowerer.CreateForConcreteReplay(new IrFactory())
            : new RoslynOperationLowerer(new IrFactory());
        if (specializedType.HasValue)
        {
            var replacement = compilation.GetSpecialType(
                specializedType.Value);
            lowerer.TypeSpecializer = type =>
                type is ITypeParameterSymbol ? replacement : type;
        }
        return lowerer.Lower(operation);
    }

    private static IOperation GetExpressionOperation(
        SemanticModel model,
        ExpressionSyntax expression)
    {
        return FrontendTestHelpers.TryGetExpressionOperation(
                model,
                expression) ??
            throw new InvalidOperationException(
                "Roslyn did not expose the target expression.");
    }

    private static ImmutableArray<MetadataReference>
        PlatformReferences
    {
        get;
    } =
        [.. ((string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path =>
                (MetadataReference)
                MetadataReference.CreateFromFile(path))];
}
