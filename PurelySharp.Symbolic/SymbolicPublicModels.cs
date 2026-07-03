using System;
using System.Collections.Generic;
using PurelySharp.Symbolic.Ir;

namespace PurelySharp.Symbolic
{
    public enum SymbolicProofBackend
    {
        None,
        Syntactic,
        Smt,
    }

    public enum SymbolicProofStatus
    {
        Unknown,
        Reachable,
        Unreachable,
        ProvenTrue,
        ProvenFalse,
    }

    public enum SymbolicUnknownReason
    {
        None,
        UnsupportedIrEncoding,
        SmtDisabled,
        SmtUnavailable,
        Timeout,
        MethodBudgetExceeded,
        PathConditionBudgetExceeded,
        ExpressionBudgetExceeded,
        CancellationRequested,
        EncodingFailure,
        Unknown,
    }

    public sealed class SymbolicBudgetInfo
    {
        public SymbolicBudgetInfo(
            int maxPathConditions,
            int maxExpressionNodes,
            int timeoutMilliseconds,
            int methodBudgetMilliseconds,
            int executedQueryCount,
            int cacheEntryCount)
        {
            MaxPathConditions = maxPathConditions;
            MaxExpressionNodes = maxExpressionNodes;
            TimeoutMilliseconds = timeoutMilliseconds;
            MethodBudgetMilliseconds = methodBudgetMilliseconds;
            ExecutedQueryCount = executedQueryCount;
            CacheEntryCount = cacheEntryCount;
        }

        public int MaxPathConditions { get; }

        public int MaxExpressionNodes { get; }

        public int TimeoutMilliseconds { get; }

        public int MethodBudgetMilliseconds { get; }

        public int ExecutedQueryCount { get; }

        public int CacheEntryCount { get; }
    }

    public sealed class SymbolicProofInfo
    {
        public SymbolicProofInfo(
            SymbolicProofStatus status,
            SymbolicProofBackend backend,
            SymbolicUnknownReason unknownReason,
            string reason,
            bool cacheHit,
            SymbolicBudgetInfo? budget)
        {
            Status = status;
            Backend = backend;
            UnknownReason = unknownReason;
            Reason = reason ?? string.Empty;
            CacheHit = cacheHit;
            Budget = budget;
        }

        public SymbolicProofStatus Status { get; }

        public SymbolicProofBackend Backend { get; }

        public SymbolicUnknownReason UnknownReason { get; }

        public string Reason { get; }

        public bool CacheHit { get; }

        public SymbolicBudgetInfo? Budget { get; }
    }

    public sealed class SymbolicFactInfo
    {
        public SymbolicFactInfo(
            string kind,
            string text,
            string provenance,
            string confidence,
            int sourceSpanStart,
            int sourceSpanLength,
            string? symbolKey = null,
            string? evidenceKey = null)
        {
            Kind = kind ?? string.Empty;
            Text = text ?? string.Empty;
            Provenance = provenance ?? string.Empty;
            Confidence = confidence ?? string.Empty;
            SourceSpanStart = sourceSpanStart;
            SourceSpanLength = sourceSpanLength;
            SymbolKey = symbolKey;
            EvidenceKey = evidenceKey;
        }

        public string Kind { get; }

        public string Text { get; }

        public string Provenance { get; }

        public string Confidence { get; }

        public int SourceSpanStart { get; }

        public int SourceSpanLength { get; }

        public string? SymbolKey { get; }

        public string? EvidenceKey { get; }

        internal static SymbolicFactInfo FromFact(SymbolicFact fact)
        {
            if (fact == null)
            {
                throw new ArgumentNullException(nameof(fact));
            }

            var atomKind = fact.Atom.GetType().Name;
            return new SymbolicFactInfo(
                atomKind,
                FormatFactText(fact),
                fact.Provenance,
                fact.Confidence.ToString(),
                fact.SourceSpan.Start,
                fact.SourceSpan.Length,
                fact.Symbol?.ToDisplayString(),
                fact.EvidenceKey);
        }

        private static string FormatFactText(SymbolicFact fact)
        {
            var text = fact.Atom.ToString() ?? string.Empty;
            return fact.Polarity ? text : "!(" + text + ")";
        }
    }

    public sealed class SymbolicInvariantInfo
    {
        public SymbolicInvariantInfo(
            string mergedText,
            IReadOnlyList<SymbolicFactInfo>? facts = null,
            IReadOnlyList<SymbolicProofInfo>? proofs = null)
        {
            MergedText = mergedText ?? string.Empty;
            Facts = facts ?? Array.Empty<SymbolicFactInfo>();
            Proofs = proofs ?? Array.Empty<SymbolicProofInfo>();
        }

        public string MergedText { get; }

        public IReadOnlyList<SymbolicFactInfo> Facts { get; }

        public IReadOnlyList<SymbolicProofInfo> Proofs { get; }
    }
}
