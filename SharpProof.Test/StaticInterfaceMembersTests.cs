using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;
using SharpProof.Attributes;
using System.Linq;

namespace SharpProof.Test
{
    [TestFixture]
    public class StaticInterfaceMembersTests
    {



        [Test]
        public async Task StaticInterfaceMethod_PureImplementation_MissingAttributeDiagnostics()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public interface IAddable<T> where T : IAddable<T>
    {
        static abstract T Add(T a, T b);
    }

    public struct Integer : IAddable<Integer>
    {
        public int Value { get; }

        public Integer(int value) { Value = value; }

        // Pure implementation of static abstract member
        // Assume [EnforcePure] could be applied to the interface method in real use.
        public static Integer Add(Integer a, Integer b) // No markup
        {
            return new Integer(a.Value + b.Value);
        }
    }
}";


            var expectedSP0004InterfaceAdd = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                                    .WithSpan(11, 27, 11, 30)
                                                    .WithArguments("Add");
            var expectedGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                        .WithSpan(16, 20, 16, 25)
                                        .WithArguments("get_Value");
            var expectedCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                       .WithSpan(18, 16, 18, 23)
                                       .WithArguments(".ctor");
            var expectedSP0004StructAdd = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                               .WithSpan(22, 31, 22, 34)
                                               .WithArguments("Add");


            await new VerifyCS.Test
            {
                TestCode = test,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                SolutionTransforms = {
                    (solution, projectId) =>
                        solution.AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location))
                 },
                ExpectedDiagnostics = { expectedSP0004InterfaceAdd, expectedGetter, expectedCtor, expectedSP0004StructAdd }
            }.RunAsync();
        }

        [Test]
        public async Task StaticInterfaceMethod_ContractOnInterface_PureImplementation_NoDiagnostic()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using System;
using SharpProof.Attributes;

interface IPureInterface
{
    [EnforcePure]
    static abstract int PureStaticMethod();
}

class PureImplementation : IPureInterface
{
    public static int PureStaticMethod() => 42;
}
";

            await new VerifyCS.Test
            {
                TestCode = test,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                SolutionTransforms = {
                    (solution, projectId) =>
                        solution.AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location))
                 },

            }.RunAsync();
        }

        [Test]
        public async Task StaticInterfaceMethod_ImpureImplementation_Diagnostic()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using System;
using SharpProof.Attributes;

interface IPureInterface
{
    [EnforcePure] // Attribute on interface method
    static abstract int PureStaticMethod();
}

class ImpureImplementation : IPureInterface
{
    static int counter = 0;
    public static int {|SP0002:PureStaticMethod|}() => ++counter;
}
";





            await new VerifyCS.Test
            {
                TestCode = test,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                SolutionTransforms = {
                    (solution, projectId) =>
                        solution.AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location))
                },

            }.RunAsync();
        }

        [Test]
        public async Task StaticInterfaceMethod_GenericDispatch_IsConservative()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using SharpProof.Attributes;

public interface IAddable<T> where T : IAddable<T>
{
    [EnforcePure]
    static abstract T Add(T left, T right);
}

public readonly struct Number : IAddable<Number>
{
    public readonly int Value;

    [EnforcePure]
    public Number(int value)
    {
        Value = value;
    }

    [EnforcePure]
    public static Number Add(Number left, Number right) => new Number(left.Value + right.Value);
}

public class Calculator
{
    [EnforcePure]
    public T {|SP0002:AddGeneric|}<T>(T left, T right) where T : IAddable<T>
    {
        return T.Add(left, right);
    }
}
";

            await new VerifyCS.Test
            {
                TestCode = test,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                SolutionTransforms =
                {
                    (solution, projectId) =>
                        solution.AddMetadataReference(projectId, Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location))
                },
            }.RunAsync();
        }

        [Test]
        public async Task StaticAbstractInterfaceOperator_GenericDispatch_IsConservative()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using SharpProof.Attributes;

public interface IAdditive<T> where T : IAdditive<T>
{
    [EnforcePure]
    static abstract T operator +(T left, T right);
}

public readonly struct Number : IAdditive<Number>
{
    public readonly int Value;

    [EnforcePure]
    public Number(int value)
    {
        Value = value;
    }

    [EnforcePure]
    public static Number operator +(Number left, Number right) => new Number(left.Value + right.Value);
}

public class Calculator
{
    [EnforcePure]
    public T {|SP0002:AddGeneric|}<T>(T left, T right) where T : IAdditive<T>
    {
        return left + right;
    }
}
";

            await new VerifyCS.Test
            {
                TestCode = test,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                SolutionTransforms =
                {
                    (solution, projectId) =>
                        solution.AddMetadataReference(projectId, Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location))
                },
            }.RunAsync();
        }

        [Test]
        public async Task StaticAbstractInterfaceUnaryOperator_GenericDispatch_IsConservative()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using SharpProof.Attributes;

public interface INegatable<T> where T : INegatable<T>
{
    [EnforcePure]
    static abstract T operator -(T value);
}

public readonly struct Number : INegatable<Number>
{
    public readonly int Value;

    [EnforcePure]
    public Number(int value)
    {
        Value = value;
    }

    [EnforcePure]
    public static Number operator -(Number value) => new Number(-value.Value);
}

public class Calculator
{
    [EnforcePure]
    public T {|SP0002:NegateGeneric|}<T>(T value) where T : INegatable<T>
    {
        return -value;
    }
}
";

            await new VerifyCS.Test
            {
                TestCode = test,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                SolutionTransforms =
                {
                    (solution, projectId) =>
                        solution.AddMetadataReference(projectId, Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location))
                },
            }.RunAsync();
        }

        [Test]
        public async Task StaticAbstractInterfaceConversionOperator_GenericDispatch_IsConservative()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using SharpProof.Attributes;

public interface IIntConvertible<T> where T : IIntConvertible<T>
{
    static abstract explicit operator int(T value);
}

public readonly struct Number : IIntConvertible<Number>
{
    public readonly int Value;

    [EnforcePure]
    public Number(int value)
    {
        Value = value;
    }

    public static explicit operator int(Number value) => value.Value;
}

public class Calculator
{
    [EnforcePure]
    public int {|SP0002:ConvertGeneric|}<T>(T value) where T : IIntConvertible<T>
    {
        return (int)value;
    }
}
";

            await new VerifyCS.Test
            {
                TestCode = test,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                SolutionTransforms =
                {
                    (solution, projectId) =>
                        solution.AddMetadataReference(projectId, Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location))
                },
            }.RunAsync();
        }

        [Test]
        public async Task StaticInterfaceMethod_VirtualWithDefault_PureImplementation_MissingAttributeDiagnostics()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using System;
using SharpProof.Attributes;

namespace TestNamespace
{
    public interface IMultiplyable<T> where T : IMultiplyable<T>
    {
        // Default implementation is impure (throws); this test verifies the pure override suggestion path.
        // Purity check applies to overriding implementations.
        static virtual T Multiply(T a, T b) => throw new NotImplementedException();
    }

    public struct Double : IMultiplyable<Double>
    {
        public double Value { get; }
        public Double(double value) { Value = value; }

        // Pure override of static virtual member
        public static Double Multiply(Double a, Double b) // No markup
        {
            return new Double(a.Value * b.Value);
        }
    }
}";







            var expectedGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                        .WithSpan(18, 23, 18, 28)
                                        .WithArguments("get_Value");
            var expectedCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                       .WithSpan(19, 16, 19, 22)
                                       .WithArguments(".ctor");
            var expectedSP0004Multiply = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                                              .WithSpan(22, 30, 22, 38)
                                              .WithArguments("Multiply");
            await new VerifyCS.Test
            {
                TestCode = test,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                SolutionTransforms = {
                    (solution, projectId) =>
                        solution.AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location))
                 },
                ExpectedDiagnostics = { expectedGetter, expectedCtor, expectedSP0004Multiply }
            }.RunAsync();
        }

        [Test]
        public async Task StaticInterfaceMethod_VirtualWithDefault_ImpureImplementation_Diagnostic()
        {
            var test = @"
// Requires LangVersion 11+
#nullable enable
using System;
using SharpProof.Attributes;

interface IPureInterface
{
    [EnforcePure] // Attribute on interface method
    static virtual int PureStaticMethod() => 0; // Default implementation is pure
}

class ImpureImplementation : IPureInterface
{
    static int counter = 0;
    public static int {|SP0002:PureStaticMethod|}() => ++counter;
}
";





            await new VerifyCS.Test
            {
                TestCode = test,
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                SolutionTransforms = {
                    (solution, projectId) =>
                        solution.AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location))
                 },

            }.RunAsync();
        }
    }
}
