using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class ParameterEnumeratorChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string support = """
            #nullable enable
            public class CursorBase<T>{public bool MoveNext()=>false;public T Current=>default!;}
            public class Cursor<T>:CursorBase<T>{}
            public class AsyncCursorBase<T>{public System.Threading.Tasks.ValueTask<bool> MoveNextAsync()=>new(false);public T Current=>default!;public System.Threading.Tasks.ValueTask DisposeAsync()=>default;}
            public class AsyncCursor<T>:AsyncCursorBase<T>{}
            public class ParameterSequence<T,E> where E:CursorBase<T>{public E GetEnumerator()=>default!;}
            public class AsyncParameterSequence<T,E> where E:AsyncCursorBase<T>{public E GetAsyncEnumerator()=>default!;}
            public class AsyncInterfaceCursor<T>:AsyncCursorBase<T>,System.Collections.Generic.IAsyncEnumerator<T>{}
            public class AsyncInterfaceSequence<T,E> where E:System.Collections.Generic.IAsyncEnumerator<T>{public E GetAsyncEnumerator()=>default!;}
            public class ExtensionParameterSequence<T,E> where E:CursorBase<T>{}
            public static class ParameterExtensions{public static E GetEnumerator<T,E>(this ExtensionParameterSequence<T,E> values) where E:CursorBase<T> =>default!;}
            public class InterfaceSequence<T,E> where E:System.Collections.Generic.IEnumerator<T>{public E GetEnumerator()=>default!;}
            public struct ValueCursor<T>:System.Collections.Generic.IEnumerator<T>{public bool MoveNext()=>false;public T Current=>default!;object System.Collections.IEnumerator.Current=>Current!;public void Reset(){}public void Dispose(){}}
            """;
        (string Name, string Source, string Signature)[] tests =
        [
            ("ParameterEnumeratorExtension", "static int M(ExtensionParameterSequence<int,Cursor<int>> values,int seed){foreach(var item in values)seed=item;return seed;}", "T1 M<T1>(global::ExtensionParameterSequence<T1, global::Cursor<T1>> values, T1 seed)"),
            ("ParameterAsyncEnumeratorInterface", "static async System.Threading.Tasks.Task<int> M(AsyncInterfaceSequence<int,AsyncInterfaceCursor<int>> values,int seed){await foreach(var item in values)seed=item;return seed;}", "global::System.Threading.Tasks.Task<T1> M<T1>(global::AsyncInterfaceSequence<T1, global::AsyncInterfaceCursor<T1>> values, T1 seed)"),
            ("ParameterEnumeratorBase", "static int M(ParameterSequence<int,Cursor<int>> values,int seed){foreach(var item in values)seed=item;return seed;}", "T1 M<T1>(global::ParameterSequence<T1, global::Cursor<T1>> values, T1 seed)"),
            ("ParameterEnumeratorArray", "static int[] M(ParameterSequence<int[],Cursor<int[]>> values,int[] seed){foreach(var item in values)seed=item;return seed;}", "T1[] M<T1>(global::ParameterSequence<T1[], global::Cursor<T1[]>> values, T1[] seed)"),
            ("ParameterAsyncEnumeratorBase", "static async System.Threading.Tasks.Task<int> M(AsyncParameterSequence<int,AsyncCursor<int>> values,int seed){await foreach(var item in values)seed=item;return seed;}", "global::System.Threading.Tasks.Task<T1> M<T1>(global::AsyncParameterSequence<T1, global::AsyncCursor<T1>> values, T1 seed)"),
            ("ParameterEnumeratorInterface", "static int M(InterfaceSequence<int,ValueCursor<int>> values,int seed){foreach(var item in values)seed=item;return seed;}", "T1 M<T1>(global::InterfaceSequence<T1, global::ValueCursor<T1>> values, T1 seed)")
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
        var runtimeChecks = await ParameterEnumeratorRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        return tests.Length + runtimeChecks;
    }
}
