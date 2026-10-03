using System.Globalization;
using System.Text.Json;
using SharpProof.Ir;

namespace SharpProof.CompilerArtifact;

internal static class CompilerReachableSourceValidator
{
    internal static string BodyId(int tree, int start, int length)
    { return string.Format(CultureInfo.InvariantCulture, "source:{0}:{1}:{2}", tree, start, length); }

    internal static string TypeKey(IrFactory factory, IrTypeId type, int depth = 0)
    {
        Require(depth <= PortableIrGraphCodec.MaximumGraphDepth);
        var info = factory.GetTypeInfo(type);
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}:{3}:{4}",
            info.Kind, info.Width, info.Signed, factory.GetString(info.Name),
            info.ElementType is { } element ? TypeKey(factory, element, depth + 1) : string.Empty);
    }

    internal static void Validate(CompilerManifestArtifact manifest, CancellationToken cancellationToken)
    {
        var artifact = manifest.ReachableSource;
        if (artifact == null)
        { return; }
        Require(artifact.Bodies != null && artifact.Roots != null && artifact.Bodies.Length <= 4096);
        Require(artifact.Documents != null && artifact.Documents.Length <= artifact.Bodies!.Length);
        var documents = new Dictionary<int, CompilerSourceDocumentArtifact>();
        foreach (var document in artifact.Documents!)
        {
            Require(document != null && document.SourceTreeOrdinal >= 0 && document.MaximumBodyEnd >= 0 &&
                !string.IsNullOrWhiteSpace(document.Path) &&
                !documents.ContainsKey(document.SourceTreeOrdinal));
            documents.Add(document!.SourceTreeOrdinal, document);
        }
        var bodies = new Dictionary<string, CompilerSourceBodyArtifact>(StringComparer.Ordinal);
        var graphs = new Dictionary<string, DecodedPortableIrGraph>(StringComparer.Ordinal);
        foreach (var body in artifact.Bodies!)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(body != null && !string.IsNullOrWhiteSpace(body.MethodIdentity) &&
                documents.ContainsKey(body.SourceTreeOrdinal));
            Require(body!.Start >= 0 && body.Length > 0 &&
                body.Start <= documents[body.SourceTreeOrdinal].MaximumBodyEnd - body.Length &&
                body.BodyId == BodyId(body.SourceTreeOrdinal, body.Start, body.Length) &&
                !bodies.ContainsKey(body.BodyId) && body.Callees != null && body.Callees.Length <= 4096 &&
                body.SourceCalls != null && body.SourceCalls.Length <= 4096 &&
                body.ParameterTypes != null && body.ParameterTypes.Length <= 4096 &&
                body.ParameterTypes.All(type => !string.IsNullOrWhiteSpace(type)));
            bodies.Add(body.BodyId, body);
            if (body.Graph != null)
            {
                var decoded = PortableIrGraphCodec.Decode(body.Graph, cancellationToken: cancellationToken);
                Require(decoded.Factory.Semantics == IrExecutionSemantics.Total && decoded.Program != null &&
                    decoded.Program.Blocks.Length <= 4096 && decoded.Instructions.Count <= 4096);
                graphs.Add(body.BodyId, decoded);
                foreach (var operation in body.Graph.Operations)
                {
                    if (operation.SourceSpan is { } span)
                    {
                        Require(span.Document == documents[body.SourceTreeOrdinal].Path &&
                            span.Start >= body.Start && span.Length >= 0 &&
                            span.Start - body.Start <= body.Length - span.Length);
                    }
                }
            }
            else
            { Require(!body.IsCallSkeleton && body.SourceCalls!.Length == 0); }
        }
        Require(bodies.Values.Select(body => body.SourceTreeOrdinal).Distinct().Count() == documents.Count);
        foreach (var document in documents.Values)
        {
            Require(document.MaximumBodyEnd == bodies.Values.Where(body => body.SourceTreeOrdinal == document.SourceTreeOrdinal)
                .Max(body => body.Start + body.Length));
        }
        foreach (var body in bodies.Values)
        {
            Require(body.Callees.All(callee => callee != null && bodies.ContainsKey(callee)) &&
                body.Callees.Distinct(StringComparer.Ordinal).Count() == body.Callees.Length);
            if (artifact.CollectionComplete)
            { Require(body.CallsComplete); }
            if (graphs.TryGetValue(body.BodyId, out var graph))
            {
                var calls = graph.Instructions.OfType<IrCallInstruction>().ToArray();
                Require(body.IsCallSkeleton == (calls.Length != 0) && body.SourceCalls.Length == calls.Length);
                var seen = new HashSet<int>();
                foreach (var row in body.SourceCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Require(row != null && row.InstructionIndex >= 0 && row.InstructionIndex < graph.Instructions.Count &&
                        seen.Add(row.InstructionIndex) && row.CalleeBodyId != null && bodies.ContainsKey(row.CalleeBodyId));
                    Require(graph.Instructions[row!.InstructionIndex] is IrCallInstruction);
                    var call = (IrCallInstruction)graph.Instructions[row.InstructionIndex];
                    var callee = bodies[row.CalleeBodyId!];
                    var member = graph.Factory.GetMemberInfo(call.Member);
                    Require(callee.IsStatic && member.IsStatic && call.Receiver == null &&
                        !string.IsNullOrWhiteSpace(callee.CallIdentity) &&
                        graph.Factory.GetString(member.Name) == "shadow-call:" + callee.CallIdentity &&
                        member.ParameterTypes.Length == callee.ParameterTypes.Length &&
                        call.Arguments.Length == callee.ParameterTypes.Length &&
                        (callee.ReturnType == null ? call.Target == null && IsVoidType(graph.Factory, member.ReturnType) : call.Target != null &&
                            graph.Factory.GetVariableInfo(call.Target.Value).Type == member.ReturnType &&
                            TypeKey(graph.Factory, member.ReturnType) == callee.ReturnType));
                    for (var index = 0; index < member.ParameterTypes.Length; index++)
                    {
                        Require(TypeKey(graph.Factory, member.ParameterTypes[index]) == callee.ParameterTypes[index] &&
                            call.Arguments[index].Type == member.ParameterTypes[index]);
                    }
                }
                Require(new HashSet<string>(body.Callees, StringComparer.Ordinal).SetEquals(
                    body.SourceCalls.Select(call => call.CalleeBodyId)));
            }
        }
        var callableIds = new HashSet<string>(manifest.Manifest.Callables.Select(callable => callable.CallableId), StringComparer.Ordinal);
        var rootIds = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        foreach (var root in artifact.Roots!)
        {
            Require(root != null && root.CallableId != null && callableIds.Contains(root.CallableId) &&
                rootIds.Add(root.CallableId) && root.BodyId != null && bodies.ContainsKey(root.BodyId));
            Require(bodies[root!.BodyId!].MethodIdentity == root.CallableId);
            pending.Push(root!.BodyId!);
        }
        if (artifact.CollectionComplete)
        { Require(rootIds.Count == callableIds.Count); }
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = pending.Pop();
            if (!reachable.Add(id))
            { continue; }
            foreach (var callee in bodies[id].Callees)
            { pending.Push(callee); }
        }
        Require(reachable.Count == bodies.Count);
    }

    private static bool IsVoidType(IrFactory factory, IrTypeId type)
    {
        var info = factory.GetTypeInfo(type);
        return info.Kind == IrTypeKind.Reference && info.Width == 0 && !info.Signed && info.ElementType == null &&
            factory.GetString(info.Name).EndsWith("::System.Void", StringComparison.Ordinal);
    }

    private static void Require(bool condition)
    {
        if (!condition)
        { throw new JsonException("The reachable source graph is invalid."); }
    }
}
