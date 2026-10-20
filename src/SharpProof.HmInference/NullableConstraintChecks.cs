using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class NullableConstraintChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string support = """
            #nullable enable
            public class NullableBoundRoot{}
            public class NullableBoundMiddle:NullableBoundRoot{}
            public class NullableBoundLeaf:NullableBoundMiddle{}
            public interface NullableBoundContract{}
            public interface NullableBoundChild:NullableBoundContract{}
            public class NullableBoundItem:NullableBoundChild{}
            public class NullableBoundBox<T>{}
            public class NullableBoundBoxLeaf<T>:NullableBoundBox<T>{}
            """;
        (string Name, string Source, string Signature)[] tests =
        [
            ("NullableClassInterfaceIntersection", "static NullableBoundItem M(NullableBoundItem value)=>First(Second(value));static T First<T>(T value) where T:class? =>value;static T Second<T>(T value) where T:NullableBoundContract=>value;", "T1 M<T1>(T1 value) where T1 : class, global::NullableBoundContract"),
            ("NullableGenericBaseInput", "static NullableBoundBoxLeaf<int>? M(NullableBoundBoxLeaf<int>? value,int other){Need(value,other);return value;}static T Need<T,U>(T value,U other) where T:NullableBoundBox<U>? =>value;", "global::NullableBoundBoxLeaf<T1>? M<T1>(global::NullableBoundBoxLeaf<T1>? value, T1 other)"),
            ("NullableBaseReverseIntersection", "static NullableBoundLeaf M(NullableBoundLeaf value)=>Second(First(value));static T First<T>(T value) where T:NullableBoundRoot =>value;static T Second<T>(T value) where T:NullableBoundMiddle? =>value;", "T1 M<T1>(T1 value) where T1 : global::NullableBoundMiddle"),
            ("NullableInterfaceWeakIntersection", "static NullableBoundItem M(NullableBoundItem value)=>First(Second(value));static T First<T>(T value) where T:NullableBoundContract? =>value;static T Second<T>(T value) where T:NullableBoundChild? =>value;", "T1 M<T1>(T1 value) where T1 : global::NullableBoundChild?"),
            ("NullableObliviousBaseIntersection", "static NullableBoundLeaf M(NullableBoundLeaf value)=>Legacy(Current(value));static T Current<T>(T value) where T:NullableBoundMiddle? =>value;\n#nullable disable\nstatic T Legacy<T>(T value) where T:NullableBoundRoot=>value;\n#nullable enable\n", "T1 M<T1>(T1 value) where T1 : global::NullableBoundMiddle?"),
            ("NullableObliviousInterfaceIntersection", "static NullableBoundItem M(NullableBoundItem value)=>Legacy(Current(value));static T Current<T>(T value) where T:NullableBoundChild? =>value;\n#nullable disable\nstatic T Legacy<T>(T value) where T:NullableBoundContract=>value;\n#nullable enable\n", "T1 M<T1>(T1 value) where T1 : global::NullableBoundChild?"),
            ("NullableObliviousClass", "static NullableBoundLeaf M(NullableBoundLeaf value)=>Legacy(value);\n#nullable disable\nstatic T Legacy<T>(T value) where T:class=>value;\n#nullable enable\n", "T1 M<T1>(T1 value) where T1 : class?"),
            ("NullableBaseInput", "static NullableBoundLeaf? M(NullableBoundLeaf? value)=>Keep(value);static T Keep<T>(T value) where T:NullableBoundRoot? =>value;", "T1? M<T1>(T1? value) where T1 : global::NullableBoundRoot"),
            ("NullableBaseControl", "static NullableBoundLeaf M(NullableBoundLeaf value)=>Keep(value);static T Keep<T>(T value) where T:NullableBoundRoot? =>value;", "T1 M<T1>(T1 value) where T1 : global::NullableBoundRoot?"),
            ("NullableBaseIntersection", "static NullableBoundLeaf M(NullableBoundLeaf value)=>First(Second(value));static T First<T>(T value) where T:NullableBoundRoot? =>value;static T Second<T>(T value) where T:NullableBoundMiddle? =>value;", "T1 M<T1>(T1 value) where T1 : global::NullableBoundMiddle?"),
            ("NullableBaseMixedIntersection", "static NullableBoundLeaf M(NullableBoundLeaf value)=>First(Second(value));static T First<T>(T value) where T:NullableBoundRoot =>value;static T Second<T>(T value) where T:NullableBoundMiddle? =>value;", "T1 M<T1>(T1 value) where T1 : global::NullableBoundMiddle"),
            ("NullableBaseWithClass", "static NullableBoundLeaf M(NullableBoundLeaf value)=>First(Second(value));static T First<T>(T value) where T:NullableBoundRoot? =>value;static T Second<T>(T value) where T:class=>value;", "T1 M<T1>(T1 value) where T1 : global::NullableBoundRoot"),
            ("NullableBaseWithNotNull", "static NullableBoundLeaf M(NullableBoundLeaf value)=>First(Second(value));static T First<T>(T value) where T:NullableBoundRoot? =>value;static T Second<T>(T value) where T:notnull=>value;", "T1 M<T1>(T1 value) where T1 : global::NullableBoundRoot"),
            ("NullableInterfaceInput", "static NullableBoundItem? M(NullableBoundItem? value)=>Keep(value);static T Keep<T>(T value) where T:NullableBoundContract? =>value;", "T1? M<T1>(T1? value) where T1 : class, global::NullableBoundContract"),
            ("NullableInterfaceDuplicate", "static NullableBoundItem M(NullableBoundItem value)=>First(Second(value));static T First<T>(T value) where T:NullableBoundContract? =>value;static T Second<T>(T value) where T:NullableBoundContract=>value;", "T1 M<T1>(T1 value) where T1 : global::NullableBoundContract"),
            ("NullableInterfaceImplication", "static NullableBoundItem M(NullableBoundItem value)=>First(Second(value));static T First<T>(T value) where T:NullableBoundContract=>value;static T Second<T>(T value) where T:NullableBoundChild? =>value;", "T1 M<T1>(T1 value) where T1 : global::NullableBoundChild"),
            ("NullableDependentCycle", "static string? M(string first,string? second){Keep(first,second);Keep(second,first);return second;}static T Keep<T,U>(T value,U other) where T:U? =>value;", "T1? M<T1>(T1 first, T1? second) where T1 : class")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(support + "class Cases{" + test.Source + "}", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(Inference.IsCompilationError).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + string.Join("; ", errors.Select(error => { ArgumentNullException.ThrowIfNull(error); return error.ToString(); }))); }
            var result = Program.Results(compilation, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { failures.Add(test.Name + ": " + result.Status + ": " + (result.Signature ?? result.Message)); }
            else
            { Program.Log.Info("PASS " + test.Name); }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        const string legacySource = """
            #nullable disable
            public static class LegacyApi{public static T Echo<T>(T value) where T:class=>value;}
            #nullable enable
            public interface Contract{}
            public static class LegacyNullableApi{public static T Echo<T>(T value) where T:Contract? =>value;}
            """;
        using var metadata = new MemoryStream();
        var library = CSharpCompilation.Create("LegacyNullableConstraints", [CSharpSyntaxTree.ParseText(legacySource, cancellationToken: cancellationToken)], Program.Compile([]).References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emitted = library.Emit(metadata, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException("Legacy constraint metadata fixture could not be emitted: " + string.Join("; ", emitted.Diagnostics)); }
        var reference = MetadataReference.CreateFromImage(metadata.ToArray()).WithAliases(["Bound"]);
        (string Name, string Source, string Signature)[] metadataTests =
        [
            ("NullableObliviousMetadataConstraint", "#nullable enable\nextern alias Bound;class Cases{static string M(string value)=>Bound::LegacyApi.Echo(value);}", "T1 M<T1>(T1 value) where T1 : class?"),
            ("NullableAliasMetadataConstraint", "#nullable enable\nextern alias Bound;public interface Contract{}public class Item:Bound::Contract{}class Cases{static Item M(Item value)=>Bound::LegacyNullableApi.Echo(value);}", "T1 M<T1>(T1 value) where T1 : Bound::Contract?")
        ];
        foreach (var test in metadataTests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(test.Source, cancellationToken: cancellationToken)]).AddReferences(reference);
            if (compilation.GetDiagnostics(cancellationToken).Any(Inference.IsCompilationError))
            { throw new InvalidOperationException(test.Name + " original fixture must compile."); }
            var result = Program.Results(compilation, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": " + result); }
            Program.Log.Info("PASS " + test.Name);
        }
        return tests.Length + metadataTests.Length + await NullableConstraintRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }
}
