using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

internal static class QueryChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("QueryQuotedDefault", "static IQueryable<int> M(IQueryable<int> values,bool flag,int tag)=>from x in values select flag?x:default;", "global::System.Linq.IQueryable<T1?> M<T1, T2>(global::System.Linq.IQueryable<T1> values, bool flag, T2 tag)"),
            ("QueryDefaultJoin", "static IEnumerable<int> M(IEnumerable<int> values,bool flag,int tag)=>from x in values select flag?x:default;", "global::System.Collections.Generic.IEnumerable<T1?> M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, bool flag, T2 tag)"),
            ("QueryNullJoin", "static IEnumerable<string?> M(IEnumerable<string> values,bool flag,int tag)=>from x in values select flag?x:null;", "global::System.Collections.Generic.IEnumerable<T1?> M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, bool flag, T2 tag) where T1 : class"),
            ("QueryNullableCoalesce", "static IEnumerable<int> M(IEnumerable<int?> values,int fallback,bool tag)=>from x in values select x??fallback;", "global::System.Collections.Generic.IEnumerable<T1> M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1?> values, T1 fallback, T2 tag) where T1 : struct"),
            ("QueryNested", "static IEnumerable<IEnumerable<(int left,string right)>> M(IEnumerable<int> values,IEnumerable<string> other,bool tag)=>from x in values select(from y in other select(x,y));", "global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.IEnumerable<(T1 left, T2 right)>> M<T1, T2, T3>(global::System.Collections.Generic.IEnumerable<T1> values, global::System.Collections.Generic.IEnumerable<T2> other, T3 tag)"),
            ("QueryCallbacks", "static IEnumerable<Func<int>> M(IEnumerable<int> values,bool tag)=>from x in values select (Func<int>)(()=>x);", "global::System.Collections.Generic.IEnumerable<global::System.Func<int>> M<T1>(global::System.Collections.Generic.IEnumerable<int> values, T1 tag)"),
            ("QueryIdentity", "static IEnumerable<int> M(IEnumerable<int> values,bool tag)=>from x in values select x;", "global::System.Collections.Generic.IEnumerable<T1> M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)"),
            ("QueryWhere", "static IEnumerable<int> M(IEnumerable<int> values,bool tag)=>from x in values where true select x;", "global::System.Collections.Generic.IEnumerable<T1> M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)"),
            ("QueryNumericControl", "static IEnumerable<int> M(IEnumerable<int> values,bool tag)=>from x in values where x>0 select x;", "global::System.Collections.Generic.IEnumerable<int> M<T1>(global::System.Collections.Generic.IEnumerable<int> values, T1 tag)"),
            ("QueryLet", "static IEnumerable<(int left,int right)> M(IEnumerable<int> values,bool tag)=>from x in values let pair=(x,x) select pair;", "global::System.Collections.Generic.IEnumerable<(T1 left, T1 right)> M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)"),
            ("QueryMany", "static IEnumerable<(int left,string right)> M(IEnumerable<int> values,IEnumerable<string> other,bool tag)=>from x in values from y in other select (x,y);", "global::System.Collections.Generic.IEnumerable<(T1 left, T2 right)> M<T1, T2, T3>(global::System.Collections.Generic.IEnumerable<T1> values, global::System.Collections.Generic.IEnumerable<T2> other, T3 tag)"),
            ("QueryOrder", "static IEnumerable<int> M(IEnumerable<int> values,bool tag)=>from x in values orderby x descending,x select x;", "global::System.Collections.Generic.IEnumerable<T1> M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)"),
            ("QueryContinuation", "static IEnumerable<int> M(IEnumerable<int> values,bool tag)=>from x in values select(x,x) into pair select pair.Item2;", "global::System.Collections.Generic.IEnumerable<T1> M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)"),
            ("QueryJoin", "static IEnumerable<(string left,bool right)> M(IEnumerable<(int key,string value)> left,IEnumerable<(int key,bool value)> right,int tag)=>from l in left join r in right on l.key equals r.key select(l.value,r.value);", "global::System.Collections.Generic.IEnumerable<(T2 left, T3 right)> M<T1, T2, T3, T4>(global::System.Collections.Generic.IEnumerable<(T1 key, T2 value)> left, global::System.Collections.Generic.IEnumerable<(T1 key, T3 value)> right, T4 tag)"),
            ("QueryGroupJoin", "static IEnumerable<IEnumerable<int>> M(IEnumerable<int> left,IEnumerable<int> right,bool tag)=>from l in left join r in right on l equals r into grouped select grouped;", "global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.IEnumerable<T1>> M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> left, global::System.Collections.Generic.IEnumerable<T1> right, T2 tag)"),
            ("QueryGrouping", "static IEnumerable<IGrouping<int,string>> M(IEnumerable<(int key,string value)> values,int tag)=>from x in values group x.value by x.key;", "global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<T1, T2>> M<T1, T2, T3>(global::System.Collections.Generic.IEnumerable<(T1 key, T2 value)> values, T3 tag)"),
            ("QueryGroupContinuation", "static IEnumerable<IGrouping<int,int>> M(IEnumerable<int> values,bool tag)=>from x in values group x by x into g select g;", "global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<T1, T1>> M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)"),
            ("QueryTypedFrom", "static IEnumerable<int> M(IEnumerable<object> values,bool tag)=>from int x in values select x;", "global::System.Collections.Generic.IEnumerable<int> M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)"),
            ("QueryQueryable", "static IQueryable<int> M(IQueryable<int> values,bool tag)=>from x in values select x;", "global::System.Linq.IQueryable<T1> M<T1, T2>(global::System.Linq.IQueryable<T1> values, T2 tag)"),
            ("QueryAsync", "static async Task<IEnumerable<int>> M(Task<IEnumerable<int>> values,bool tag)=>from x in await values select x;", "global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<T1>> M<T1, T2>(global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<T1>> values, T2 tag)"),
            ("QueryCustom", "class Q<T>{public Q<U> Select<U>(Func<T,U> selector)=>throw new NotImplementedException();}static Q<int> M(Q<int> values,bool tag)=>from x in values select x;", "global::Cases.Q<T1> M<T1, T2>(global::Cases.Q<T1> values, T2 tag)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var tree = CSharpSyntaxTree.ParseText("#nullable enable\nusing System;using System.Linq;using System.Collections.Generic;using System.Threading.Tasks;class Cases{" + test.Source + "}", cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { failures.Add(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); continue; }
            Program.Log.Info("PASS " + test.Name);
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        const string bindingSource = """
            using System;using System.Linq;using System.Collections.Generic;
            public sealed class Seq<T>{public IEnumerable<T> Values;public Seq(IEnumerable<T> values){Values=values;}}
            public static class Operators{
                public static IEnumerable<bool> Select(this Seq<int> values,Func<int,bool> selector){IEnumerable<bool> result=values.Values.Select(selector);return result;}
                public static IEnumerable<U> Select<T,U>(this Seq<T> values,Func<T,U> selector)=>values.Values.Select(selector);
            }
            public static class Consumer{public static IEnumerable<bool> Read(Seq<string> values)=>from x in values select true;}
            """;
        var bindingTree = CSharpSyntaxTree.ParseText(bindingSource, cancellationToken: cancellationToken);
        var bindingCompilation = Program.Compile([bindingTree]);
        var target = bindingTree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().First(method =>
        { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == "Select" && method.TypeParameterList is null; });
        var bindingResult = Inference.Analyze(target, bindingCompilation.GetSemanticModel(bindingTree), cancellationToken);
        if (bindingResult.Status == "Suggestion" || !bindingResult.Message.Contains("query operator", StringComparison.Ordinal))
        { throw new InvalidOperationException("Query operator rebinding: " + bindingResult.Status + ": " + bindingResult.Message); }
        Program.Log.Info("PASS QueryRejectsImplicitOperatorRebinding");
        foreach (var invalid in new[] { "from x in missing select x", "from x in values select Missing" })
        {
            var invalidTree = CSharpSyntaxTree.ParseText("using System.Linq;using System.Collections.Generic;class Invalid{static IEnumerable<int> M(IEnumerable<int> values)=>" + invalid + ";}", cancellationToken: cancellationToken);
            var invalidCompilation = Program.Compile([invalidTree]);
            var invalidMethod = invalidTree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var invalidResult = Inference.Analyze(invalidMethod, invalidCompilation.GetSemanticModel(invalidTree), cancellationToken);
            if (invalidResult.Status == "Suggestion")
            { throw new InvalidOperationException("Malformed query received a suggestion: " + invalid); }
            Program.Log.Info("PASS QueryMalformed " + invalid);
        }
        return tests.Length + 3;
    }
}
