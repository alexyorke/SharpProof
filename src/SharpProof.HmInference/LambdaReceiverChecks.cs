using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class LambdaReceiverChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        var branchChecks = VerifyNestedReturnArms(cancellationToken);
        var gapChecks = VerifyGapSources(cancellationToken);
        (string Name, string Method, string Signature)[] tests =
        [
            ("LambdaReceiverExpressionReturn", "static int M(Box<int> box,int value)=>box.Run(value,()=>Maybe(value));", "T1? M<T1>(global::Cases.Box<T1?> box, T1 value)"),
            ("LambdaReceiverBlockReturn", "static int M(Box<int> box,int value){return box.Run(value,()=>{return Maybe(value);});}", "T1? M<T1>(global::Cases.Box<T1?> box, T1 value)"),
            ("LambdaReceiverAnonymousReturn", "static int M(Box<int> box,int value)=>box.Run(value,delegate{return Maybe(value);});", "T1? M<T1>(global::Cases.Box<T1?> box, T1 value)"),
            ("LambdaReceiverReturnFirst", "static int M(PairBox<int,string> box,int value,string tag)=>box.Run(value,x=>Maybe(value),tag);", "T1? M<T1, T2>(global::Cases.PairBox<T1?, T2> box, T1 value, T2 tag)"),
            ("LambdaReceiverTaskReturn", "static Task<int> M(AsyncBox<int> box,int value)=>box.Run(value,async()=>{await Task.Yield();return Maybe(value);});", "global::System.Threading.Tasks.Task<T1?> M<T1>(global::Cases.AsyncBox<T1?> box, T1 value)"),
            ("LambdaReceiverValueTaskReturn", "static ValueTask<int> M(ValueAsyncBox<int> box,int value)=>box.Run(value,async()=>{await Task.Yield();return Maybe(value);});", "global::System.Threading.Tasks.ValueTask<T1?> M<T1>(global::Cases.ValueAsyncBox<T1?> box, T1 value)"),
            ("LambdaReceiverCustomTaskReturn", "static Task<int> M(AsyncPairBox<int,string> box,int value,string tag)=>box.Run(value,async x=>{await Task.Yield();return Maybe(value);},tag);", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(global::Cases.AsyncPairBox<T1?, T2> box, T1 value, T2 tag)"),
            ("LambdaReceiverCustomValueTaskReturn", "static ValueTask<int> M(ValueAsyncPairBox<int,string> box,int value,string tag)=>box.Run(value,async x=>{await Task.Yield();return Maybe(value);},tag);", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(global::Cases.ValueAsyncPairBox<T1?, T2> box, T1 value, T2 tag)"),
            ("LambdaReceiverListReturn", "static List<int> M(ListBox<int> box,int value)=>box.Run(value,()=>Pack(value,Maybe(value)));", "global::System.Collections.Generic.List<T1?> M<T1>(global::Cases.ListBox<T1?> box, T1 value)"),
            ("LambdaReceiverNestedListReturn", "static List<List<int>> M(NestedListBox<int> box,int value)=>box.Run(value,()=>NestedPack(Pack(value,Maybe(value))));", "global::System.Collections.Generic.List<global::System.Collections.Generic.List<T1?>> M<T1>(global::Cases.NestedListBox<T1?> box, T1 value)"),
            ("LambdaReceiverArrayReturn", "static int[] M(ArrayBox<int> box,int value)=>box.Run(value,()=>ArrayPack(value,Maybe(value)));", "T1?[] M<T1>(global::Cases.ArrayBox<T1?> box, T1 value)"),
            ("LambdaReceiverTupleReturn", "static (int value,string tag) M(TupleBox<int,string> box,int value,string tag)=>box.Run(value,()=> (Maybe(value),tag));", "(T1? value, T2 tag) M<T1, T2>(global::Cases.TupleBox<T1?, T2> box, T1 value, T2 tag)"),
            ("LambdaReceiverTupleSibling", "static (int value,string tag) M(TupleBox<int,string> box,int value,string tag)=>box.Run(value,()=> (value,Maybe(tag)));", "(T1 value, T2? tag) M<T1, T2>(global::Cases.TupleBox<T1, T2?> box, T1 value, T2 tag)"),
            ("LambdaReceiverRepeatedTupleFirst", "static (int left,int right) M(RepeatBox<int> box,int value)=>box.Run(value,()=> (Maybe(value),value));", "(T1? left, T1? right) M<T1>(global::Cases.RepeatBox<T1?> box, T1 value)"),
            ("LambdaReceiverRepeatedTupleSecond", "static (int left,int right) M(RepeatBox<int> box,int value)=>box.Run(value,()=> (value,Maybe(value)));", "(T1? left, T1? right) M<T1>(global::Cases.RepeatBox<T1?> box, T1 value)"),
            ("LambdaReceiverMixedTupleSibling", "static (int left,string tag) M(MixedBox<int,string> box,int value,string tag)=>box.Run(value,()=> (value,Maybe(tag)));", "(T1 left, T2? tag) M<T1, T2>(global::Cases.MixedBox<T1, T2?> box, T1 value, T2 tag)"),
            ("LambdaReceiverCustomListReturn", "static List<int> M(CustomListBox<int,string> box,int value,string tag)=>box.Run(value,x=>Pack(value,Maybe(value)),tag);", "global::System.Collections.Generic.List<T1?> M<T1, T2>(global::Cases.CustomListBox<T1?, T2> box, T1 value, T2 tag)"),
            ("LambdaReceiverAsyncListReturn", "static Task<List<int>> M(AsyncListBox<int> box,int value)=>box.Run(value,async()=>{await Task.Yield();return Pack(value,Maybe(value));});", "global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<T1?>> M<T1>(global::Cases.AsyncListBox<T1?> box, T1 value)"),
            ("LambdaReceiverEnclosingOwner", "static int M(Outer<int>.Inner<string> box,int value,string tag)=>box.Run(value,()=>Maybe(value));", "T1? M<T1, T2, T3>(global::Cases.Outer<T1?>.Inner<T2> box, T1 value, T3 tag)"),
            ("LambdaReceiverNestedOwnArgument", "static string M(Outer<int>.Inner<string> box,int value,string tag)=>box.RunInner(tag,()=>Maybe(tag));", "T2? M<T1, T2, T3>(global::Cases.Outer<T1>.Inner<T2?> box, T3 value, T2 tag)"),
            ("LambdaReceiverDeepEnclosingOwner", "static int M(DeepOuter<int>.Middle.Inner<string> box,int value,string tag)=>box.Run(value,()=>Maybe(value));", "T1? M<T1, T2, T3>(global::Cases.DeepOuter<T1?>.Middle.Inner<T2> box, T1 value, T3 tag)"),
            ("LambdaReceiverEnclosingList", "static List<int> M(Outer<int>.Inner<string> box,int value,string tag)=>box.RunList(value,()=>Pack(value,Maybe(value)));", "global::System.Collections.Generic.List<T1?> M<T1, T2, T3>(global::Cases.Outer<T1?>.Inner<T2> box, T1 value, T3 tag)"),
            ("LambdaReceiverShadowedOwnerName", "static string M(ShadowOuter<int>.Inner<string> box,int value,string tag)=>box.Run(tag,()=>Maybe(tag));", "T2? M<T1, T2, T3>(global::Cases.ShadowOuter<T1>.Inner<T2?> box, T3 value, T2 tag)"),
            ("LambdaReceiverNongenericEnclosingClass", "static int M(Host.GenericBox<int> box,int value)=>box.Run(value,()=>Maybe(value));", "T1? M<T1>(global::Cases.Host.GenericBox<T1?> box, T1 value)")
        ];
        foreach (var test in tests)
        {
            var source = "#nullable enable\nusing System;using System.Collections.Generic;using System.Diagnostics.CodeAnalysis;using System.Threading.Tasks;class Cases{"
                + "sealed class Box<T>{public T Run(T seed,Func<T> action)=>action();}"
                + "delegate R ReturnFirst<R,A>(A argument);"
                + "sealed class PairBox<T,U>{public T Run(T seed,ReturnFirst<T,U> action,U tag)=>action(tag);}"
                + "sealed class AsyncBox<T>{public Task<T> Run(T seed,Func<Task<T>> action)=>action();}"
                + "sealed class ValueAsyncBox<T>{public ValueTask<T> Run(T seed,Func<ValueTask<T>> action)=>action();}"
                + "delegate Task<R> AsyncReturn<R,A>(A argument);"
                + "delegate ValueTask<R> ValueAsyncReturn<R,A>(A argument);"
                + "sealed class AsyncPairBox<T,U>{public Task<T> Run(T seed,AsyncReturn<T,U> action,U tag)=>action(tag);}"
                + "sealed class ValueAsyncPairBox<T,U>{public ValueTask<T> Run(T seed,ValueAsyncReturn<T,U> action,U tag)=>action(tag);}"
                + "sealed class ListBox<T>{public List<T> Run(T seed,Func<List<T>> action)=>action();}"
                + "sealed class NestedListBox<T>{public List<List<T>> Run(T seed,Func<List<List<T>>> action)=>action();}"
                + "sealed class ArrayBox<T>{public T[] Run(T seed,Func<T[]> action)=>action();}"
                + "sealed class TupleBox<T,U>{public (T value,U tag) Run(T seed,Func<(T value,U tag)> action)=>action();}"
                + "delegate (R left,R right) RepeatReturn<R>();"
                + "sealed class RepeatBox<T>{public (T left,T right) Run(T seed,RepeatReturn<T> action)=>action();}"
                + "delegate (R left,U tag) MixedReturn<R,U>();"
                + "sealed class MixedBox<T,U>{public (T left,U tag) Run(T seed,MixedReturn<T,U> action)=>action();}"
                + "delegate List<R> ListReturn<R,A>(A argument);"
                + "sealed class CustomListBox<T,U>{public List<T> Run(T seed,ListReturn<T,U> action,U tag)=>action(tag);}"
                + "sealed class AsyncListBox<T>{public Task<List<T>> Run(T seed,Func<Task<List<T>>> action)=>action();}"
                + "sealed class Outer<T>{public sealed class Inner<U>{public T Run(T seed,Func<T> action)=>action();public U RunInner(U seed,Func<U> action)=>action();public List<T> RunList(T seed,Func<List<T>> action)=>action();}}"
                + "sealed class DeepOuter<T>{public sealed class Middle{public sealed class Inner<U>{public T Run(T seed,Func<T> action)=>action();}}}"
                + "sealed class Host{public sealed class GenericBox<T>{public T Run(T seed,Func<T> action)=>action();}}"
                + "\n#pragma warning disable CS0693\nsealed class ShadowOuter<T>{public sealed class Inner<T>{public T Run(T seed,Func<T> action)=>action();}}\n#pragma warning restore CS0693\n"
                + "[return:MaybeNull]static T Maybe<T>(T value)=>default;"
                + "static List<T> Pack<T>(T first,T second)=>new(){first,second};"
                + "static List<List<T>> NestedPack<T>(List<T> value)=>new(){value};"
                + "static T[] ArrayPack<T>(T first,T second)=>new[]{first,second};"
                + test.Method
                + "static T? Fixed<T>(Box<T?> box,T value)=>box.Run(value,()=>Maybe(value));"
                + "static List<T?> FixedList<T>(ListBox<T?> box,T value)=>box.Run(value,()=>Pack(value,Maybe(value)));"
                + "static (T? left,T? right) GoodRepeat<T>(RepeatBox<T?> box,T value)=>box.Run(value,()=> (Maybe(value),value));"
                + "static (T left,U? tag) GoodMixed<T,U>(MixedBox<T,U?> box,T value,U tag)=>box.Run(value,()=> (value,Maybe(tag)));"
                + "static T? GoodOuter<T,U>(Outer<T?>.Inner<U> box,T value,U tag)=>box.Run(value,()=>Maybe(value));"
                + "static U? GoodInner<T,U>(Outer<T>.Inner<U?> box,T value,U tag)=>box.RunInner(tag,()=>Maybe(tag));"
                + "static T? GoodDeep<T,U>(DeepOuter<T?>.Middle.Inner<U> box,T value,U tag)=>box.Run(value,()=>Maybe(value));"
                + "static List<T?> GoodOuterList<T,U>(Outer<T?>.Inner<U> box,T value,U tag)=>box.RunList(value,()=>Pack(value,Maybe(value)));"
                + "static U? GoodShadow<T,U>(ShadowOuter<T>.Inner<U?> box,U tag)=>box.Run(tag,()=>Maybe(tag));"
                + "static T? GoodHost<T>(Host.GenericBox<T?> box,T value)=>box.Run(value,()=>Maybe(value));}";
            var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            if (test.Name.StartsWith("LambdaReceiverRepeated", StringComparison.Ordinal)
                || test.Name == "LambdaReceiverMixedTupleSibling")
            {
                var goodName = test.Name == "LambdaReceiverMixedTupleSibling" ? "GoodMixed" : "GoodRepeat";
                var goodMethod = tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
                { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == goodName; });
                var nullableWarnings = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
                {
                    ArgumentNullException.ThrowIfNull(diagnostic);
                    return diagnostic.Id is "CS8603" or "CS8619"
                        && diagnostic.Location.SourceTree == tree
                        && goodMethod.Span.Contains(diagnostic.Location.SourceSpan);
                }).ToArray();
                if (nullableWarnings.Length != 0)
                { throw new InvalidOperationException(test.Name + " native annotated control: " + nullableWarnings[0]); }
            }
            if (test.Name is "LambdaReceiverEnclosingOwner" or "LambdaReceiverNestedOwnArgument"
                or "LambdaReceiverDeepEnclosingOwner" or "LambdaReceiverEnclosingList"
                or "LambdaReceiverShadowedOwnerName" or "LambdaReceiverNongenericEnclosingClass")
            {
                var goodName = test.Name switch
                {
                    "LambdaReceiverNestedOwnArgument" => "GoodInner",
                    "LambdaReceiverDeepEnclosingOwner" => "GoodDeep",
                    "LambdaReceiverEnclosingList" => "GoodOuterList",
                    "LambdaReceiverShadowedOwnerName" => "GoodShadow",
                    "LambdaReceiverNongenericEnclosingClass" => "GoodHost",
                    _ => "GoodOuter"
                };
                var goodMethod = tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
                { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == goodName; });
                var nullableWarnings = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
                {
                    ArgumentNullException.ThrowIfNull(diagnostic);
                    return diagnostic.Id is "CS8603" or "CS8619"
                        && diagnostic.Location.SourceTree == tree
                        && goodMethod.Span.Contains(diagnostic.Location.SourceSpan);
                }).ToArray();
                if (nullableWarnings.Length != 0)
                { throw new InvalidOperationException(test.Name + " native annotated control: " + nullableWarnings[0]); }
            }
            var result = Program.Results(compilation, cancellationToken).Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS " + test.Name);
        }
        (string Name, string Method)[] boundaries =
        [
            ("LambdaReceiverSuppressedReturn", "\n#pragma warning disable CS8603\nstatic int M(Box<int> box,int value)=>box.Run(value,()=>Maybe(value));\n#pragma warning restore CS8603\n"),
            ("LambdaReceiverSuppressedList", "\n#pragma warning disable CS8619\nstatic System.Collections.Generic.List<int> M(ListBox<int> box,int value)=>box.Run(value,()=>Pack(value,Maybe(value)));\n#pragma warning restore CS8619\n"),
            ("LambdaReceiverSuppressedInterfaceArm", "\n#pragma warning disable CS8619\nstatic System.Collections.Generic.IReadOnlyList<int> M(ReadOnlyBox<int> box,int value,bool choose)=>box.Run(value,()=>choose?Pack(value,Maybe(value)):new[]{value});\n#pragma warning restore CS8619\n"),
            ("LambdaReceiverSuppressedMixedList", "\n#pragma warning disable CS8619\nstatic System.Collections.Generic.List<int> M(ListBox<int> box,int value,bool choose)=>box.Run(value,()=>choose?Pack(value,Maybe(value)):Pack(value,value));\n#pragma warning restore CS8619\n"),
            ("LambdaReceiverExplicitReturn", "static int M(Box<int> box,int value)=>box.Run(value,int()=>Maybe(value));"),
            ("LambdaReceiverExplicitInterfaceReturn", "static System.Collections.Generic.IReadOnlyList<int> M(ReadOnlyBox<int> box,int value,bool choose)=>box.Run(value,System.Collections.Generic.IReadOnlyList<int>()=>choose?Pack(value,Maybe(value)):new[]{value});"),
            ("LambdaReceiverExplicitMixedList", "static System.Collections.Generic.List<int> M(ListBox<int> box,int value,bool choose)=>box.Run(value,System.Collections.Generic.List<int>()=>choose?Pack(value,Maybe(value)):Pack(value,value));"),
            ("LambdaReceiverSuppressedEnclosingOwner", "\n#pragma warning disable CS8603\nstatic int M(Outer<int>.Inner<string> box,int value,string tag)=>box.Run(value,()=>Maybe(value));\n#pragma warning restore CS8603\n"),
            ("LambdaReceiverExplicitEnclosingOwner", "static int M(Outer<int>.Inner<string> box,int value,string tag)=>box.Run(value,int()=>Maybe(value));"),
            ("LambdaReceiverNestedLocalEnclosingOwner", "static int M(Outer<int>.Inner<string> box,int value,string tag)=>box.Run(value,()=>{int Local()=>Maybe(value);return value;});"),
            ("LambdaReceiverNongenericNestedDelegate", "static int M(Host.Callback callback,int value){_=callback();return value;}"),
            ("LambdaReceiverNongenericNestedClass", "static int M(Host.Plain box,int value)=>box.Run(()=>value);"),
            ("LambdaReceiverNestedReturn", "static int M(Box<int> box,int value)=>box.Run(value,()=>{_=(Func<object>)(()=>Maybe(value));return value;});"),
            ("LambdaReceiverNestedListReturn", "static System.Collections.Generic.List<int> M(ListBox<int> box,int value)=>box.Run(value,()=>{_=(Func<System.Collections.Generic.List<object>>)(()=>Pack<object>(new object(),Maybe<object>(new object())));return new System.Collections.Generic.List<int>{value};});"),
            ("LambdaReceiverNestedInterfaceArmLocal", "static System.Collections.Generic.IReadOnlyList<int> M(ReadOnlyBox<int> box,int value,bool choose)=>box.Run(value,()=>{System.Collections.Generic.IReadOnlyList<object> Nested(bool flag)=>flag?Pack(new object(),Maybe(new object())):new object[0];return choose?new[]{value}:new[]{value};});")
        ];
        foreach (var boundary in boundaries)
        {
            var source = "#nullable enable\nusing System;using System.Diagnostics.CodeAnalysis;class Cases{"
                + "sealed class Box<T>{public T Run(T seed,Func<T> action)=>action();}"
                + "sealed class ListBox<T>{public System.Collections.Generic.List<T> Run(T seed,Func<System.Collections.Generic.List<T>> action)=>action();}"
                + "sealed class ReadOnlyBox<T>{public System.Collections.Generic.IReadOnlyList<T> Run(T seed,Func<System.Collections.Generic.IReadOnlyList<T>> action)=>action();}"
                + "sealed class Outer<T>{public sealed class Inner<U>{public T Run(T seed,Func<T> action)=>action();}}"
                + "sealed class Host{public delegate int Callback();public sealed class Plain{public int Run(Func<int> action)=>action();}}"
                + "[return:MaybeNull]static T Maybe<T>(T value)=>default;"
                + "static System.Collections.Generic.List<T> Pack<T>(T first,T second)=>new(){first,second};"
                + boundary.Method + "}";
            var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(boundary.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "M"; });
            if (result.Status == "Suggestion" && (result.Signature?.Contains("Box<T1?>", StringComparison.Ordinal) == true
                || result.Signature?.Contains("ListBox<T1?>", StringComparison.Ordinal) == true
                || result.Signature?.Contains("ReadOnlyBox<T1?>", StringComparison.Ordinal) == true
                || (boundary.Name.EndsWith("EnclosingOwner", StringComparison.Ordinal)
                    && result.Signature?.Contains("Outer<T1?>", StringComparison.Ordinal) == true)
                || ((boundary.Name == "LambdaReceiverSuppressedMixedList" || boundary.Name == "LambdaReceiverExplicitMixedList")
                    && result.Signature?.Contains("where T1 : struct", StringComparison.Ordinal) == true)))
            { throw new InvalidOperationException(boundary.Name + " promoted a receiver without a direct, effective return warning: " + result.Signature); }
            Program.Log.Info("PASS " + boundary.Name);
        }
        return tests.Length + boundaries.Length + branchChecks + gapChecks;
    }

    private static int VerifyGapSources(CancellationToken cancellationToken)
    {
        var source = "#nullable enable\nusing System;using System.Diagnostics.CodeAnalysis;using System.Linq.Expressions;"
            + "using System.Runtime.CompilerServices;using System.Threading.Tasks;\n"
            + CustomBuilderChecks.Support + "\n" + """
            sealed class TreeGapBox<T> { public T Run(T seed, Expression<Func<T>> read) => read.Compile()(); }
            sealed class NestedGapBox<T> { public delegate T Reader(); public T Run(T seed, Reader read) => read(); }
            sealed class OwnGapBox<T,U> { public delegate R Reader<R>(U tag); public T Run(T seed, Reader<T> read,U tag) => read(tag); }
            sealed class ShadowGapBox<T> {
            #pragma warning disable CS0693
                public delegate T Reader<T>();
            #pragma warning restore CS0693
                public T Run(T seed, Reader<T> read) => read();
            }
            sealed class CustomGapBox<T> { public ProbeTask<T> Run(T seed,Func<ProbeTask<T>> read) => read(); }
            static class GapCases {
                [return:MaybeNull]static T Maybe<T>(T value) => default;
                static int TreeM(TreeGapBox<int> box,int value) => box.Run(value,()=>Maybe(value));
                static T TreeBad<T>(TreeGapBox<T> box,T value) => box.Run(value,()=>Maybe(value));
                static T? TreeGood<T>(TreeGapBox<T?> box,T value) => box.Run(value,()=>Maybe(value));
                static int NestedM(NestedGapBox<int> box,int value) => box.Run(value,()=>Maybe(value));
                static T NestedBad<T>(NestedGapBox<T> box,T value) => box.Run(value,()=>Maybe(value));
                static T? NestedGood<T>(NestedGapBox<T?> box,T value) => box.Run(value,()=>Maybe(value));
                static int OwnM(OwnGapBox<int,string> box,int value,string tag) => box.Run(value,_=>Maybe(value),tag);
                static T OwnBad<T,U>(OwnGapBox<T,U> box,T value,U tag) => box.Run(value,_=>Maybe(value),tag);
                static T? OwnGood<T,U>(OwnGapBox<T?,U> box,T value,U tag) => box.Run(value,_=>Maybe(value),tag);
                static int ShadowM(ShadowGapBox<int> box,int value) => box.Run(value,()=>Maybe(value));
                static T ShadowBad<T>(ShadowGapBox<T> box,T value) => box.Run(value,()=>Maybe(value));
                static T? ShadowGood<T>(ShadowGapBox<T?> box,T value) => box.Run(value,()=>Maybe(value));
                static ProbeTask<int> CustomM(CustomGapBox<int> box,int value) =>
                    box.Run(value,async()=>{await Task.Yield();return Maybe(value);});
                static ProbeTask<T> CustomBad<T>(CustomGapBox<T> box,T value) =>
                    box.Run(value,async()=>{await Task.Yield();return Maybe(value);});
                static ProbeTask<T?> CustomGood<T>(CustomGapBox<T?> box,T value) =>
                    box.Run(value,async()=>{await Task.Yield();return Maybe(value);});
                static int TreeExplicit(TreeGapBox<int> box,int value) => box.Run(value,int()=>Maybe(value));
                static int NestedExplicit(NestedGapBox<int> box,int value) => box.Run(value,int()=>Maybe(value));
                static ProbeTask<int> CustomExplicit(CustomGapBox<int> box,int value) =>
                    box.Run(value,async ProbeTask<int> () => {await Task.Yield();return Maybe(value);});
                static ProbeTask<int> CustomOverride(CustomGapBox<int> box,int value) =>
                    box.Run(value,[AsyncMethodBuilder(typeof(ProbeBuilder<>))] async ProbeTask<int> () =>
                        {await Task.Yield();return Maybe(value);});
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = Program.Compile([tree]);
        var diagnostics = compilation.GetDiagnostics(cancellationToken).ToArray();
        var error = diagnostics.FirstOrDefault(diagnostic =>
        { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; });
        if (error is not null)
        { throw new InvalidOperationException("Lambda gap fixture: " + error); }
        var methods = tree.GetRoot(cancellationToken).DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(declaration =>
            { ArgumentNullException.ThrowIfNull(declaration); return declaration.Identifier.ValueText == "GapCases"; })
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .ToDictionary(method =>
            { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText; }, StringComparer.Ordinal);
        (string Name, string Signature)[] expected =
        [
            ("TreeM", "T1? TreeM<T1>(global::TreeGapBox<T1?> box, T1 value)"),
            ("NestedM", "T1? NestedM<T1>(global::NestedGapBox<T1?> box, T1 value)"),
            ("OwnM", "T1? OwnM<T1, T2>(global::OwnGapBox<T1?, T2> box, T1 value, T2 tag)"),
            ("ShadowM", "T1? ShadowM<T1>(global::ShadowGapBox<T1?> box, T1 value)"),
            ("CustomM", "global::ProbeTask<T1?> CustomM<T1>(global::CustomGapBox<T1?> box, T1 value)")
        ];
        var results = Program.Results(compilation, cancellationToken);
        bool HasReturnWarning(MethodDeclarationSyntax method)
        {
            ArgumentNullException.ThrowIfNull(method);
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Id == "CS8603" && method.Span.Contains(diagnostic.Location.SourceSpan))
                { return true; }
            }
            return false;
        }
        foreach (var (name, signature) in expected)
        {
            var bad = methods[name.Replace("M", "Bad", StringComparison.Ordinal)];
            var good = methods[name.Replace("M", "Good", StringComparison.Ordinal)];
            if (!HasReturnWarning(bad) || HasReturnWarning(good))
            { throw new InvalidOperationException(name + " native nullable controls differ from CS8603/clean."); }
            var result = results.Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == name; });
            if (result.Status != "Suggestion" || result.Signature != signature)
            { throw new InvalidOperationException(name + ": expected " + signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS LambdaGap" + name);
        }
        foreach (var name in new[] { "TreeExplicit", "NestedExplicit", "CustomExplicit", "CustomOverride" })
        {
            var result = results.Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == name; });
            if (result.Status == "Suggestion" && result.Signature?.Contains("GapBox<T1?>", StringComparison.Ordinal) == true)
            { throw new InvalidOperationException(name + " widened an explicit result boundary."); }
        }
        return expected.Length + 4;
    }

    private static int VerifyNestedReturnArms(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading.Tasks;
            class Cases {
                sealed class ListBox<T> { public List<T> Run(T seed, Func<List<T>> action) => action(); }
                sealed class PairListBox<T,U> { public List<T> Run(T seed, Func<List<T>> action,U tag) => action(); }
                sealed class TupleBox<T,U> { public (T value,U tag) Run(T seed, Func<(T value,U tag)> action) => action(); }
                sealed class ReadOnlyBox<T> { public IReadOnlyList<T> Run(T seed, Func<IReadOnlyList<T>> action) => action(); }
                sealed class AsyncReadOnlyBox<T> { public Task<IReadOnlyList<T>> Run(T seed, Func<Task<IReadOnlyList<T>>> action) => action(); }
                delegate Task<IReadOnlyList<R>> AsyncReadOnlyReturn<R,A>(A arg);
                sealed class CustomAsyncReadOnlyBox<T,U> { public Task<IReadOnlyList<T>> Run(T seed, AsyncReadOnlyReturn<T,U> action,U arg) => action(arg); }
                class Base<T> { public T Value; public Base(T value) { Value=value; } }
                sealed class Derived<T> : Base<T> { public Derived(T value) : base(value) {} }
                sealed class BaseBox<T> { public Base<T> Run(T seed, Func<Base<T>> action) => action(); }
                [return: MaybeNull] static T Maybe<T>(T value) => default;
                static List<T> Pack<T>(T first,T second) => new(){first,second};
                static Derived<T> Wrap<T>(T value) => new(value);
                static List<int> ListConditional(ListBox<int> box,int value,bool choose) => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : Pack(Maybe(value),value));
                static List<T> BadListConditional<T>(ListBox<T> box,T value,bool choose) => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : Pack(Maybe(value),value));
                static List<T?> GoodListConditional<T>(ListBox<T?> box,T value,bool choose) => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : Pack(Maybe(value),value));
                static List<int> MixedListConditional(ListBox<int> box,int value,bool choose) => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : Pack(value,value));
                static List<T?> InvalidNullableMixedList<T>(ListBox<T?> box,T value,bool choose) => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : Pack(value,value));
                static List<T> StructMixedList<T>(ListBox<T> box,T value,bool choose) where T:struct => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : Pack(value,value));
                static List<int> MixedListSwitch(ListBox<int> box,int value,bool choose) => box.Run(value,()=>choose switch { true => Pack(value,Maybe(value)), _ => Pack(value,value) });
                static List<T?> InvalidNullableMixedSwitch<T>(ListBox<T?> box,T value,bool choose) => box.Run(value,()=>choose switch { true => Pack(value,Maybe(value)), _ => Pack(value,value) });
                static List<T> StructMixedSwitch<T>(ListBox<T> box,T value,bool choose) where T:struct => box.Run(value,()=>choose switch { true => Pack(value,Maybe(value)), _ => Pack(value,value) });
                static List<int> MixedListSibling(PairListBox<int,string> box,int value,string tag,bool choose) => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : Pack(value,value),tag);
                static List<T?> InvalidNullableMixedSibling<T,U>(PairListBox<T?,U> box,T value,U tag,bool choose) => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : Pack(value,value),tag);
                static List<T> StructMixedSibling<T,U>(PairListBox<T,U> box,T value,U tag,bool choose) where T:struct => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : Pack(value,value),tag);
                static List<int?> MixedPhysicalNullable(ListBox<int?> box,int? value,bool choose) => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : Pack(value,value));
                static List<int?> PhysicalCaller(ListBox<int?> box,int? value,bool choose) => MixedPhysicalNullable(box,value,choose);
                static List<int> ListSwitch(ListBox<int> box,int value,bool choose) => box.Run(value,()=>choose switch { true => Pack(value,Maybe(value)), _ => Pack(Maybe(value),value) });
                static List<T> BadListSwitch<T>(ListBox<T> box,T value,bool choose) => box.Run(value,()=>choose switch { true => Pack(value,Maybe(value)), _ => Pack(Maybe(value),value) });
                static List<T?> GoodListSwitch<T>(ListBox<T?> box,T value,bool choose) => box.Run(value,()=>choose switch { true => Pack(value,Maybe(value)), _ => Pack(Maybe(value),value) });
                static (int value,string tag) TupleConditional(TupleBox<int,string> box,int value,string tag,bool choose) => box.Run(value,()=>choose ? (Maybe(value),tag) : (value,tag));
                static (T? value,U tag) GoodTupleConditional<T,U>(TupleBox<T?,U> box,T value,U tag,bool choose) => box.Run(value,()=>choose ? (Maybe(value),tag) : (value,tag));
                static IReadOnlyList<int> InterfaceConditional(ReadOnlyBox<int> box,int value,bool choose) => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : new[]{value});
                static IReadOnlyList<T> BadInterfaceConditional<T>(ReadOnlyBox<T> box,T value,bool choose) => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : new[]{value});
                static IReadOnlyList<T?> GoodInterfaceConditional<T>(ReadOnlyBox<T?> box,T value,bool choose) => box.Run(value,()=>choose ? Pack(value,Maybe(value)) : new[]{value});
                static IReadOnlyList<int> InterfaceSwitch(ReadOnlyBox<int> box,int value,bool choose) => box.Run(value,()=>choose switch { true => Pack(value,Maybe(value)), _ => new[]{value} });
                static IReadOnlyList<T> BadInterfaceSwitch<T>(ReadOnlyBox<T> box,T value,bool choose) => box.Run(value,()=>choose switch { true => Pack(value,Maybe(value)), _ => new[]{value} });
                static IReadOnlyList<T?> GoodInterfaceSwitch<T>(ReadOnlyBox<T?> box,T value,bool choose) => box.Run(value,()=>choose switch { true => Pack(value,Maybe(value)), _ => new[]{value} });
                static Task<IReadOnlyList<int>> AsyncInterfaceConditional(AsyncReadOnlyBox<int> box,int value,bool choose) => box.Run(value,async()=>{await Task.Yield();return choose ? Pack(value,Maybe(value)) : new[]{value};});
                static Task<IReadOnlyList<T>> BadAsyncInterfaceConditional<T>(AsyncReadOnlyBox<T> box,T value,bool choose) => box.Run(value,async()=>{await Task.Yield();return choose ? Pack(value,Maybe(value)) : new[]{value};});
                static Task<IReadOnlyList<T?>> GoodAsyncInterfaceConditional<T>(AsyncReadOnlyBox<T?> box,T value,bool choose) => box.Run(value,async()=>{await Task.Yield();return choose ? Pack(value,Maybe(value)) : new[]{value};});
                static Task<IReadOnlyList<int>> CustomAsyncInterfaceConditional(CustomAsyncReadOnlyBox<int,string> box,int value,string tag,bool choose) => box.Run(value,async _=>{await Task.Yield();return choose ? Pack(value,Maybe(value)) : new[]{value};},tag);
                static Task<IReadOnlyList<T>> BadCustomAsyncInterfaceConditional<T,U>(CustomAsyncReadOnlyBox<T,U> box,T value,U tag,bool choose) => box.Run(value,async _=>{await Task.Yield();return choose ? Pack(value,Maybe(value)) : new[]{value};},tag);
                static Task<IReadOnlyList<T?>> GoodCustomAsyncInterfaceConditional<T,U>(CustomAsyncReadOnlyBox<T?,U> box,T value,U tag,bool choose) => box.Run(value,async _=>{await Task.Yield();return choose ? Pack(value,Maybe(value)) : new[]{value};},tag);
                static Base<int> BaseConditional(BaseBox<int> box,int value,bool choose) => box.Run(value,()=>choose ? Wrap(Maybe(value)) : Wrap(Maybe(value)));
                static Base<T> BadBaseConditional<T>(BaseBox<T> box,T value,bool choose) => box.Run(value,()=>choose ? Wrap(Maybe(value)) : Wrap(Maybe(value)));
                static Base<T?> GoodBaseConditional<T>(BaseBox<T?> box,T value,bool choose) => box.Run(value,()=>choose ? Wrap(Maybe(value)) : Wrap(Maybe(value)));
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = Program.Compile([tree]);
        var methods = tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var diagnostics = compilation.GetDiagnostics(cancellationToken).ToArray();
        var errors = diagnostics.Where(diagnostic =>
        { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
        if (errors.Length != 0)
        { throw new InvalidOperationException("Nested return arm fixture: " + errors[0]); }
        var results = Program.Results(compilation, cancellationToken).ToArray();
        (string Name, string Signature, string? BadWarningSpan)[] cases =
        [
            ("ListConditional", "global::System.Collections.Generic.List<T1?> ListConditional<T1>(global::Cases.ListBox<T1?> box, T1 value, bool choose)", "choose ? Pack(value,Maybe(value)) : Pack(Maybe(value),value)"),
            ("ListSwitch", "global::System.Collections.Generic.List<T1?> ListSwitch<T1>(global::Cases.ListBox<T1?> box, T1 value, bool choose)", "choose switch { true => Pack(value,Maybe(value)), _ => Pack(Maybe(value),value) }"),
            ("TupleConditional", "(T1? value, T2 tag) TupleConditional<T1, T2>(global::Cases.TupleBox<T1?, T2> box, T1 value, T2 tag, bool choose)", null),
            ("InterfaceConditional", "global::System.Collections.Generic.IReadOnlyList<T1?> InterfaceConditional<T1>(global::Cases.ReadOnlyBox<T1?> box, T1 value, bool choose)", "Pack(value,Maybe(value))"),
            ("InterfaceSwitch", "global::System.Collections.Generic.IReadOnlyList<T1?> InterfaceSwitch<T1>(global::Cases.ReadOnlyBox<T1?> box, T1 value, bool choose)", "Pack(value,Maybe(value))"),
            ("AsyncInterfaceConditional", "global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<T1?>> AsyncInterfaceConditional<T1>(global::Cases.AsyncReadOnlyBox<T1?> box, T1 value, bool choose)", "Pack(value,Maybe(value))"),
            ("CustomAsyncInterfaceConditional", "global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<T1?>> CustomAsyncInterfaceConditional<T1, T2>(global::Cases.CustomAsyncReadOnlyBox<T1?, T2> box, T1 value, T2 tag, bool choose)", "Pack(value,Maybe(value))"),
            ("BaseConditional", "global::Cases.Base<T1?> BaseConditional<T1>(global::Cases.BaseBox<T1?> box, T1 value, bool choose)", "choose ? Wrap(Maybe(value)) : Wrap(Maybe(value))")
        ];
        foreach (var test in cases)
        {
            var good = methods.Single(method =>
            { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == "Good" + test.Name; });
            var goodWarnings = diagnostics.Where(diagnostic =>
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
                return diagnostic.Id is "CS8603" or "CS8619"
                    && diagnostic.Location.SourceTree == tree
                    && good.Span.Contains(diagnostic.Location.SourceSpan);
            }).ToArray();
            if (goodWarnings.Length != 0)
            { throw new InvalidOperationException(test.Name + " native annotated control: " + goodWarnings[0]); }
            if (test.BadWarningSpan is not null)
            {
                var bad = methods.Single(method =>
                { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == "Bad" + test.Name; });
                if (!diagnostics.Any(diagnostic =>
                {
                    ArgumentNullException.ThrowIfNull(diagnostic);
                    return diagnostic.Id == "CS8619" && diagnostic.Location.SourceTree == tree
                        && bad.Span.Contains(diagnostic.Location.SourceSpan)
                        && tree.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan) == test.BadWarningSpan;
                }))
                { throw new InvalidOperationException(test.Name + " lacks the expected native CS8619 return warning."); }
            }
            var result = results.Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == test.Name; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS LambdaReceiver" + test.Name);
        }
        (string Name, string Invalid, string Struct, string Signature)[] mixedCases =
        [
            ("MixedListConditional", "InvalidNullableMixedList", "StructMixedList",
                "global::System.Collections.Generic.List<T1> MixedListConditional<T1>(global::Cases.ListBox<T1> box, T1 value, bool choose) where T1 : struct"),
            ("MixedListSwitch", "InvalidNullableMixedSwitch", "StructMixedSwitch",
                "global::System.Collections.Generic.List<T1> MixedListSwitch<T1>(global::Cases.ListBox<T1> box, T1 value, bool choose) where T1 : struct"),
            ("MixedListSibling", "InvalidNullableMixedSibling", "StructMixedSibling",
                "global::System.Collections.Generic.List<T1> MixedListSibling<T1, T2>(global::Cases.PairListBox<T1, T2> box, T1 value, T2 tag, bool choose) where T1 : struct")
        ];
        foreach (var test in mixedCases)
        {
            var invalidNullable = methods.Single(method =>
            { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == test.Invalid; });
            var nativeStruct = methods.Single(method =>
            { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == test.Struct; });
            if (!diagnostics.Any(diagnostic =>
                {
                    ArgumentNullException.ThrowIfNull(diagnostic);
                    return diagnostic.Id == "CS8619" && diagnostic.Location.SourceTree == tree
                        && invalidNullable.Span.Contains(diagnostic.Location.SourceSpan);
                })
                || diagnostics.Any(diagnostic =>
                {
                    ArgumentNullException.ThrowIfNull(diagnostic);
                    return diagnostic.Id is "CS8603" or "CS8619" && diagnostic.Location.SourceTree == tree
                        && nativeStruct.Span.Contains(diagnostic.Location.SourceSpan);
                }))
            { throw new InvalidOperationException(test.Name + " does not preserve the native value-domain control."); }
            var result = results.Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == test.Name; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS LambdaReceiver" + test.Name + "ValueDomain");
        }
        var physical = methods.Single(method =>
        { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == "MixedPhysicalNullable"; });
        if (diagnostics.Any(diagnostic =>
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
                return diagnostic.Id is "CS8603" or "CS8619" && diagnostic.Location.SourceTree == tree
                    && physical.Span.Contains(diagnostic.Location.SourceSpan);
            }))
        { throw new InvalidOperationException("Physical nullable receiver control has a native warning."); }
        var physicalResult = results.Single(candidate =>
        { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "MixedPhysicalNullable"; });
        if (physicalResult.Status == "Suggestion" && physicalResult.Signature is { } physicalSignature
            && physicalSignature.Contains("ListBox<T1>", StringComparison.Ordinal)
            && physicalSignature.Contains("where T1 : struct", StringComparison.Ordinal))
        { throw new InvalidOperationException("Physical nullable receiver was replaced by a non-nullable struct domain."); }
        Program.Log.Info("PASS LambdaReceiverPhysicalNullableBoundary");
        return cases.Length + mixedCases.Length + 1;
    }

    public static async Task<int> RunApplied(CancellationToken cancellationToken)
    {
        var source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Diagnostics.CodeAnalysis;
            using System.Linq.Expressions;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            #pragma warning disable HM0001
            public static class ReceiverTrace { public static int Helpers,Packs; public static bool UseDefault; public static Exception? Failure; }
            public sealed class ReceiverBox<T> {
                public int Runs;
                public T? Seed;
                public T Run(T seed, Func<T> action) { Runs++; Seed=seed; return action(); }
            }
            public sealed class AsyncReceiverBox<T> {
                public int Runs;
                public Task<T> Run(T seed, Func<Task<T>> action) { Runs++; return action(); }
            }
            public sealed class ValueAsyncReceiverBox<T> {
                public int Runs;
                public ValueTask<T> Run(T seed, Func<ValueTask<T>> action) { Runs++; return action(); }
            }
            public sealed class ListReceiverBox<T> {
                public int Runs;
                public List<T> Run(T seed, Func<List<T>> action) { Runs++; return action(); }
            }
            public sealed class TupleReceiverBox<T,U> {
                public int Runs;
                public (T value,U tag) Run(T seed, Func<(T value,U tag)> action) { Runs++; return action(); }
            }
            public sealed class AsyncListReceiverBox<T> {
                public int Runs;
                public Task<List<T>> Run(T seed, Func<Task<List<T>>> action) { Runs++; return action(); }
            }
            public sealed class ReadOnlyReceiverBox<T> {
                public int Runs;
                public IReadOnlyList<T> Run(T seed, Func<IReadOnlyList<T>> action) { Runs++; return action(); }
            }
            public sealed class AsyncReadOnlyReceiverBox<T> {
                public int Runs;
                public Task<IReadOnlyList<T>> Run(T seed, Func<Task<IReadOnlyList<T>>> action) { Runs++; return action(); }
            }
            public sealed class NestedReceiverOuter<T> {
                public sealed class Inner<U> {
                    public int Runs,InnerRuns,ListRuns;
                    public T? Seed;
                    public U? InnerSeed;
                    public T Run(T seed,Func<T> action) { Runs++; Seed=seed; return action(); }
                    public U RunInner(U seed,Func<U> action) { InnerRuns++; InnerSeed=seed; return action(); }
                    public List<T> RunList(T seed,Func<List<T>> action) { ListRuns++; Seed=seed; return action(); }
                }
            }
            public sealed class DeepReceiverOuter<T> {
                public sealed class Middle {
                    public sealed class Inner<U> {
                        public int Runs;
                        public T? Seed;
                        public T Run(T seed,Func<T> action) { Runs++; Seed=seed; return action(); }
                    }
                }
            }
            public sealed class TreeGapReceiver<T> {
                public int Runs;
                public Expression<Func<T>>? Last;
                public T Run(T seed,Expression<Func<T>> read) { Runs++; Last=read; return read.Compile()(); }
            }
            public sealed class NestedGapReceiver<T> {
                public delegate T Reader();
                public int Runs;
                public Reader? Last;
                public T Run(T seed,Reader read) { Runs++; Last=read; return read(); }
            }
            public sealed class OwnGapReceiver<T,U> {
                public delegate R Reader<R>(U tag);
                public int Runs;
                public U? LastTag;
                public T Run(T seed,Reader<T> read,U tag) { Runs++; LastTag=tag; return read(tag); }
            }
            public sealed class ShadowGapReceiver<T> {
            #pragma warning disable CS0693
                public delegate T Reader<T>();
            #pragma warning restore CS0693
                public int Runs;
                public T Run(T seed,Reader<T> read) { Runs++; return read(); }
            }
            public sealed class CustomGapReceiver<T> {
                public int Runs;
                public ProbeTask<T> Run(T seed,Func<ProbeTask<T>> read) { Runs++; return read(); }
            }
            #pragma warning restore HM0001
            public static class ReceiverProbe {
                [return:MaybeNull]static T Maybe<T>(T value) {
                    ReceiverTrace.Helpers++;
                    if (ReceiverTrace.Failure is { } failure) throw failure;
                    return ReceiverTrace.UseDefault ? default : value;
                }
                static List<T> Pack<T>(T first,T second) { ReceiverTrace.Packs++; return new(){first,second}; }
                public static int M(ReceiverBox<int> box,int value)=>box.Run(value,()=>Maybe(value));
                public static int Block(ReceiverBox<int> box,int value)=>box.Run(value,()=>{return Maybe(value);});
                public static List<int> List(ListReceiverBox<int> box,int value)=>box.Run(value,()=>Pack(value,Maybe(value)));
                public static (int value,string tag) Tuple(TupleReceiverBox<int,string> box,int value,string tag)
                    =>box.Run(value,()=> (Maybe(value),tag));
                public static Task<int> Async(AsyncReceiverBox<int> box,int value,Task gate)
                    =>box.Run(value,async()=>{await gate;return Maybe(value);});
                public static ValueTask<int> ValueAsync(ValueAsyncReceiverBox<int> box,int value,Task gate)
                    =>box.Run(value,async()=>{await gate;return Maybe(value);});
                public static Task<List<int>> AsyncList(AsyncListReceiverBox<int> box,int value,Task gate)
                    =>box.Run(value,async()=>{await gate;return Pack(value,Maybe(value));});
                public static IReadOnlyList<int> InterfaceConditional(ReadOnlyReceiverBox<int> box,int value,bool choose)
                    =>box.Run(value,()=>choose ? Pack(value,Maybe(value)) : new[]{value});
                public static IReadOnlyList<int> InterfaceSwitch(ReadOnlyReceiverBox<int> box,int value,bool choose)
                    =>box.Run(value,()=>choose switch { true => Pack(value,Maybe(value)), _ => new[]{value} });
                public static Task<IReadOnlyList<int>> AsyncInterfaceConditional(AsyncReadOnlyReceiverBox<int> box,int value,Task gate,bool choose)
                    =>box.Run(value,async()=>{await gate;return choose ? Pack(value,Maybe(value)) : new[]{value};});
                public static List<int> MixedListConditional(ListReceiverBox<int> box,int value,bool choose)
                    =>box.Run(value,()=>choose ? Pack(value,Maybe(value)) : Pack(value,value));
                public static List<int> MixedListSwitch(ListReceiverBox<int> box,int value,bool choose)
                    =>box.Run(value,()=>choose switch { true => Pack(value,Maybe(value)), _ => Pack(value,value) });
                public static int NestedOuter(NestedReceiverOuter<int>.Inner<string> box,int value)
                    =>box.Run(value,()=>Maybe(value));
                public static int NestedInner(NestedReceiverOuter<string>.Inner<int> box,int tag)
                    =>box.RunInner(tag,()=>Maybe(tag));
                public static int DeepOuter(DeepReceiverOuter<int>.Middle.Inner<string> box,int value)
                    =>box.Run(value,()=>Maybe(value));
                public static List<int> NestedList(NestedReceiverOuter<int>.Inner<string> box,int value)
                    =>box.RunList(value,()=>Pack(value,Maybe(value)));
                public static int TreeGap(TreeGapReceiver<int> box,int value)=>box.Run(value,()=>Maybe(value));
                public static int NestedGap(NestedGapReceiver<int> box,int value)=>box.Run(value,()=>Maybe(value));
                public static int OwnGap(OwnGapReceiver<int,string> box,int value,string tag)=>box.Run(value,_=>Maybe(value),tag);
                public static int ShadowGap(ShadowGapReceiver<int> box,int value)=>box.Run(value,()=>Maybe(value));
                public static ProbeTask<int> CustomGap(CustomGapReceiver<int> box,int value,Task gate)
                    =>box.Run(value,async()=>{await gate;return Maybe(value);});
            }
            public static class OriginalReceiverRunner {
                public static void Run() {
                    var box=new ReceiverBox<int>();
                    ReceiverTrace.Helpers=0;ReceiverTrace.UseDefault=false;
                    if(ReceiverProbe.M(box,7)!=7||box.Runs!=1||box.Seed!=7||ReceiverTrace.Helpers!=1)throw new Exception("expression result/effects");
                    ReceiverTrace.UseDefault=true;
                    if(ReceiverProbe.Block(box,9)!=0||box.Runs!=2||box.Seed!=9||ReceiverTrace.Helpers!=2)throw new Exception("block default/effects");
                    var failure=new InvalidOperationException("receiver-fault");ReceiverTrace.Failure=failure;
                    try{ReceiverProbe.M(box,11);throw new Exception("missing fault");}
                    catch(InvalidOperationException caught) when(ReferenceEquals(caught,failure)){}
                    finally{ReceiverTrace.Failure=null;}
                    if(box.Runs!=3||box.Seed!=11||ReceiverTrace.Helpers!=3)throw new Exception("fault effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var listBox=new ListReceiverBox<int>();var list=ReceiverProbe.List(listBox,31);
                    if(list.Count!=2||list[0]!=31||list[1]!=0||listBox.Runs!=1||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("list default/effects");
                    var tupleBox=new TupleReceiverBox<int,string>();var pair=ReceiverProbe.Tuple(tupleBox,37,"tag");
                    if(pair.value!=0||pair.tag!="tag"||tupleBox.Runs!=1||ReceiverTrace.Helpers!=2||ReceiverTrace.Packs!=1)
                        throw new Exception("tuple default/sibling/effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;
                    var interfaceBox=new ReadOnlyReceiverBox<int>();
                    var conditional=ReceiverProbe.InterfaceConditional(interfaceBox,53,true);
                    if(conditional.Count!=2||conditional[0]!=53||conditional[1]!=0||interfaceBox.Runs!=1||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("interface conditional nullable arm/effects");
                    var other=ReceiverProbe.InterfaceConditional(interfaceBox,55,false);
                    if(other.Count!=1||other[0]!=55||interfaceBox.Runs!=2||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("interface conditional ordinary arm/effects");
                    var switched=ReceiverProbe.InterfaceSwitch(interfaceBox,57,true);
                    if(switched.Count!=2||switched[0]!=57||switched[1]!=0||interfaceBox.Runs!=3||ReceiverTrace.Helpers!=2||ReceiverTrace.Packs!=2)
                        throw new Exception("interface switch nullable arm/effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;
                    var mixedBox=new ListReceiverBox<int>();var mixed=ReceiverProbe.MixedListConditional(mixedBox,89,true);
                    if(mixed.Count!=2||mixed[0]!=89||mixed[1]!=0||mixedBox.Runs!=1||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("mixed list conditional result/effects");
                    var mixedOther=ReceiverProbe.MixedListSwitch(mixedBox,91,false);
                    if(mixedOther.Count!=2||mixedOther[0]!=91||mixedOther[1]!=91||mixedBox.Runs!=2||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=2)
                        throw new Exception("mixed list switch ordinary arm/effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var nested=new NestedReceiverOuter<int>.Inner<string>();
                    if(ReceiverProbe.NestedOuter(nested,97)!=0||nested.Runs!=1||nested.Seed!=97||ReceiverTrace.Helpers!=1)
                        throw new Exception("nested enclosing value/effects");
                    var own=new NestedReceiverOuter<string>.Inner<int>();
                    if(ReceiverProbe.NestedInner(own,97)!=0||own.InnerRuns!=1||own.InnerSeed!=97||ReceiverTrace.Helpers!=2)
                        throw new Exception("nested own value/effects");
                    var nestedList=ReceiverProbe.NestedList(nested,99);
                    if(nestedList.Count!=2||nestedList[0]!=99||nestedList[1]!=0||nested.ListRuns!=1||nested.Seed!=99||ReceiverTrace.Helpers!=3||ReceiverTrace.Packs!=1)
                        throw new Exception("nested list/effects");
                    var deep=new DeepReceiverOuter<int>.Middle.Inner<string>();
                    if(ReceiverProbe.DeepOuter(deep,101)!=0||deep.Runs!=1||deep.Seed!=101||ReceiverTrace.Helpers!=4)
                        throw new Exception("deep nested enclosing value/effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.UseDefault=true;
                    var treeGap=new TreeGapReceiver<int>();
                    if(ReceiverProbe.TreeGap(treeGap,107)!=0||treeGap.Runs!=1||ReceiverTrace.Helpers!=1
                        ||treeGap.Last?.Body is not MethodCallExpression { Method.Name: "Maybe" }
                        ||treeGap.Last.Parameters.Count!=0)
                        throw new Exception("quoted gap structure/result/effects");
                    var nestedGap=new NestedGapReceiver<int>();
                    if(ReceiverProbe.NestedGap(nestedGap,109)!=0||nestedGap.Runs!=1||nestedGap.Last is null||ReceiverTrace.Helpers!=2)
                        throw new Exception("nested delegate gap result/effects");
                    var ownGap=new OwnGapReceiver<int,string>();
                    if(ReceiverProbe.OwnGap(ownGap,111,"own")!=0||ownGap.Runs!=1||ownGap.LastTag!="own"||ReceiverTrace.Helpers!=3)
                        throw new Exception("nested own-slot gap result/tag/effects");
                    var shadowGap=new ShadowGapReceiver<int>();
                    if(ReceiverProbe.ShadowGap(shadowGap,113)!=0||shadowGap.Runs!=1||ReceiverTrace.Helpers!=4)
                        throw new Exception("nested shadow gap result/effects");
                }
                public static async Task RunAsync() {
                    ReceiverTrace.UseDefault=false;ReceiverTrace.Helpers=0;
                    var taskBox=new AsyncReceiverBox<int>();var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var pending=ReceiverProbe.Async(taskBox,17,gate.Task);
                    if(pending.IsCompleted||taskBox.Runs!=1||ReceiverTrace.Helpers!=0)throw new Exception("task suspension");
                    gate.SetResult();
                    if(await pending!=17||ReceiverTrace.Helpers!=1)throw new Exception("task result/effects");
                    var valueBox=new ValueAsyncReceiverBox<int>();var valueGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    ReceiverTrace.UseDefault=true;
                    var valuePending=ReceiverProbe.ValueAsync(valueBox,19,valueGate.Task);
                    if(valuePending.IsCompleted||valueBox.Runs!=1||ReceiverTrace.Helpers!=1)throw new Exception("value task suspension");
                    valueGate.SetResult();
                    if(await valuePending!=0||ReceiverTrace.Helpers!=2)throw new Exception("value task default/effects");
                    var failure=new InvalidOperationException("async-receiver-fault");
                    var faultGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var faulted=ReceiverProbe.Async(taskBox,23,faultGate.Task);faultGate.SetException(failure);
                    try{await faulted;throw new Exception("missing async fault");}
                    catch(InvalidOperationException caught) when(ReferenceEquals(caught,failure)){}
                    if(taskBox.Runs!=2||ReceiverTrace.Helpers!=2)throw new Exception("async fault effects");
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    var cancelGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var canceledTask=ReceiverProbe.ValueAsync(valueBox,29,cancelGate.Task);
                    cancelGate.SetCanceled(canceled.Token);
                    try{await canceledTask;throw new Exception("missing async cancellation");}
                    catch(OperationCanceledException caught) when(caught.CancellationToken==canceled.Token){}
                    if(valueBox.Runs!=2||ReceiverTrace.Helpers!=2)throw new Exception("async cancel effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var listBox=new AsyncListReceiverBox<int>();var listGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var listPending=ReceiverProbe.AsyncList(listBox,41,listGate.Task);
                    if(listPending.IsCompleted||listBox.Runs!=1||ReceiverTrace.Helpers!=0||ReceiverTrace.Packs!=0)
                        throw new Exception("async list suspension");
                    listGate.SetResult();var list=await listPending;
                    if(list.Count!=2||list[0]!=41||list[1]!=0||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("async list result/effects");
                    var listFailure=new InvalidOperationException("async-list-fault");
                    var listFaultGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var listFaulted=ReceiverProbe.AsyncList(listBox,43,listFaultGate.Task);listFaultGate.SetException(listFailure);
                    try{await listFaulted;throw new Exception("missing async list fault");}
                    catch(InvalidOperationException caught) when(ReferenceEquals(caught,listFailure)){}
                    if(listBox.Runs!=2||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("async list fault effects");
                    var listCancelGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var listCanceled=ReceiverProbe.AsyncList(listBox,47,listCancelGate.Task);listCancelGate.SetCanceled(canceled.Token);
                    try{await listCanceled;throw new Exception("missing async list cancellation");}
                    catch(OperationCanceledException caught) when(caught.CancellationToken==canceled.Token){}
                    if(listBox.Runs!=3||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("async list cancel effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var interfaceBox=new AsyncReadOnlyReceiverBox<int>();var interfaceGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var interfacePending=ReceiverProbe.AsyncInterfaceConditional(interfaceBox,67,interfaceGate.Task,true);
                    if(interfacePending.IsCompleted||interfaceBox.Runs!=1||ReceiverTrace.Helpers!=0||ReceiverTrace.Packs!=0)
                        throw new Exception("async interface suspension");
                    interfaceGate.SetResult();var interfaceResult=await interfacePending;
                    if(interfaceResult.Count!=2||interfaceResult[0]!=67||interfaceResult[1]!=0||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("async interface nullable arm/effects");
                    var interfaceFault=new InvalidOperationException("async-interface-fault");
                    var interfaceFaultGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var interfaceFaulted=ReceiverProbe.AsyncInterfaceConditional(interfaceBox,69,interfaceFaultGate.Task,true);
                    interfaceFaultGate.SetException(interfaceFault);
                    try{await interfaceFaulted;throw new Exception("missing async interface fault");}
                    catch(InvalidOperationException caught) when(ReferenceEquals(caught,interfaceFault)){}
                    if(interfaceBox.Runs!=2||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("async interface fault effects");
                    var interfaceCancelGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var interfaceCanceled=ReceiverProbe.AsyncInterfaceConditional(interfaceBox,71,interfaceCancelGate.Task,true);
                    interfaceCancelGate.SetCanceled(canceled.Token);
                    try{await interfaceCanceled;throw new Exception("missing async interface cancellation");}
                    catch(OperationCanceledException caught) when(caught.CancellationToken==canceled.Token){}
                    if(interfaceBox.Runs!=3||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("async interface cancel effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.UseDefault=true;BuilderTrace.Value="";
                    var customGap=new CustomGapReceiver<int>();var customGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var customPending=ReceiverProbe.CustomGap(customGap,127,customGate.Task);
                    if(customPending.GetAwaiter().IsCompleted||customGap.Runs!=1||ReceiverTrace.Helpers!=0
                        ||!BuilderTrace.Value.Contains("create;",StringComparison.Ordinal)
                        ||!BuilderTrace.Value.Contains("start;",StringComparison.Ordinal))
                        throw new Exception("custom task gap suspension/builder start");
                    customGate.SetResult();
                    if(await customPending!=0||ReceiverTrace.Helpers!=1
                        ||!BuilderTrace.Value.Contains("result;",StringComparison.Ordinal))
                        throw new Exception("custom task gap result/builder completion");
                    var customFailure=new InvalidOperationException("custom-gap-fault");
                    ReceiverTrace.Failure=customFailure;
                    try{await ReceiverProbe.CustomGap(customGap,131,Task.CompletedTask);throw new Exception("missing custom task fault");}
                    catch(InvalidOperationException caught) when(ReferenceEquals(caught,customFailure)){}
                    finally{ReceiverTrace.Failure=null;}
                    if(customGap.Runs!=2||ReceiverTrace.Helpers!=2||!BuilderTrace.Value.Contains("error;",StringComparison.Ordinal))
                        throw new Exception("custom task gap fault/builder effects");
                }
            }
            """ + "\n#pragma warning disable HM0001\n" + CustomBuilderChecks.Support;
        const string newSource = """
            #nullable enable
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            public static class NewReceiverRunner {
                public static void Run() {
                    var strings=new ReceiverBox<string?>();
                    ReceiverTrace.Helpers=0;ReceiverTrace.UseDefault=true;
                    if(ReceiverProbe.M(strings,"first") is not null||strings.Runs!=1||strings.Seed!="first"||ReceiverTrace.Helpers!=1)
                        throw new Exception("reference default/effects");
                    ReceiverTrace.UseDefault=false;
                    if(ReceiverProbe.Block(strings,"second")!="second"||strings.Runs!=2||strings.Seed!="second"||ReceiverTrace.Helpers!=2)
                        throw new Exception("reference result/effects");
                    var values=new ReceiverBox<int?>();ReceiverTrace.UseDefault=true;
                    if(ReceiverProbe.M(values,(int?)13) is not null||values.Runs!=1||values.Seed!=13||ReceiverTrace.Helpers!=3)
                        throw new Exception("physical nullable/effects");
                    var failure=new InvalidOperationException("new-receiver-fault");ReceiverTrace.Failure=failure;
                    try{ReceiverProbe.M(strings,"fault");throw new Exception("missing fault");}
                    catch(InvalidOperationException caught) when(ReferenceEquals(caught,failure)){}
                    finally{ReceiverTrace.Failure=null;}
                    if(strings.Runs!=3||ReceiverTrace.Helpers!=4)throw new Exception("reference fault effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var listStrings=new ListReceiverBox<string?>();var list=ReceiverProbe.List(listStrings,"item");
                    if(list.Count!=2||list[0]!="item"||list[1] is not null||listStrings.Runs!=1||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("new list reference result/effects");
                    var listValues=new ListReceiverBox<int?>();var valueList=ReceiverProbe.List(listValues,(int?)59);
                    if(valueList.Count!=2||valueList[0]!=59||valueList[1] is not null||listValues.Runs!=1||ReceiverTrace.Helpers!=2||ReceiverTrace.Packs!=2)
                        throw new Exception("new list physical nullable/effects");
                    var tupleBox=new TupleReceiverBox<string?,int>();var pair=ReceiverProbe.Tuple(tupleBox,"tuple",61);
                    if(pair.value is not null||pair.tag!=61||tupleBox.Runs!=1||ReceiverTrace.Helpers!=3||ReceiverTrace.Packs!=2)
                        throw new Exception("new tuple nullable/sibling/effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var interfaceStrings=new ReadOnlyReceiverBox<string?>();
                    var nullableArm=ReceiverProbe.InterfaceConditional(interfaceStrings,"conditional",true);
                    if(nullableArm.Count!=2||nullableArm[0]!="conditional"||nullableArm[1] is not null||interfaceStrings.Runs!=1||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("new interface conditional reference/effects");
                    var ordinaryArm=ReceiverProbe.InterfaceSwitch(interfaceStrings,"switch",false);
                    if(ordinaryArm.Count!=1||ordinaryArm[0]!="switch"||interfaceStrings.Runs!=2||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("new interface switch ordinary arm/effects");
                    var interfaceValues=new ReadOnlyReceiverBox<int?>();
                    var physicalArm=ReceiverProbe.InterfaceSwitch(interfaceValues,(int?)79,true);
                    if(physicalArm.Count!=2||physicalArm[0]!=79||physicalArm[1] is not null||interfaceValues.Runs!=1||ReceiverTrace.Helpers!=2||ReceiverTrace.Packs!=2)
                        throw new Exception("new interface switch physical nullable/effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var longBox=new ListReceiverBox<long>();var mixedLong=ReceiverProbe.MixedListConditional(longBox,101L,true);
                    if(mixedLong.Count!=2||mixedLong[0]!=101L||mixedLong[1]!=0L||longBox.Runs!=1||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("new mixed list long nullable arm/effects");
                    var mixedLongOther=ReceiverProbe.MixedListSwitch(longBox,103L,false);
                    if(mixedLongOther.Count!=2||mixedLongOther[0]!=103L||mixedLongOther[1]!=103L||longBox.Runs!=2||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=2)
                        throw new Exception("new mixed list long ordinary arm/effects");
                    var guid=Guid.NewGuid();var guidBox=new ListReceiverBox<Guid>();var mixedGuid=ReceiverProbe.MixedListSwitch(guidBox,guid,true);
                    if(mixedGuid.Count!=2||mixedGuid[0]!=guid||mixedGuid[1]!=Guid.Empty||guidBox.Runs!=1||ReceiverTrace.Helpers!=2||ReceiverTrace.Packs!=3)
                        throw new Exception("new mixed list struct domain/effects");
                    var mixedFailure=new InvalidOperationException("mixed-list-fault");ReceiverTrace.Failure=mixedFailure;
                    try{ReceiverProbe.MixedListConditional(longBox,107L,true);throw new Exception("missing mixed list fault");}
                    catch(InvalidOperationException caught) when(ReferenceEquals(caught,mixedFailure)){}
                    finally{ReceiverTrace.Failure=null;}
                    if(longBox.Runs!=3||ReceiverTrace.Helpers!=3||ReceiverTrace.Packs!=3)
                        throw new Exception("new mixed list fault effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var nestedStrings=new NestedReceiverOuter<string?>.Inner<int>();
                    if(ReceiverProbe.NestedOuter(nestedStrings,"outer") is not null||nestedStrings.Runs!=1||nestedStrings.Seed!="outer"||ReceiverTrace.Helpers!=1)
                        throw new Exception("new nested enclosing reference/effects");
                    var nestedValues=new NestedReceiverOuter<int?>.Inner<string>();
                    if(ReceiverProbe.NestedOuter(nestedValues,(int?)43) is not null||nestedValues.Runs!=1||nestedValues.Seed!=43||ReceiverTrace.Helpers!=2)
                        throw new Exception("new nested enclosing physical nullable/effects");
                    var nestedInner=new NestedReceiverOuter<long>.Inner<string?>();
                    if(ReceiverProbe.NestedInner(nestedInner,"inner") is not null||nestedInner.InnerRuns!=1||nestedInner.InnerSeed!="inner"||ReceiverTrace.Helpers!=3)
                        throw new Exception("new nested own reference/effects");
                    var nestedList=ReceiverProbe.NestedList(nestedStrings,"list");
                    if(nestedList.Count!=2||nestedList[0]!="list"||nestedList[1] is not null||nestedStrings.ListRuns!=1||nestedStrings.Seed!="list"||ReceiverTrace.Helpers!=4||ReceiverTrace.Packs!=1)
                        throw new Exception("new nested list reference/effects");
                    var deep=new DeepReceiverOuter<string?>.Middle.Inner<int>();
                    if(ReceiverProbe.DeepOuter(deep,"deep") is not null||deep.Runs!=1||deep.Seed!="deep"||ReceiverTrace.Helpers!=5)
                        throw new Exception("new deep nested reference/effects");
                    var nestedFailure=new InvalidOperationException("nested-owner-fault");ReceiverTrace.Failure=nestedFailure;
                    try{ReceiverProbe.NestedOuter(nestedStrings,"fault");throw new Exception("missing nested fault");}
                    catch(InvalidOperationException caught) when(ReferenceEquals(caught,nestedFailure)){}
                    finally{ReceiverTrace.Failure=null;}
                    if(nestedStrings.Runs!=2||ReceiverTrace.Helpers!=6)
                        throw new Exception("new nested fault effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.UseDefault=true;
                    var treeGap=new TreeGapReceiver<string?>();
                    if(ReceiverProbe.TreeGap(treeGap,"quoted") is not null||treeGap.Runs!=1||ReceiverTrace.Helpers!=1
                        ||treeGap.Last?.Body is not System.Linq.Expressions.MethodCallExpression { Method.Name: "Maybe" })
                        throw new Exception("new quoted gap reference/structure/effects");
                    ReceiverTrace.UseDefault=false;
                    var sentinel=new Uri("https://example.invalid/");var uriTree=new TreeGapReceiver<Uri?>();
                    if(!ReferenceEquals(ReceiverProbe.TreeGap(uriTree,sentinel),sentinel)||uriTree.Runs!=1||ReceiverTrace.Helpers!=2)
                        throw new Exception("new quoted gap reference identity/effects");
                    ReceiverTrace.UseDefault=true;
                    var valueTree=new TreeGapReceiver<int?>();
                    if(ReceiverProbe.TreeGap(valueTree,(int?)139) is not null||valueTree.Runs!=1||ReceiverTrace.Helpers!=3)
                        throw new Exception("new quoted gap physical nullable/effects");
                    var nestedGap=new NestedGapReceiver<string?>();
                    if(ReceiverProbe.NestedGap(nestedGap,"nested") is not null||nestedGap.Runs!=1||ReceiverTrace.Helpers!=4)
                        throw new Exception("new nested delegate gap reference/effects");
                    var ownGap=new OwnGapReceiver<string?,int>();
                    if(ReceiverProbe.OwnGap(ownGap,"own",149) is not null||ownGap.Runs!=1||ownGap.LastTag!=149||ReceiverTrace.Helpers!=5)
                        throw new Exception("new nested own-slot gap sibling/effects");
                    var shadowGap=new ShadowGapReceiver<int?>();
                    if(ReceiverProbe.ShadowGap(shadowGap,(int?)151) is not null||shadowGap.Runs!=1||ReceiverTrace.Helpers!=6)
                        throw new Exception("new nested shadow gap physical nullable/effects");
                }
                public static async Task RunAsync() {
                    ReceiverTrace.Helpers=0;ReceiverTrace.UseDefault=true;
                    var taskBox=new AsyncReceiverBox<string?>();var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var pending=ReceiverProbe.Async(taskBox,"text",gate.Task);
                    if(pending.IsCompleted||taskBox.Runs!=1||ReceiverTrace.Helpers!=0)throw new Exception("new task suspension");
                    gate.SetResult();
                    if(await pending is not null||ReceiverTrace.Helpers!=1)throw new Exception("new task nullable result");
                    var valueBox=new ValueAsyncReceiverBox<string?>();var valueGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    ReceiverTrace.UseDefault=false;
                    var valuePending=ReceiverProbe.ValueAsync(valueBox,"value",valueGate.Task);
                    if(valuePending.IsCompleted||valueBox.Runs!=1||ReceiverTrace.Helpers!=1)throw new Exception("new value task suspension");
                    valueGate.SetResult();
                    if(await valuePending!="value"||ReceiverTrace.Helpers!=2)throw new Exception("new value task result");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var listBox=new AsyncListReceiverBox<string?>();var listGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var listPending=ReceiverProbe.AsyncList(listBox,"async-list",listGate.Task);
                    if(listPending.IsCompleted||listBox.Runs!=1||ReceiverTrace.Helpers!=0||ReceiverTrace.Packs!=0)
                        throw new Exception("new async list suspension");
                    listGate.SetResult();var list=await listPending;
                    if(list.Count!=2||list[0]!="async-list"||list[1] is not null||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("new async list nullable/effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;
                    var interfaceBox=new AsyncReadOnlyReceiverBox<string?>();var interfaceGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var interfacePending=ReceiverProbe.AsyncInterfaceConditional(interfaceBox,"async-interface",interfaceGate.Task,true);
                    if(interfacePending.IsCompleted||interfaceBox.Runs!=1||ReceiverTrace.Helpers!=0||ReceiverTrace.Packs!=0)
                        throw new Exception("new async interface suspension");
                    interfaceGate.SetResult();var interfaceResult=await interfacePending;
                    if(interfaceResult.Count!=2||interfaceResult[0]!="async-interface"||interfaceResult[1] is not null||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("new async interface reference/effects");
                    var physicalBox=new AsyncReadOnlyReceiverBox<int?>();var physicalGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var physicalPending=ReceiverProbe.AsyncInterfaceConditional(physicalBox,(int?)83,physicalGate.Task,true);
                    if(physicalPending.IsCompleted||physicalBox.Runs!=1||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("new async interface physical suspension");
                    physicalGate.SetResult();var physicalResult=await physicalPending;
                    if(physicalResult.Count!=2||physicalResult[0]!=83||physicalResult[1] is not null||ReceiverTrace.Helpers!=2||ReceiverTrace.Packs!=2)
                        throw new Exception("new async interface physical nullable/effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.UseDefault=true;BuilderTrace.Value="";
                    var customGap=new CustomGapReceiver<string?>();var customGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var customPending=ReceiverProbe.CustomGap(customGap,"async",customGate.Task);
                    if(customPending.GetAwaiter().IsCompleted||customGap.Runs!=1||ReceiverTrace.Helpers!=0)
                        throw new Exception("new custom task gap suspension");
                    customGate.SetResult();
                    if(await customPending is not null||ReceiverTrace.Helpers!=1
                        ||!BuilderTrace.Value.Contains("result;",StringComparison.Ordinal))
                        throw new Exception("new custom task gap reference/builder effects");
                    var valueGap=new CustomGapReceiver<int?>();var customValueGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var customValuePending=ReceiverProbe.CustomGap(valueGap,(int?)157,customValueGate.Task);
                    if(customValuePending.GetAwaiter().IsCompleted||valueGap.Runs!=1||ReceiverTrace.Helpers!=1)
                        throw new Exception("new custom task gap physical suspension");
                    customValueGate.SetResult();
                    if(await customValuePending is not null||ReceiverTrace.Helpers!=2)
                        throw new Exception("new custom task gap physical nullable/effects");
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    var cancelGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var canceledTask=ReceiverProbe.CustomGap(customGap,"cancel",cancelGate.Task);
                    cancelGate.SetCanceled(canceled.Token);
                    try{await canceledTask;throw new Exception("missing custom gap cancellation");}
                    catch(OperationCanceledException caught) when(caught.CancellationToken==canceled.Token){}
                    if(customGap.Runs!=2||ReceiverTrace.Helpers!=2)
                        throw new Exception("new custom task gap cancellation effects");
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "LambdaReceivers", "LambdaReceivers", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Receivers.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
            new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var methods = changed.GetTypeByMetadataName("ReceiverProbe")!;
        foreach (var name in new[] { "M", "Block", "List", "Tuple", "Async", "ValueAsync", "AsyncList", "InterfaceConditional", "InterfaceSwitch", "AsyncInterfaceConditional", "TreeGap", "NestedGap", "OwnGap", "ShadowGap", "CustomGap" })
        {
            var method = methods.GetMembers(name).OfType<IMethodSymbol>().Single();
            var expectedParameters = name is "Tuple" or "OwnGap" ? 2 : 1;
            if (method.TypeParameters.Length != expectedParameters || method.Parameters[0].Type is not INamedTypeSymbol owner
                || owner.TypeArguments[0].NullableAnnotation != NullableAnnotation.Annotated)
            {
                var compilerErrors = original.GetDiagnostics(cancellationToken).Where(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).Take(12);
                throw new InvalidOperationException("Lambda receiver Fix All missed " + name + ": " + method.ToDisplayString()
                    + "; original errors: " + string.Join(" | ", compilerErrors));
            }
        }
        foreach (var name in new[] { "MixedListConditional", "MixedListSwitch" })
        {
            var method = methods.GetMembers(name).OfType<IMethodSymbol>().Single();
            if (method.TypeParameters.Length != 1 || !method.TypeParameters[0].HasValueTypeConstraint
                || method.Parameters[0].Type is not INamedTypeSymbol owner
                || owner.TypeArguments[0].NullableAnnotation == NullableAnnotation.Annotated)
            { throw new InvalidOperationException("Lambda value receiver Fix All missed " + name + ": " + method.ToDisplayString()); }
        }
        foreach (var name in new[] { "NestedOuter", "NestedList", "DeepOuter", "NestedInner" })
        {
            var method = methods.GetMembers(name).OfType<IMethodSymbol>().Single();
            var owner = method.Parameters[0].Type as INamedTypeSymbol;
            var argument = name == "NestedInner" ? owner?.TypeArguments[0]
                : name == "DeepOuter" ? owner?.ContainingType?.ContainingType?.TypeArguments[0]
                : owner?.ContainingType?.TypeArguments[0];
            if (method.TypeParameters.Length != 2 || argument?.NullableAnnotation != NullableAnnotation.Annotated)
            { throw new InvalidOperationException("Nested lambda receiver Fix All missed " + name + ": " + method.ToDisplayString()); }
        }
        var contextBefore = new System.Runtime.Loader.AssemblyLoadContext("BeforeLambdaReceivers", isCollectible: true);
        var contextAfter = new System.Runtime.Loader.AssemblyLoadContext("AfterLambdaReceivers", isCollectible: true);
        try
        {
            async Task Execute(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext, string runner)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                ArgumentNullException.ThrowIfNull(runner);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Lambda receiver emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                var assembly = loadContext.LoadFromStream(image);
                var runnerType = assembly.GetType(runner)!;
                try
                {
                    runnerType.GetMethod("Run")!.Invoke(null, null);
                    await ((Task)runnerType.GetMethod("RunAsync")!.Invoke(null, null)!).WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is { } inner)
                { throw new InvalidOperationException(runner + ": " + inner.Message, inner); }
            }
            await Execute(original.WithAssemblyName("BeforeLambdaReceivers"), contextBefore, "OriginalReceiverRunner").ConfigureAwait(false);
            await Execute(changed.WithAssemblyName("AfterLambdaReceivers"), contextAfter, "OriginalReceiverRunner").ConfigureAwait(false);
            await Execute(changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText(newSource, cancellationToken: cancellationToken))
                .WithAssemblyName("NewLambdaReceivers"), contextAfter, "NewReceiverRunner").ConfigureAwait(false);
        }
        finally { contextBefore.Unload(); contextAfter.Unload(); }
        Program.Log.Info("PASS LambdaReceiverFixAllOriginalAndNewNullableDomainsEffectsAndFaults");
        return 1;
    }
}
