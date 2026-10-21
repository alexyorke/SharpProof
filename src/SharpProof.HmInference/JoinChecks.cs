using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class JoinChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("LocalConditionalFixedResult", "static object M(bool flag,object value,int tag){object Local()=>flag?value:new object();_=Local();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : notnull"),
            ("LocalSwitchFixedResult", "static object M(int flag,object value,int tag){object Local()=>flag switch{0=>value,_=>new object()};_=Local();return value;}", "T1 M<T1, T2>(int flag, T1 value, T2 tag) where T1 : notnull"),
            ("LocalConditionalIndependentCaptures", "static (object,object) M(bool flag,object left,object right,int tag){object Local()=>flag?left:right;_=Local();return(left,right);}", "(T1, T2) M<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull"),
            ("LocalSwitchIndependentCaptures", "static (object,object) M(int flag,object left,object right,int tag){object Local()=>flag switch{0=>left,_=>right};_=Local();return(left,right);}", "(T1, T2) M<T1, T2, T3>(int flag, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull"),
            ("LocalNestedJoins", "static object M(bool flag,int key,object value,int tag){object Local()=>flag?(key switch{0=>value,_=>new object()}):new object();_=Local();return value;}", "T1 M<T1, T2>(bool flag, int key, T1 value, T2 tag) where T1 : notnull"),
            ("LocalNullableConditional", "static object? M(bool flag,object? value,int tag){object? Local()=>flag?value:null;_=Local();return value;}", "T1? M<T1, T2>(bool flag, T1? value, T2 tag) where T1 : class"),
            ("LocalNullableSwitch", "static object? M(int flag,object? value,int tag){object? Local()=>flag switch{0=>value,_=>null};_=Local();return value;}", "T1? M<T1, T2>(int flag, T1? value, T2 tag) where T1 : class"),
            ("LocalAsyncConditional", "static Task<object> M(bool flag,Task<object> value,int tag){async Task<object> Local()=>flag?await value:new object();_=Local();return value;}", "global::System.Threading.Tasks.Task<T1> M<T1, T2>(bool flag, global::System.Threading.Tasks.Task<T1> value, T2 tag) where T1 : notnull"),
            ("LocalValueAsyncSwitch", "static ValueTask<object> M(int flag,ValueTask<object> value,int tag){async ValueTask<object> Local()=>flag switch{0=>await value,_=>new object()};_=Local();return value;}", "global::System.Threading.Tasks.ValueTask<T1> M<T1, T2>(int flag, global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : notnull"),
            ("LocalTupleConditional", "static (object,int) M(bool flag,(object,int) value,int tag){(object,int) Local()=>flag?value:(new object(),0);_=Local();return value;}", "(T1, int) M<T1, T2>(bool flag, (T1, int) value, T2 tag) where T1 : notnull"),
            ("LocalArraySwitch", "static object[] M(int flag,object[] value,int tag){object[] Local()=>flag switch{0=>value,_=>new object[0]};_=Local();return value;}", "T1[] M<T1, T2>(int flag, T1[] value, T2 tag) where T1 : class"),
            ("LocalYieldConditional", "static object M(bool flag,object value,int tag){IEnumerable<object> Local(){yield return flag?value:new object();}foreach(var item in Local()){}return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : notnull"),
            ("LocalRefConditionalInvariant", "class Box<T>{public T Value=default!;}static (Box<object>,Box<object>) M(bool flag,Box<object> left,Box<object> right,int tag){ref object Local()=>ref(flag?ref left.Value:ref right.Value);_=Local();return(left,right);}", "(global::Cases.Box<object>, global::Cases.Box<object>) M<T1>(bool flag, global::Cases.Box<object> left, global::Cases.Box<object> right, T1 tag)"),
            ("ConditionalUserConversion", "struct Input{public static implicit operator long(Input value)=>0;}static long M(bool flag,Input left,long right,int tag)=>flag?left:right;", "long M<T1>(bool flag, global::Cases.Input left, long right, T1 tag)"),
            ("SwitchUserConversion", "struct Input{public static implicit operator long(Input value)=>0;}static long M(int flag,Input left,long right,int tag)=>flag switch{0=>left,_=>right};", "long M<T1>(int flag, global::Cases.Input left, long right, T1 tag)"),
            ("ConditionalNumericConversion", "static long M(bool flag,int left,long right,int tag)=>flag?left:right;", "long M<T1>(bool flag, int left, long right, T1 tag)"),
            ("SwitchNumericConversion", "static long M(int flag,int left,long right,int tag)=>flag switch{0=>left,_=>right};", "long M<T1>(int flag, int left, long right, T1 tag)"),
            ("ConditionalTargetConversion", "static object M(bool flag,int left,string right,int tag)=>flag?left:right;", "object M<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull"),
            ("SwitchTargetConversion", "static object M(int flag,int left,string right,int tag)=>flag switch{0=>left,_=>right};", "object M<T1, T2, T3>(int flag, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull"),
            ("ConditionalNullableLift", "static int? M(bool flag,int left,int? right,int tag)=>flag?left:right;", "T1? M<T1, T2>(bool flag, T1 left, T1? right, T2 tag) where T1 : struct"),
            ("SwitchNullableLift", "static int? M(int flag,int left,int? right,int tag)=>flag switch{0=>left,_=>right};", "T1? M<T1, T2>(int flag, T1 left, T1? right, T2 tag) where T1 : struct"),
            ("AsyncConditionalNullableLift", "static async Task<int?> M(bool flag,Task<int> left,Task<int?> right,int tag)=>flag?await left:await right;", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(bool flag, global::System.Threading.Tasks.Task<T1> left, global::System.Threading.Tasks.Task<T1?> right, T2 tag) where T1 : struct"),
            ("ConditionalHomogeneousGeneric", "static int M(bool flag,int left,int right)=>flag?left:right;", "T1 M<T1>(bool flag, T1 left, T1 right)"),
            ("SwitchHomogeneousGeneric", "static int M(int flag,int left,int right)=>flag switch{0=>left,_=>right};", "T1 M<T1>(int flag, T1 left, T1 right)"),
            ("ConditionalThrowArm", "static int M(bool flag,int value)=>flag?value:throw new Exception();", "T1 M<T1>(bool flag, T1 value)"),
            ("SwitchThrowArm", "static int M(int flag,int value)=>flag switch{0=>value,_=>throw new Exception()};", "T1 M<T1>(int flag, T1 value)")
        ];
        foreach (var test in tests)
        {
            var source = "#nullable enable\nusing System;using System.Collections.Generic;using System.Threading.Tasks;class Cases{" + test.Source + "}";
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS " + test.Name);
        }
        foreach (var (version, name) in new[] { (LanguageVersion.CSharp7_3, "ConditionalNumericConversion"), (LanguageVersion.CSharp8, "SwitchNumericConversion"), (LanguageVersion.CSharp9, "ConditionalTargetConversion") })
        {
            var test = tests.Single(candidate => candidate.Name == name);
            var tree = CSharpSyntaxTree.ParseText("class Cases{" + test.Source + "}", new CSharpParseOptions(version), cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException("Legacy join fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single();
            var expectedSignature = name == "ConditionalTargetConversion"
                ? "object M<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag)" : test.Signature;
            if (result.Signature != expectedSignature)
            { throw new InvalidOperationException("Legacy join " + version + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS ExpressionJoin" + version);
        }
        var legacyIndependent = tests.Single(candidate => candidate.Name == "LocalConditionalIndependentCaptures");
        var legacyTree = CSharpSyntaxTree.ParseText("#nullable enable\nclass Cases{" + legacyIndependent.Source + "}", new CSharpParseOptions(LanguageVersion.CSharp8), cancellationToken: cancellationToken);
        var legacyResult = Program.Results(Program.Compile([legacyTree]), cancellationToken).Single();
        const string legacySignature = "(T1, T2) M<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull, T2 where T2 : notnull";
        if (legacyResult.Signature != legacySignature)
        { throw new InvalidOperationException("Legacy conditional relationship: " + (legacyResult.Signature ?? legacyResult.Message)); }
        Program.Log.Info("PASS LegacyConditionalDependentCaptureDomains");
        var mixedTree = CSharpSyntaxTree.ParseText("#nullable enable\nclass Cases{static object M(bool flag,object left,string right,int tag){object Local()=>flag?left:right;_=Local();return left;}}", new CSharpParseOptions(LanguageVersion.CSharp8), cancellationToken: cancellationToken);
        var mixedResult = Program.Results(Program.Compile([mixedTree]), cancellationToken).Single();
        if (mixedResult.Signature != "object M<T1, T2>(bool flag, object left, T1 right, T2 tag) where T1 : notnull")
        { throw new InvalidOperationException("Legacy conditional mixed endpoints: " + (mixedResult.Signature ?? mixedResult.Message)); }
        Program.Log.Info("PASS LegacyConditionalMixedConversionEndpoints");
        return tests.Length + 5;
    }
}
