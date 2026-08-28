using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Effects;

/// <summary>
/// Enumerates the semantic phases of a deconstruction in language order.
/// </summary>
/// <remarks>
/// Roslyn exposes deconstruction calls and user-defined conversions through
/// <see cref="DeconstructionInfo"/> rather than as ordinary child operations.
/// Keeping their traversal here prevents completion and effect analysis from
/// disagreeing about which phase was reached before a failure.
/// </remarks>
internal readonly record struct DeconstructionPhase(
    IMethodSymbol? Method,
    IMethodSymbol? Conversion,
    bool IsRootMethod)
{
}

internal static class DeconstructionPhaseWalker
{
    internal static IEnumerable<DeconstructionPhase> Enumerate(
        DeconstructionInfo info)
    {
        var pending = new Stack<(DeconstructionInfo Info, bool IsRoot, bool Exit)>();
        pending.Push((info, true, false));
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (current.Exit)
            {
                if (current.Info.Conversion is
                    { MethodSymbol: { } conversion })
                {
                    yield return new(
                        Method: null,
                        Conversion: conversion,
                        IsRootMethod: false);
                }

                continue;
            }

            if (current.Info.Method is { } method)
            {
                yield return new(
                    Method: method,
                    Conversion: null,
                    IsRootMethod: current.IsRoot);
            }

            pending.Push((current.Info, current.IsRoot, true));
            var nested = current.Info.Nested.IsDefault
                ? ImmutableArray<DeconstructionInfo>.Empty
                : current.Info.Nested;
            for (var index = nested.Length - 1; index >= 0; index--)
            {
                pending.Push((nested[index], false, false));
            }
        }
    }
}
