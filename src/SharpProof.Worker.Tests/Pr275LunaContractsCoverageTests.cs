using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275LunaContractsCoverageTests
{
    private static readonly CSharpCompilation Compilation = TestCompilation.Create(
        "Pr275LunaContracts",
        """
        public sealed class Pr275LunaShapeTarget
        {
            public int Value { get; set; }
            public void Read(int value) { }
        }

        public sealed class Pr275LunaInstanceCompanion
        {
            public static void Read(Pr275LunaShapeTarget target, int value) { }
        }

        public static class Pr275LunaStaticCompanion
        {
            public static void Read(Pr275LunaShapeTarget target, int value) { }
        }

        public interface IPr275LunaMarker<T> { }
        public interface IPr275LunaOtherMarker<T> { }

        public interface IPr275LunaReader<T>
        {
            void Read<U>(in U value) where U : IPr275LunaMarker<T>;
        }

        public static class Pr275LunaReaderCompanion<T>
        {
            public static void Read<U>(IPr275LunaReader<T> receiver, in U value)
                where U : IPr275LunaMarker<T> { }
        }

        public static class Pr275LunaWrongReaderCompanion<T>
        {
            public static void Read<U>(IPr275LunaReader<T> receiver, in U value)
                where U : IPr275LunaOtherMarker<T> { }
        }

        public interface IPr275LunaDynamicTarget
        {
            void Read(dynamic value);
        }

        public static class Pr275LunaDynamicCompanion
        {
            public static void Read(IPr275LunaDynamicTarget receiver, dynamic value) { }
        }

        public unsafe interface IPr275LunaFunctionTarget
        {
            delegate* unmanaged<int, int> Map(delegate* unmanaged<int, int> value);
        }

        public static unsafe class Pr275LunaFunctionMismatchCompanion
        {
            public static delegate* unmanaged<int, int> Map(
                IPr275LunaFunctionTarget receiver,
                delegate* unmanaged[Cdecl]<int, int> value) =>
                (delegate* unmanaged<int, int>)value;
        }

        public unsafe interface IPr275LunaCdeclFunctionTarget
        {
            delegate* unmanaged[Cdecl]<int, int> Map(
                delegate* unmanaged[Cdecl]<int, int> value);
        }

        public static unsafe class Pr275LunaCdeclFunctionCompanion
        {
            public static delegate* unmanaged[Cdecl]<int, int> Map(
                IPr275LunaCdeclFunctionTarget receiver,
                delegate* unmanaged[Cdecl]<int, int> value) => value;
        }

        public sealed class Pr275LunaOuter<T>
        {
            public sealed class Inner<U> { }
            public sealed class Marker { }
        }

        public sealed class Pr275LunaBox<T> { }

        public sealed class Pr275LunaSubject<TContainer>
        {
            public void Source<TMethod>(TMethod value) { }
            public unsafe void CallbackSource(delegate*<TContainer, int> callback) { }
            public unsafe void PointerSource<TMethod>() where TMethod : unmanaged { }
        }
        """,
        allowUnsafe: true);

    [Test]
    public void CompanionResolutionUsesCompilerSymbolsForOrdinaryTargets()
    {
        var targetType = Compilation.GetTypeByMetadataName("Pr275LunaShapeTarget")!;
        var target = targetType.GetMembers("Read").OfType<IMethodSymbol>().Single();
        var companionType = Compilation.GetTypeByMetadataName("Pr275LunaStaticCompanion")!;
        var descriptor = new ContractForSymbolMatcher.CompanionDescriptor(
            companionType,
            (Target: targetType, IsOpen: false));

        var resolution = ContractForSymbolMatcher.ResolveCompanion([descriptor], target);

        Assert.That(resolution.Method, Is.Not.Null);
        Assert.That(resolution.Method!.ContainingType, Is.EqualTo(companionType));

        var property = targetType.GetMembers("Value").OfType<IPropertySymbol>().Single();
        var nonMethod = ContractForSymbolMatcher.ResolveCompanion(
            [descriptor], property.GetMethod!);
        Assert.That(nonMethod.Method, Is.Null);
        Assert.That(nonMethod.Failure, Is.EqualTo(ContractBindingFailure.None));
    }

    [Test]
    public void CompanionResolutionRejectsACompiledNonStaticCompanionType()
    {
        var targetType = Compilation.GetTypeByMetadataName("Pr275LunaShapeTarget")!;
        var target = targetType.GetMembers("Read").OfType<IMethodSymbol>().Single();
        var companionType = Compilation.GetTypeByMetadataName("Pr275LunaInstanceCompanion")!;
        var descriptor = new ContractForSymbolMatcher.CompanionDescriptor(
            companionType,
            (Target: targetType, IsOpen: false));

        var resolution = ContractForSymbolMatcher.ResolveCompanion([descriptor], target);

        Assert.That(resolution.Method, Is.Null);
        Assert.That(resolution.Failure,
            Is.EqualTo(ContractBindingFailure.CompanionSignatureMismatch));
    }

    [Test]
    public void SignatureMatchingCoversReadonlyInputsGenericConstraintsAndDynamicTypes()
    {
        var reader = Compilation.GetTypeByMetadataName("IPr275LunaReader`1")!;
        var target = reader.GetMembers("Read").OfType<IMethodSymbol>().Single();
        var companion = Compilation.GetTypeByMetadataName("Pr275LunaReaderCompanion`1")!
            .GetMembers("Read").OfType<IMethodSymbol>().Single();
        var wrongCompanion = Compilation.GetTypeByMetadataName("Pr275LunaWrongReaderCompanion`1")!
            .GetMembers("Read").OfType<IMethodSymbol>().Single();

        Assert.That(ContractForSymbolMatcher.MemberSignaturesMatch(target, companion), Is.True);
        Assert.That(ContractForSymbolMatcher.MemberSignaturesMatch(target, wrongCompanion), Is.False);

        var dynamicTarget = Compilation.GetTypeByMetadataName("IPr275LunaDynamicTarget")!
            .GetMembers("Read").OfType<IMethodSymbol>().Single();
        var dynamicCompanion = Compilation.GetTypeByMetadataName("Pr275LunaDynamicCompanion")!
            .GetMembers("Read").OfType<IMethodSymbol>().Single();

        Assert.That(ContractForSymbolMatcher.MemberSignaturesMatch(
            dynamicTarget, dynamicCompanion), Is.True);
    }

    [Test]
    public void SignatureMatchingComparesUnmanagedFunctionPointerConventions()
    {
        var target = Compilation.GetTypeByMetadataName("IPr275LunaFunctionTarget")!
            .GetMembers("Map").OfType<IMethodSymbol>().Single();
        var mismatch = Compilation.GetTypeByMetadataName(
                "Pr275LunaFunctionMismatchCompanion")!
            .GetMembers("Map").OfType<IMethodSymbol>().Single();
        var cdeclTarget = Compilation.GetTypeByMetadataName(
                "IPr275LunaCdeclFunctionTarget")!
            .GetMembers("Map").OfType<IMethodSymbol>().Single();
        var cdeclCompanion = Compilation.GetTypeByMetadataName(
                "Pr275LunaCdeclFunctionCompanion")!
            .GetMembers("Map").OfType<IMethodSymbol>().Single();

        Assert.That(ContractForSymbolMatcher.MemberSignaturesMatch(target, mismatch), Is.False);
        Assert.That(ContractForSymbolMatcher.MemberSignaturesMatch(
            cdeclTarget, cdeclCompanion), Is.True);
    }

}
