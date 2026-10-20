using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class ArrayConstructionChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] cases =
        [
            ("ImplicitUserConversion", "static Target<int>[] M(Source<int> x,Target<int> y)=>new[]{x,y}; struct Source<T>{public static implicit operator Target<T>(Source<T> value)=>default;} struct Target<T>{}", "global::ArrayCases.Target<T1>[] M<T1>(global::ArrayCases.Source<T1> x, global::ArrayCases.Target<T1> y)"),
            ("ExplicitUserConversion", "static Target<int>[] M(Source<int> x,bool tag)=>new Target<int>[]{x}; struct Source<T>{public static implicit operator Target<T>(Source<T> value)=>default;} struct Target<T>{}", "global::ArrayCases.Target<int>[] M<T1>(global::ArrayCases.Source<int> x, T1 tag)"),
            ("ReferenceElement", "static object[] M(System.Collections.Generic.List<int> value)=>new object[]{value};", "object[] M<T1>(global::System.Collections.Generic.List<T1> value)"),
            ("JaggedBoxing", "static object[][] M(int value)=>new[]{new object[]{value}};", "object[][] M<T1>(T1 value) where T1 : notnull"),
            ("UnsignedDimension", "static int[] M(ulong size,bool tag)=>new int[size];", "int[] M<T1>(ulong size, T1 tag)"),

            ("ImplicitNull", "static string?[] M(string value)=>new[]{value,null};", "T1?[] M<T1>(T1 value) where T1 : class"),
            ("ImplicitDefault", "static int[] M(int value)=>new[]{value,default};", "T1?[] M<T1>(T1 value)"),
            ("ExplicitTuple", "static (object,int)[] M(int x,short y)=>new (object,int)[]{(x,y)};", "(object, int)[] M<T1>(T1 x, short y) where T1 : notnull"),
            ("ImplicitNumeric", "static long[] M(short x,long y,bool tag)=>new[]{x,y};", "long[] M<T1>(short x, long y, T1 tag)"),
            ("ExplicitNumeric", "static int[] M(short x,bool tag)=>new int[]{x};", "int[] M<T1>(short x, T1 tag)"),
            ("ExplicitBoxing", "static object[] M(int x)=>new object[]{x};", "object[] M<T1>(T1 x) where T1 : notnull"),
            ("ExplicitNullable", "static int?[] M(short? x,bool tag)=>new int?[]{x};", "int?[] M<T1>(short? x, T1 tag)"),
            ("ImplicitNullable", "static int?[] M(int x,int? y)=>new[]{x,y};", "T1?[] M<T1>(T1 x, T1? y) where T1 : struct"),
            ("ImplicitRectangular", "static int[,] M(int x,int y)=>new[,]{{x,y},{y,x}};", "T1[,] M<T1>(T1 x, T1 y)"),
            ("ExplicitRectangular", "static object[,] M(int x)=>new object[,]{{x,x}};", "object[,] M<T1>(T1 x) where T1 : notnull"),
            ("BareInitializer", "static object[] M(int x){object[] values={x};return values;}", "object[] M<T1>(T1 x) where T1 : notnull"),
            ("ShortDimension", "static int[] M(short size,bool tag)=>new int[size];", "int[] M<T1>(short size, T1 tag)"),
            ("LongDimension", "static int[] M(long size,bool tag)=>new int[size];", "int[] M<T1>(long size, T1 tag)"),
            ("AwaitedBoxing", "static async System.Threading.Tasks.Task<object[]> M(System.Threading.Tasks.Task<int> value)=>new object[]{await value};", "global::System.Threading.Tasks.Task<object[]> M<T1>(global::System.Threading.Tasks.Task<T1> value) where T1 : notnull"),
            ("AwaitedNullable", "static async System.Threading.Tasks.Task<int?[]> M(System.Threading.Tasks.Task<int> value,int? other)=>new[]{await value,other};", "global::System.Threading.Tasks.Task<T1?[]> M<T1>(global::System.Threading.Tasks.Task<T1> value, T1? other) where T1 : struct")
        ];
        var failures = new List<string>();
        foreach (var test in cases)
        {
            var tree = CSharpSyntaxTree.ParseText("#nullable enable\nclass ArrayCases{" + test.Source + "}", cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return Inference.IsCompilationError(diagnostic); }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException("Invalid array fixture " + test.Name + ": " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(result =>
            { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
            Program.Log.Info("PASS Array" + test.Name);
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { failures.Add(test.Name + ": " + result.Status + ": " + result.Message); }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return cases.Length + await ArrayConstructionRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }
}
