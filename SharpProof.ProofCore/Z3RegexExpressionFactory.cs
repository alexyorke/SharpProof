namespace SharpProof.ProofCore.Smt;

internal sealed class Z3RegexExpressionFactory {
    private readonly Context _context;
    private ReExpr? _anyCharacter;

    internal Z3RegexExpressionFactory(Context context) {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    internal ReExpr AnyString() => _context.MkStar(AnyCharacter());

    internal ReExpr OptionalFinalNewline() => _context.MkOption(Literal("\n"));

    internal ReExpr AnyCharacter() {
        if (_anyCharacter != null) return _anyCharacter;

        const string marker = "__sharpproof_allchar";
        var regexSort = _context.MkReSort(_context.StringSort);
        var declaration = _context.MkConstDecl(marker, regexSort);
        var assertions = _context.ParseSMTLIB2String(
            "(assert (= " + marker + " re.allchar))",
            Array.Empty<Symbol>(),
            Array.Empty<Sort>(),
            new[] { _context.MkSymbol(marker) },
            new[] { declaration });
        if (assertions.Length != 1 ||
            assertions[0].Args.Length != 2 ||
            assertions[0].Args[1] is not ReExpr allCharacter)
            throw new InvalidOperationException("Unable to create the Z3 all-character regular expression.");

        _anyCharacter = allCharacter;
        return allCharacter;
    }

    internal ReExpr CharacterRange(char start, char end) {
        if (start > end) throw new ArgumentOutOfRangeException(nameof(start));

        if (start == char.MinValue) {
            if (end == char.MaxValue) return AnyCharacter();

            return _context.MkDiff(
                AnyCharacter(),
                CharacterRange((char)(end + 1), char.MaxValue));
        }

        return _context.MkRange(
            _context.MkString(start.ToString()),
            _context.MkString(end.ToString()));
    }

    internal ReExpr Dot(bool singleline) =>
        singleline
            ? AnyCharacter()
            : _context.MkDiff(AnyCharacter(), Literal("\n"));

    internal ReExpr ExactRepeat(ReExpr regex, uint count) =>
        count == 0 ? Literal(string.Empty) : _context.MkLoop(regex, count, count);

    internal ReExpr Concat(ReExpr left, ReExpr right) => _context.MkConcat(left, right);

    internal ReExpr Concat(params ReExpr[] expressions) => _context.MkConcat(expressions);

    internal ReExpr Literal(string value) => _context.MkToRe(_context.MkString(value));
}
