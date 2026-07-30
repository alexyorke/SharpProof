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

    [Test]
    public void ReferenceDefaultsLowerToExactNullConstants()
    {
        var text = Lower(
            "private static string Target() => default(string);");
        var instance = Lower(
            """
            private sealed class Item {}
            private static Item Target() => default(Item);
            """);
        var sequence = Lower(
            "private static int[] Target() => default(int[]);");
        var constrainedTypeParameter = Lower(
            """
            private static T Target<T>() where T : class =>
                default(T);
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.IsExact, Is.True);
            Assert.That(text.Term, Is.TypeOf<IrNullTerm>());
            Assert.That(instance.IsExact, Is.True);
            Assert.That(instance.Term, Is.TypeOf<IrNullTerm>());
            Assert.That(sequence.IsExact, Is.True);
            Assert.That(sequence.Term, Is.TypeOf<IrNullTerm>());
            Assert.That(constrainedTypeParameter.IsExact, Is.True);
            Assert.That(
                constrainedTypeParameter.Term,
                Is.TypeOf<IrNullTerm>());
        }
    }

    [Test]
    public void UnsupportedValueTypeDefaultsFailClosed()
    {
        var decimalValue = Lower(
            "private static decimal Target() => default(decimal);");
        var dateTime = Lower(
            """
            private static System.DateTime Target() =>
                default(System.DateTime);
            """);
        var nativeInteger = Lower(
            "private static nint Target() => default(nint);");
        var customValue = Lower(
            """
            private readonly struct Item {}
            private static Item Target() => default(Item);
            """);
        var enumeration = Lower(
            """
            private enum State { None }
            private static State Target() => default(State);
            """);
        var nullableValue = Lower(
            "private static int? Target() => default(int?);");
        var unconstrainedTypeParameter = Lower(
            "private static T Target<T>() => default(T);");

        using (Assert.EnterMultipleScope())
        {
            AssertAbstention(
                decimalValue,
                FrontendAbstention.UnsupportedType);
            AssertAbstention(
                dateTime,
                FrontendAbstention.UnsupportedType);
            AssertAbstention(
                nativeInteger,
                FrontendAbstention.UnsupportedType);
            AssertAbstention(
                customValue,
                FrontendAbstention.UnsupportedType);
            AssertAbstention(
                enumeration,
                FrontendAbstention.UnsupportedType);
            AssertAbstention(
                nullableValue,
                FrontendAbstention.UnsupportedType);
            AssertAbstention(
                unconstrainedTypeParameter,
                FrontendAbstention.UnsupportedType);
        }
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
    public void UnaryPlusOnUnsupportedScalarTypesFailsClosed()
    {
        var decimalValue = Lower(
            "private static decimal Target(decimal value) => +value;");
        var singleValue = Lower(
            "private static float Target(float value) => +value;");
        var doubleValue = Lower(
            "private static double Target(double value) => +value;");
        var nativeInteger = Lower(
            "private static nint Target(nint value) => +value;");
        var nativeUnsignedInteger = Lower(
            "private static nuint Target(nuint value) => +value;");

        using (Assert.EnterMultipleScope())
        {
            AssertAbstention(
                decimalValue,
                FrontendAbstention.UnsupportedType);
            AssertAbstention(
                singleValue,
                FrontendAbstention.UnsupportedType);
            AssertAbstention(
                doubleValue,
                FrontendAbstention.UnsupportedType);
            AssertAbstention(
                nativeInteger,
                FrontendAbstention.UnsupportedType);
            AssertAbstention(
                nativeUnsignedInteger,
                FrontendAbstention.UnsupportedType);
        }
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

    private static FrontendLoweringResult Lower(string members)
    {
        var tree = CSharpSyntaxTree.ParseText(
            "public static class Subject {" +
            Environment.NewLine +
            members +
            Environment.NewLine +
            "}",
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
        return new RoslynOperationLowerer(new IrFactory())
            .Lower(operation);
    }

    private static IOperation GetExpressionOperation(
        SemanticModel model,
        ExpressionSyntax expression)
    {
        var operation = model.GetOperation(expression);
        if (operation != null)
        {
            return operation;
        }

        return expression switch
        {
            CheckedExpressionSyntax checkedExpression =>
                GetExpressionOperation(
                    model,
                    checkedExpression.Expression),
            ParenthesizedExpressionSyntax parenthesized =>
                GetExpressionOperation(
                    model,
                    parenthesized.Expression),
            _ => throw new InvalidOperationException(
                "Roslyn did not expose the target expression.")
        };
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
