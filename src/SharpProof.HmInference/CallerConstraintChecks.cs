using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

internal static class CallerConstraintChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string? Signature)[] cases =
        [
            ("PartialAsyncBoxing", "public static partial System.Threading.Tasks.Task<object?> M(object? value,int tag);public static async partial System.Threading.Tasks.Task<object> M(object value,int tag){await System.Threading.Tasks.Task.Yield();return value;}public static System.Threading.Tasks.Task<object?> Use()=>M(42,1);", "global::System.Threading.Tasks.Task<object> M<T1>(object value, T1 tag)"),
            ("PartialBoxing", "public static partial object? M(object? value,int tag);public static partial object M(object value,int tag)=>value;public static object? Use()=>M(42,1);", "object M<T1>(object value, T1 tag)"),
            ("ReferenceMember", "public static string M(object value,int tag){Need(value);return value.ToString()!;}static void Need<T>(T value)where T:class{}public static string Use()=>M(42,1);", "string M<T1>(object value, T1 tag)"),
            ("ReferenceValue", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:class{}public static object Use()=>M(42,1);", "object M<T1>(object value, T1 tag)"),
            ("Constructor", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:new(){}public static object Use()=>M(\"payload\",1);", "object M<T1>(object value, T1 tag)"),
            ("BaseConversion", "public class Base{}public class Convertible{public static implicit operator Base(Convertible value)=>new();}public static Base M(Base value,int tag){Need(value);return value;}static void Need<T>(T value)where T:Base{}public static Base Use()=>M(new Convertible(),1);", "global::Cases.Base M<T1>(global::Cases.Base value, T1 tag)"),
            ("InterfaceConversion", "public class Base:System.IComparable{public int CompareTo(object? other)=>0;}public struct Convertible{public static implicit operator Base(Convertible value)=>new();}public static Base M(Base value,int tag){Need(value);return value;}static void Need<T>(T value)where T:System.IComparable{}public static Base Use()=>M(new Convertible(),1);", "global::Cases.Base M<T1>(global::Cases.Base value, T1 tag)"),
            ("StructConversion", "public struct Value{public int Number;}public class Convertible{public static implicit operator Value(Convertible value)=>default;}public static Value M(Value value,int tag){Need(value);return value;}static void Need<T>(T value)where T:struct{}public static Value Use()=>M(new Convertible(),1);", "global::Cases.Value M<T1>(global::Cases.Value value, T1 tag)"),
            ("UnmanagedConversion", "public struct Value{public int Number;}public struct Convertible{public object Payload;public static implicit operator Value(Convertible value)=>default;}public static Value M(Value value,int tag){Need(value);return value;}static void Need<T>(T value)where T:unmanaged{}public static Value Use()=>M(new Convertible{Payload=new object()},1);", "global::Cases.Value M<T1>(global::Cases.Value value, T1 tag)"),
            ("Sibling", "public static (object,int) M(object value,int sibling,int tag){Need(value);return(value,sibling);}static void Need<T>(T value)where T:class{}public static (object,int) Use()=>M(42,7,1);", "(object, T1) M<T1, T2>(object value, T1 sibling, T2 tag)"),
            ("Tuple", "public static (object left,int right) M((object left,int right) pair,int tag){Need(pair.left);return pair;}static void Need<T>(T value)where T:class{}public static (object left,int right) Use()=>M((42,7),1);", "(object left, T1 right) M<T1, T2>((object left, T1 right) pair, T2 tag)"),
            ("Extension", "public static object M(this object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:class{}public static object Use()=>(42).M(1);", "object M<T1>(this object value, T1 tag)"),
            ("Named", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:class{}public static object Use()=>M(tag:1,value:42);", "object M<T1>(object value, T1 tag)"),
            ("OpenCaller", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:class{}public static object Use<U>(U value)=>M(value!,1);", "object M<T1>(object value, T1 tag)"),
            ("Satisfied", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:class{}public static object Use()=>M((object)42,1);", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : class"),
            ("NoResidual", "public static object M(object value){Need(value);return value;}static void Need<T>(T value)where T:class{}public static object Use()=>M(42);", null)
        ];
        var failures = new List<string>();
        foreach (var test in cases)
        {
            foreach (var separateCaller in new[] { false, true })
            {
                var tree = CSharpSyntaxTree.ParseText("#nullable enable\npublic static partial class Cases{" + test.Source + "}", path: "Implementation.cs", cancellationToken: cancellationToken);
                SyntaxTree[] trees = [tree];
                if (separateCaller)
                {
                    var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                    var callers = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(method =>
                    { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == "Use"; }).ToArray();
                    var removed = root.RemoveNodes(callers, SyntaxRemoveOptions.KeepNoTrivia)!;
                    var callerRoot = SyntaxFactory.ParseCompilationUnit("#nullable enable\npublic static partial class Cases{}");
                    var owner = (ClassDeclarationSyntax)callerRoot.Members.Single();
                    callerRoot = callerRoot.ReplaceNode(owner, owner.WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>(callers)));
                    trees = [tree.WithRootAndOptions(removed, tree.Options), CSharpSyntaxTree.Create(callerRoot, (CSharpParseOptions)tree.Options, "Caller.cs")];
                }
                var compilation = Program.Compile(trees);
                var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return Inference.IsCompilationError(diagnostic); }).ToArray();
                if (errors.Length != 0)
                { throw new InvalidOperationException("Invalid caller constraint fixture " + test.Name + ": " + errors[0]); }
                var result = Program.Results(compilation, cancellationToken).Last(result =>
                { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
                if (test.Signature is null ? result.Status == "Suggestion" : result.Status != "Suggestion" || result.Signature != test.Signature)
                { failures.Add(test.Name + ": " + result.Status + ": " + (result.Signature ?? result.Message)); }
                else
                { Program.Log.Info("PASS CallerConstraint" + test.Name + (separateCaller ? "CrossFile" : "SameFile")); }
            }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return cases.Length * 2 + await CallerConstraintRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }
}
