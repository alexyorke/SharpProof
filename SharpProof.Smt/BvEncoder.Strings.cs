using System.Text;

namespace SharpProof.Smt;

// String content is a sequence of UTF-16 code units, read through `text` from
// a string reference. Content is encoded only once a query compares string
// content; until then strings stay plain references.
internal sealed partial class BvEncoder
{
    private Sort? _codeUnitSort;
    private SeqSort? _textSort;
    private FuncDecl? _text;
    private readonly HashSet<uint> _textLinked = [];
    private readonly List<(Expr Value, Expr Left, Expr Right)> _concatenations = [];

    private Expr EncodeStringEquals(Expr left, Expr right, SmtQueryResourceMeter meter)
    {
        var leftNull = owner.Own(context.MkEq(left, NullReference));
        var rightNull = owner.Own(context.MkEq(right, NullReference));
        var sameText = owner.Own(context.MkEq(Text(left, meter), Text(right, meter)));
        return owner.Own(context.MkITE(leftNull, rightNull,
            owner.Own(context.MkAnd(owner.Own(context.MkNot(rightNull)), sameText))));
    }

    // The content of a non-null string; its length is the reference's length.
    private SeqExpr Text(Expr value, SmtQueryResourceMeter meter)
    {
        meter.Consume();
        EnsureText(meter);
        var text = (SeqExpr)owner.Own(context.MkApp(_text!, value));
        if (_textLinked.Add(value.Id))
        {
            var length = owner.Own(context.MkBV2Int((BitVecExpr)EncodeLength(value, meter), false));
            ReferenceFacts.Add(owner.Own(context.MkOr(owner.Own(context.MkEq(value, NullReference)),
                owner.Own(context.MkEq(length, owner.Own(context.MkLength(text)))))));
        }
        return text;
    }

    private void EnsureText(SmtQueryResourceMeter meter)
    {
        if (_text != null)
        { return; }
        _codeUnitSort = context.MkBitVecSort(16);
        _textSort = context.MkSeqSort(_codeUnitSort);
        _text = context.MkFuncDecl("text", ReferenceSort, _textSort);
        foreach (var literal in _stringLiterals)
        { AddLiteralText(literal.Value, factory.GetString(literal.Key), meter); }
        foreach (var concatenation in _concatenations)
        { AddConcatenationText(concatenation, meter); }
    }

    private void AddLiteralText(Expr value, string content, SmtQueryResourceMeter meter)
    {
        meter.Consume(content.Length + 1L);
        ReferenceFacts.Add(owner.Own(context.MkEq(Text(value, meter), Literal(content))));
    }

    private void RegisterConcatenation(Expr value, Expr left, Expr right, SmtQueryResourceMeter meter)
    {
        var concatenation = (value, left, right);
        _concatenations.Add(concatenation);
        if (_text != null)
        { AddConcatenationText(concatenation, meter); }
    }

    // A null operand concatenates as the empty string.
    private void AddConcatenationText((Expr Value, Expr Left, Expr Right) concatenation, SmtQueryResourceMeter meter)
    {
        SeqExpr Operand(Expr operand)
        {
            return (SeqExpr)owner.Own(context.MkITE(owner.Own(context.MkEq(operand, NullReference)),
                owner.Own(context.MkEmptySeq(_textSort!)), Text(operand, meter)));
        }
        ReferenceFacts.Add(owner.Own(context.MkEq(Text(concatenation.Value, meter),
            owner.Own(context.MkConcat(Operand(concatenation.Left), Operand(concatenation.Right))))));
    }

    private SeqExpr Literal(string content)
    {
        if (content.Length == 0)
        { return (SeqExpr)owner.Own(context.MkEmptySeq(_textSort!)); }
        var units = content.Select(unit => (SeqExpr)owner.Own(context.MkUnit(owner.Own(context.MkBV(unit, 16))))).ToArray();
        return units.Length == 1 ? units[0] : (SeqExpr)owner.Own(context.MkConcat(units));
    }

    // The model's content for a string, or null when no query read content.
    // A reference whose content was never linked to its length may decode to
    // content of another length; the caller then keeps the length.
    private string? DecodeText(Expr value, Model model, SmtQueryResourceMeter meter)
    {
        if (_text == null)
        { return null; }
        using var evaluated = model.Evaluate(owner.Own(context.MkApp(_text, value)), true);
        var units = new StringBuilder();
        void Append(Expr sequence)
        {
            meter.Consume();
            switch (sequence.FuncDecl.DeclKind)
            {
                case Z3_decl_kind.Z3_OP_SEQ_EMPTY:
                    return;
                case Z3_decl_kind.Z3_OP_SEQ_UNIT when sequence.Args[0] is BitVecNum unit:
                    units.Append((char)unit.UInt);
                    return;
                case Z3_decl_kind.Z3_OP_SEQ_CONCAT:
                    foreach (var part in sequence.Args)
                    { Append(part); }
                    return;
                default:
                    throw new UnsupportedIrEncodingException();
            }
        }
        Append(evaluated);
        return units.ToString();
    }

    private void DisposeStrings()
    {
        _text?.Dispose();
        _textSort?.Dispose();
        _codeUnitSort?.Dispose();
    }
}
