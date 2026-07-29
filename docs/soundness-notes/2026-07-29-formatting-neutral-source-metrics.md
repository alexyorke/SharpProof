# Formatting-neutral source metrics - 2026-07-29

## Decision

SharpProof no longer uses physical-line, nonblank-line, or source-span limits as
release gates. Those measurements made ordinary brace placement, line wrapping,
comments, and blank lines appear to be architectural regressions. They also
made collapsing code onto fewer lines appear to be decomposition.

The repository now follows ordinary `dotnet format` conventions:

- opening braces use normal C# new lines;
- control-flow statements require braces;
- methods, constructors, local functions, and operators use block bodies;
- properties, accessors, simple lambdas, and genuinely short members may remain
  expression-bodied;
- generated C# preserves its generator-authored wrapping and is normalized to
  the same brace layout.

## Replacement gates

The acceptance contract retains exact, non-overlapping trusted-computing-base
path ownership. Growth is measured with Roslyn structure that excludes trivia:

- expression-node counts approximate executable source structure without
  counting optional block braces;
- decision points track branching complexity;
- member counts track surface growth;
- selected algorithm members have their own expression and decision limits.

Physical, nonblank, token, and total syntax-node counts remain available as
informational measurements. They do not affect acceptance.

The former historical "10% smaller" nonblank-line check was removed. It did not
demonstrate a 10% semantic or architectural reduction and could be satisfied by
formatting compression. Coordinator layers now have explicit current
expression-node and decision-point ratchets.

## Mechanical enforcement

`scripts/Format-CSharp.ps1` invokes the SDK's `dotnet format` commands for
handwritten and checked-in generated C#. CI verifies the same result.
`scripts/Test-ProductionCSharpComplexity.ps1` and the architecture tests enforce
the formatting-neutral metrics. Generator verification continues to require
deterministic LF, UTF-8, and byte-for-byte current output.
