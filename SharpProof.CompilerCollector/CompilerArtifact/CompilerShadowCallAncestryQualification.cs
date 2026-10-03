using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Contracts;
using SharpProof.Analyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;
namespace SharpProof.CompilerArtifact;
internal sealed record CompilerShadowCallAncestryNode(int Parent, string Caller, string Callee, string Document, int Start, int Length);
internal sealed record CompilerShadowCallAncestryLink(int InstructionIndex, int Node);
internal sealed class CompilerShadowCallAncestryArtifact
{
    public string RootIdentity { get; set; } = "";
    public CompilerShadowCallAncestryNode[] Nodes { get; set; } = [];
    public CompilerShadowCallAncestryLink[] Markers { get; set; } = [];
}
internal static class CompilerShadowCallAncestryCodec
{
    internal static CompilerShadowCallAncestryArtifact Encode(CompilerTotalCallablePreparation body, string root, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var indices = Instructions(body, cancellationToken);
        var nodes = new List<CompilerShadowCallAncestryNode>();
        var links = new List<CompilerShadowCallAncestryLink>();
        var interned = new Dictionary<CompilerShadowCallAncestryNode, int>();
        Require(body.CallPreconditions.Length <= 4096);
        foreach (var marker in body.CallPreconditions.OrderBy(marker => indices[marker.Instruction]))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(marker.Ancestry.Length is > 0 and <= 256);
            var parent = -1;
            foreach (var hop in marker.Ancestry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = new CompilerShadowCallAncestryNode(parent, hop.CallerIdentity, hop.CalleeIdentity,
                    hop.Site.Document, hop.Site.Start, hop.Site.Length);
                if (!interned.TryGetValue(node, out var index))
                {
                    Require(nodes.Count < 4096);
                    index = nodes.Count;
                    nodes.Add(node);
                    interned.Add(node, index);
                }
                parent = index;
            }
            links.Add(new(indices[marker.Instruction], parent));
        }
        var wire = new CompilerShadowCallAncestryArtifact { RootIdentity = root, Nodes = [.. nodes], Markers = [.. links] };
        Decode(wire, body, root, cancellationToken);
        return wire;
    }
    internal static ImmutableArray<ImmutableArray<CompilerShadowCallAncestryNode>> Decode(CompilerShadowCallAncestryArtifact wire,
        CompilerTotalCallablePreparation body, string root, CancellationToken cancellationToken = default)
    {
        wire = ArgumentNullGuard.NotNull(wire, nameof(wire));
        if (wire.Nodes == null || wire.Markers == null)
        { throw new InvalidDataException("Missing ancestry arrays."); }
        Require(wire.RootIdentity == root && ValidText(root) && wire.Nodes.Length <= 4096);
        cancellationToken.ThrowIfCancellationRequested();
        var indices = Instructions(body, cancellationToken);
        var markers = body.CallPreconditions.OrderBy(marker => indices[marker.Instruction]).ToArray();
        Require(wire.Markers.Length == markers.Length && wire.Markers.Length <= 4096);
        var depths = new int[wire.Nodes.Length];
        var unique = new HashSet<CompilerShadowCallAncestryNode>();
        for (var index = 0; index < wire.Nodes.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = wire.Nodes[index] ?? throw new InvalidDataException("Missing ancestry node.");
            Require(node.Parent >= -1 && node.Parent < index && ValidText(node.Caller) &&
                ValidText(node.Callee) && ValidText(node.Document) && node.Start >= 0 && node.Length > 0 &&
                node.Start <= int.MaxValue - node.Length && unique.Add(node));
            Require(node.Parent == -1 ? node.Caller == root : node.Caller == wire.Nodes[node.Parent].Callee);
            depths[index] = node.Parent == -1 ? 1 : depths[node.Parent] + 1;
            Require(depths[index] <= 256);
        }
        var seen = new HashSet<int>();
        var next = 0;
        var result = ImmutableArray.CreateBuilder<ImmutableArray<CompilerShadowCallAncestryNode>>(markers.Length);
        var instructions = body.Program.Blocks.SelectMany(block => block.Instructions).ToDictionary(instruction => { cancellationToken.ThrowIfCancellationRequested(); return instruction.Id; });
        for (var ordinal = 0; ordinal < markers.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var link = wire.Markers[ordinal] ?? throw new InvalidDataException("Missing ancestry link.");
            var marker = markers[ordinal];
            Require(link.InstructionIndex == indices[marker.Instruction] && link.Node >= 0 && link.Node < wire.Nodes.Length);
            var end = wire.Nodes[link.Node];
            var span = body.Program.Factory.GetOperationInfo(instructions[marker.Instruction].Operation).SourceSpan;
            Require(end.Callee == marker.CalleeIdentity && span != null && end.Document == span.Document && end.Start == span.Start && end.Length == span.Length);
            var path = new List<int>(depths[link.Node]);
            for (var current = link.Node; current >= 0; current = wire.Nodes[current].Parent)
            { cancellationToken.ThrowIfCancellationRequested(); path.Add(current); }
            path.Reverse();
            foreach (var index in path)
            { cancellationToken.ThrowIfCancellationRequested(); if (seen.Add(index)) { Require(index == next++); } }
            result.Add([.. path.Select(index => wire.Nodes[index])]);
        }
        Require(seen.Count == wire.Nodes.Length);
        return result.MoveToImmutable();
    }
    private static Dictionary<IrInstructionId, int> Instructions(CompilerTotalCallablePreparation body, CancellationToken cancellationToken)
    {
        var result = new Dictionary<IrInstructionId, int>();
        foreach (var block in body.Program.Blocks.OrderBy(block => block.Id.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var instruction in block.Instructions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Require(result.Count < 65_536 && !result.ContainsKey(instruction.Id));
                result.Add(instruction.Id, result.Count);
            }
        }
        return result;
    }
    private static bool ValidText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096)
        { return false; }
        try
        { return new UTF8Encoding(false, true).GetByteCount(text) <= 4096; }
        catch (EncoderFallbackException) { return false; }
    }
    private static void Require(bool condition)
    { if (!condition) { throw new InvalidDataException("Invalid bounded shadow call ancestry."); } }
}
internal sealed record CompilerShadowCallAncestryMatch(int Expected, int Matched, ImmutableArray<string> Failures);
internal static class CompilerShadowCallAncestryQualifier
{
    internal static CompilerShadowCallAncestryMatch Match(CSharpCompilation compilation, CompilerTotalCallablePreparation body,
        string rootIdentity, CompilerShadowCallAncestryArtifact wire, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (body.CallPreconditions.Length > 4096)
        { throw new InvalidDataException("Source ancestry marker count exceeded."); }
        foreach (var marker in body.CallPreconditions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (marker.MetadataClause != null || body.Program.Factory.GetOperationInfo(marker.ClauseSite).SourceSpan is not { Length: > 0 })
            { throw new InvalidDataException("Source ancestry requires a physically owned source clause."); }
        }
        var decoded = CompilerShadowCallAncestryCodec.Decode(wire, body, rootIdentity, cancellationToken);
        var captured = CompilerCompilationCapture.CaptureTrees(compilation, cancellationToken);
        var documents = compilation.SyntaxTrees.Select((tree, index) =>
            (tree, captured[index].Path))
            .ToDictionary(row => row.tree, row => row.Path);
        var inventory = new ClaimManifestBuilder(compilation, cancellationToken: cancellationToken).BuildPotentialCallShadow();
        if (!inventory.Gaps.IsEmpty || inventory.Owners.Length > 4096)
        { throw new InvalidDataException("Incomplete bounded source census."); }
        var owners = new Dictionary<IMethodSymbol, CompilerPotentialCallOwner>(SymbolEqualityComparer.Default);
        foreach (var owner in inventory.Owners)
        { cancellationToken.ThrowIfCancellationRequested(); owners.Add(owner.Method, owner); }
        var remainingSourceCalls = 4096;
        var callsByOwner = new Dictionary<IMethodSymbol, ImmutableArray<PotentialRequiresCallSite>>(SymbolEqualityComparer.Default);
        foreach (var owner in inventory.Owners)
        {
            var calls = new RequiresCallSiteDiscovery(owner.Method, owner.Declaration, owner.SemanticModel, cancellationToken)
                .GetPotentialCalls(static _ => true, out var complete);
            if (!complete || !calls.HasValue)
            { throw new InvalidDataException("Incomplete independent source calls."); }
            remainingSourceCalls -= calls.Value.Length;
            if (remainingSourceCalls < 0)
            { throw new InvalidDataException("Source call census size exceeded."); }
            callsByOwner.Add(owner.Method, calls.Value);
        }
        var contractApi = ContractClauseSymbols.TryCreate(compilation);
        var bindings = new Dictionary<IMethodSymbol, ImmutableArray<BoundTotalContractClause>>(SymbolEqualityComparer.Default);
        var root = inventory.Owners.Single(owner => CompilerIdentityBridge.CreateSymbolDisplay(owner.Method) == rootIdentity);
        var expected = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(CompilerPotentialCallOwner Owner, ImmutableArray<CompilerShadowCallAncestryNode> Path, ImmutableHashSet<IMethodSymbol> Active)>();
        pending.Push((root, [], ImmutableHashSet.Create<IMethodSymbol>(SymbolEqualityComparer.Default, root.Method)));
        var remaining = 4096;
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = pending.Pop();
            if (!frame.Owner.DiscoveryComplete || frame.Path.Length > 256)
            { throw new InvalidDataException("Incomplete bounded source ancestry."); }
            foreach (var call in callsByOwner[frame.Owner.Method].Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { throw new InvalidDataException("Source census budget exceeded."); }
                if (contractApi?.GetClauseKind(call.Target) == BoundContractKind.Requires)
                { continue; }
                if (frame.Path.Length >= 256 || call.Origin is not Microsoft.CodeAnalysis.Operations.IInvocationOperation ||
                    !owners.TryGetValue(call.Target, out var callee) || frame.Active.Contains(call.Target) ||
                    !documents.TryGetValue(call.Syntax.SyntaxTree, out var document))
                { throw new InvalidDataException("Unsupported bounded source ancestry."); }
                var hop = new CompilerShadowCallAncestryNode(-1, CompilerIdentityBridge.CreateSymbolDisplay(frame.Owner.Method),
                    CompilerIdentityBridge.CreateSymbolDisplay(call.Target), document, call.Syntax.SpanStart, call.Syntax.Span.Length);
                var path = frame.Path.Add(hop);
                if (!bindings.TryGetValue(call.Target, out var clauses))
                {
                    var factory = new IrFactory(IrExecutionSemantics.Total);
                    var binding = new ContractBinder(compilation, factory).BindTotalRequires(new TotalLoweringContext(factory, call.Target));
                    if (!binding.IsSuccess)
                    { throw new InvalidDataException("Unsupported source clause census."); }
                    clauses = binding.Clauses;
                    bindings.Add(call.Target, clauses);
                }
                var ordinal = 0;
                foreach (var clause in clauses.Where(clause => clause.Kind == BoundContractKind.Requires))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (expected.Count >= 4096 || clause.SourceSyntax is not { } syntax ||
                        !documents.TryGetValue(syntax.SyntaxTree, out var clauseDocument))
                    { throw new InvalidDataException("Incomplete source clause ownership."); }
                    expected.Add(Key(path, ordinal++, clauseDocument, syntax.Span.Start, syntax.Span.Length));
                }
                pending.Push((callee, path, frame.Active.Add(call.Target)));
            }
        }
        var instructions = body.Program.Blocks.OrderBy(block => block.Id.Value).SelectMany(block => block.Instructions)
            .Select((instruction, index) => { cancellationToken.ThrowIfCancellationRequested(); return (instruction.Id, index); }).ToDictionary(row => row.Id, row => row.index);
        var native = body.CallPreconditions.OrderBy(marker => instructions[marker.Instruction]).ToArray();
        var actual = new HashSet<string>(StringComparer.Ordinal);
        var failures = ImmutableArray.CreateBuilder<string>();
        for (var ordinal = 0; ordinal < native.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clause = body.Program.Factory.GetOperationInfo(native[ordinal].ClauseSite).SourceSpan ??
                throw new InvalidDataException("Source clause has no physical span.");
            var key = Key(decoded[ordinal], native[ordinal].ClauseOrdinal, clause.Document, clause.Start, clause.Length);
            if (!actual.Add(key))
            { failures.Add("DuplicateOriginalSourceAncestry"); }
            if (!expected.Contains(key))
            { failures.Add("UnexpectedSourceAncestry"); }
        }
        foreach (var missing in expected.Except(actual))
        { cancellationToken.ThrowIfCancellationRequested(); failures.Add("MissingSourceAncestry:" + missing); }
        return new(expected.Count, actual.Count(expected.Contains), failures.ToImmutable());
    }
    // Qualification only: the compilation must already be an authenticated compiler-owned input.
    // No caller-provided expected graph, transport digest or alternate options are accepted.
    internal static CompilerShadowCallAncestryMatch MatchCompilerRecomputed(CSharpCompilation compilation,
        CompilerTotalCallablePreparation body, string rootIdentity, CompilerShadowCallAncestryArtifact wire,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trees = CompilerCompilationCapture.CaptureTrees(compilation, cancellationToken);
        var references = CompilerCompilationCapture.CaptureReferences(compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, cancellationToken);
        var sourceInputs = JsonSerializer.Serialize(new { Trees = trees, References = references });
        var inventory = new ClaimManifestBuilder(compilation, cancellationToken: cancellationToken).BuildPotentialCallShadow();
        var owner = inventory.Owners.Single(item => CompilerIdentityBridge.CreateSymbolDisplay(item.Method) == rootIdentity);
        if (body.CallableId != owner.CallableId)
        { throw new InvalidDataException("Source owner differs from prepared body."); }
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            trees, references, CompilerSpecificationPackProvider.ResolveConfiguration([]), cancellationToken);
        var expected = batch.Callers.SingleOrDefault(caller => caller.OwnerId == owner.CallableId);
        cancellationToken.ThrowIfCancellationRequested();
        if (expected == null || JsonSerializer.Serialize(CompilerTotalCallableArtifactCodec.Encode(expected.Body)) !=
            JsonSerializer.Serialize(CompilerTotalCallableArtifactCodec.Encode(body)))
        { throw new InvalidDataException("Source recomputation differs from canonical prepared graph."); }
        var currentTrees = CompilerCompilationCapture.CaptureTrees(compilation, cancellationToken);
        var currentReferences = CompilerCompilationCapture.CaptureReferences(compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, cancellationToken);
        if (sourceInputs != JsonSerializer.Serialize(new { Trees = currentTrees, References = currentReferences }))
        { throw new InvalidDataException("Compiler source/reference inputs changed during qualification."); }
        return Match(compilation, body, rootIdentity, wire, cancellationToken);
    }
    private static string Key(ImmutableArray<CompilerShadowCallAncestryNode> path, int ordinal, string document, int start, int length)
    {
        return JsonSerializer.Serialize(new
        {
            Hops = path.Select(hop => new { hop.Caller, hop.Callee, hop.Document, hop.Start, hop.Length }),
            ClauseOrdinal = ordinal,
            ClauseDocument = document,
            ClauseStart = start,
            ClauseLength = length
        });
    }
}
