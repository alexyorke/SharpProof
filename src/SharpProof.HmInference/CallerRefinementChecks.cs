using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class CallerRefinementChecks
{
    internal const string Support = VariantProjectionChecks.Support + """
        public sealed class ConcreteDispatchProducer:SplitProducer<string>{}
        public static class RefinementApi{public static System.Type? ArgumentType;public static System.Type? ReceiverType;public static U Read<T,U>(T value,U other) where T:BoundProducer<U>{ArgumentType=typeof(U);ReceiverType=typeof(T);BoundTrace.Value+="bind;";return value.Read();}}
        """;

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Method, string Caller, string Signature)[] tests =
        [
            ("MultipleCallers", "public static object M(ConcreteDispatchProducer first,object left,ConcreteDispatchProducer second,object right,int tag){_=RefinementApi.Read(first,left);return RefinementApi.Read(second,right);}", "static object CallerA(ConcreteDispatchProducer value)=>M(value,23,value,new object(),0);static object CallerB(ConcreteDispatchProducer value)=>M(value,new object(),value,23,0);", "object M<T1, T2, T3>(T1 first, object left, T2 second, object right, T3 tag) where T1 : global::BoundProducer<object> where T2 : global::BoundProducer<object>"),
            ("BoxedArgument", "public static object M(ConcreteDispatchProducer value,object other)=>RefinementApi.Read(value,other);", "static object Caller(ConcreteDispatchProducer value)=>M(value,23);", "object M<T1>(T1 value, object other) where T1 : global::BoundProducer<object>"),
            ("ReferenceArgument", "public static object M(ConcreteDispatchProducer value,object other)=>RefinementApi.Read(value,other);", "static object Caller(ConcreteDispatchProducer value)=>M(value,new string[0]);", "object M<T1>(T1 value, object other) where T1 : global::BoundProducer<object>"),
            ("TupleComponent", "public static object M(ConcreteDispatchProducer value,(object Tag,int Count) other)=>RefinementApi.Read(value,other.Tag);", "static object Caller(ConcreteDispatchProducer value)=>M(value,(23,0));", "object M<T1, T2>(T1 value, (object Tag, T2 Count) other) where T1 : global::BoundProducer<object>"),
            ("ArrayComponent", "public static object M(ConcreteDispatchProducer value,object[] other)=>RefinementApi.Read(value,other[0]);", "static object Caller(ConcreteDispatchProducer value)=>M(value,new[]{new string[0]});", "object M<T1>(T1 value, object[] other) where T1 : global::BoundProducer<object>"),
            ("DelegateComponent", "public static object M(ConcreteDispatchProducer value,System.Func<object> other)=>RefinementApi.Read(value,other());", "static object Caller(ConcreteDispatchProducer value)=>M(value,()=>new string[0]);", "object M<T1>(T1 value, global::System.Func<object> other) where T1 : global::BoundProducer<object>"),
            ("MethodGroup", "public static object M(ConcreteDispatchProducer value,object other)=>RefinementApi.Read(value,other);", "static System.Func<ConcreteDispatchProducer,string[],object> Caller()=>M;", "object M<T1>(T1 value, object other) where T1 : global::BoundProducer<object>"),
            ("Extension", "public static object M(this ConcreteDispatchProducer value,object other)=>RefinementApi.Read(value,other);", "static object Caller(ConcreteDispatchProducer value)=>value.M(23);", "object M<T1>(this T1 value, object other) where T1 : global::BoundProducer<object>")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(Support + "public static class Cases{" + test.Method + test.Caller + "}", cancellationToken: cancellationToken)]);
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
            if (!emitted.Success)
            { throw new InvalidOperationException("Caller refinement fixture " + test.Name + ": " + string.Join("; ", emitted.Diagnostics)); }
            var result = Program.Results(compilation, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { failures.Add(test.Name + ": " + result.Status + ": " + (result.Signature ?? result.Message)); }
            else
            { Program.Log.Info("PASS CallerRefinement" + test.Name); }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        var declaration = CSharpSyntaxTree.ParseText(Support + "public static class AcrossFiles{public static object M(ConcreteDispatchProducer value,object other)=>RefinementApi.Read(value,other);}", path: "Declaration.cs", cancellationToken: cancellationToken);
        var caller = CSharpSyntaxTree.ParseText("public static class OtherFile{public static object Call(ConcreteDispatchProducer value)=>AcrossFiles.M(value,23);}", path: "Caller.cs", cancellationToken: cancellationToken);
        var across = Program.Compile([declaration, caller]);
        using var acrossImage = new MemoryStream();
        if (!across.Emit(acrossImage, cancellationToken: cancellationToken).Success)
        { throw new InvalidOperationException("Cross-file caller fixture failed emission."); }
        var acrossResult = Program.Results(across, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
        if (acrossResult.Status != "Suggestion" || acrossResult.Signature != "object M<T1>(T1 value, object other) where T1 : global::BoundProducer<object>")
        { throw new InvalidOperationException("Cross-file refinement failed: " + acrossResult.Status + ": " + (acrossResult.Signature ?? acrossResult.Message)); }
        Program.Log.Info("PASS CallerRefinementAcrossSuppliedFiles");
        var unchanged = Program.Compile([CSharpSyntaxTree.ParseText("class Cases{public static object M(object value)=>value;static object Caller()=>M(23);}", cancellationToken: cancellationToken)]);
        if (Program.Results(unchanged, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; }).Status != "Rejected")
        { throw new InvalidOperationException("An unrefinable proposal must retain its original rejection."); }
        Program.Log.Info("PASS CallerRefinementTerminatesWhenNoGenericComponentRemains");
        return tests.Length + 2 + await CallerRefinementRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }
}
