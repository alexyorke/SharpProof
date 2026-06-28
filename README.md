# PurelySharp Analyzer

A Roslyn analyzer designed to help enforce method purity in C# projects.

**Note:** PurelySharp **v1** ships a full diagnostic surface (`PS0002`-`PS0011`), code fixes, editor/MSBuild configuration, CFG-based method-body analysis, and the feature matrix below. It is **conservative by design**: it may report `PS0002` when it cannot prove purity. **Perfect** purity verification for arbitrary C# and the whole BCL is **not possible** (undecidable in the general case); this tool implements practical, bounded rules and catalogs instead.

## Goal

The primary goal of PurelySharp is to provide developers with tools to identify methods intended to be functionally pure and flag potential issues related to that intent.

## Current Status & Features

The analyzer provides the following checks:

1.  **`[EnforcePure]` / `[Pure]` Attributes:** Use either `PurelySharp.Attributes.EnforcePureAttribute` or `PurelySharp.Attributes.PureAttribute` to mark methods intended to be pure. The analyzer treats `[Pure]` as an interchangeable shorthand for `[EnforcePure]`. Applying both at once yields `PS0005` (conflict).
2.  **PS0002: Purity Not Verified:** If a method is marked with a purity attribute (`[EnforcePure]` or `[Pure]`) and the engine cannot determine it to be pure based on the current rules (see below), it reports diagnostic `PS0002`.
3.  **PS0003: Misplaced Attribute:** If a purity attribute (`[EnforcePure]` or `[Pure]`) is applied to any declaration _other than_ a method (e.g., class, struct, field, property, event, parameter), the analyzer reports diagnostic `PS0003`.
4.  **PS0004: Missing Attribute:** If a method is _not_ marked with a purity attribute but the analysis engine determines it _is_ likely pure based on the currently implemented rules, it reports diagnostic `PS0004` as a suggestion.
5.  **Basic Purity Analysis:** A limited analysis engine (`PurityAnalysisEngine`) attempts to verify the purity of methods. It checks for:
    - Known impure operations (e.g., some I/O, `DateTime.Now`, field assignments).
    - Purity of invoked methods (recursive check with cycle detection).
    - Purity of expressions (constants, parameters, `static readonly` fields, basic operators, etc.).
    - Purity of basic statements (local declarations, return, simple expression statements).
6.  **Regression coverage:** The in-repo analyzer suite runs on .NET 8 via `PurelySharp.Test`.

**Inherent limitations (not "missing features"):**

- **Whole-program / whole-BCL formal proof** of purity is out of scope; the analyzer uses explicit impure/pure catalogs, symbolic method analysis, and conservative defaults.
- **CFG and dataflow:** method bodies are analyzed with Roslyn `ControlFlowGraph` rooted at the method (or constructor / local function) declaration, with flow-capture handling for lowered conditionals, explicit condition-branch tracking, and conservative constant-branch pruning. Remaining edge cases may still produce false positives (`PS0002`) or false negatives; the engine stays conservative when in doubt.
- **Target frameworks:** every TFM is not CI-matrixed; the analyzer is `netstandard2.0`, with a `net472` smoke project (`PurelySharp.Smoke.Net472`) and the main test suite on .NET 8 (see **Cross-Framework and Language Version Support** below).

**Recently implemented (see codebase):**

- **Code fixes** (`PurelySharp.CodeFixes`) for `PS0002`-`PS0008` (remove/add attributes, resolve conflicts). The demo and tests reference the code-fix project where applicable.
- **Configuration** via `.editorconfig` / MSBuild `global analyzerconfig`: `purelysharp_known_impure_methods`, `purelysharp_known_pure_methods`, `purelysharp_known_impure_namespaces`, `purelysharp_known_impure_types`, `purelysharp_purity_profile` (`balanced`, `strict`, `pragmatic`; default `balanced`), `purelysharp_enable_debug_logging`, `purelysharp_suggest_missing_enforce_pure` (`true`/`false`, default `true`), and `purelysharp_suggest_missing_enforce_pure_scope` (`all`, `public`, `internal`, `off`, default `all`) to tune `PS0004` suggestions. The `strict` profile currently tightens mutable `this` field reads; `balanced` preserves the default adoption behavior. Optional PS0004 filters include `purelysharp_suggest_missing_enforce_pure_exclude_generated`, `purelysharp_suggest_missing_enforce_pure_exclude_tests`, `purelysharp_suggest_missing_enforce_pure_min_complexity`, and `purelysharp_suggest_missing_enforce_pure_namespace_filters`; these PS0004 controls honor per-file `.editorconfig` sections. Adoption baselines are supported through an additional file named `PurelySharp.Baseline.json`, matching diagnostics by ID, symbol documentation ID, and relative path. Boundary attributes `[PureExternal]` and `[Impure]` let teams explicitly trust or reject boundary methods, properties, constructors, or whole assemblies without broad catalog changes.
- **Exception-flow reporting** is available as opt-in `PS0010` and `PS0011` diagnostics. Enable method-level exception summaries with `purelysharp_report_exceptions = true` and enable call-site uncaught-exception warnings with `purelysharp_checked_exceptions = true`. The current implementation reports method-level uncaught exception summaries, warns on uncaught call sites, reports source-level uncaught `throw` / throw-expression exception types, infers typed `throw;` rethrows from enclosing catch clauses, detects definite integer/decimal divide-by-zero and modulo-by-zero expressions, detects branch-implied zero divisors including simple short-circuit and guard-if cases, detects definite null dereferences on literal/default-null receivers and branch-implied null receivers including `is not null` else branches, propagates summaries recursively through same-compilation source method calls, constructors, and property getters with cycle detection, suppresses simple caught throws at throw, call, object-creation, property-read, definite divide-by-zero, and definite null-dereference sites, preserves multi-hop source-callee evidence chains, and keeps nested lambdas/local functions separate. Built-in metadata summaries are regenerated into analyzer `obj` during build/test and loaded only from embedded resources in that build; users can still opt in explicit `PurelySharp.EffectSummary.json` additional files.
- **Bottom-up SDK purity calibration** still has a report-first path in `Tools/PurelySharp.EffectSummary`, but the repo no longer checks in generated effect-summary JSON artifacts or a reviewed runtime artifact spec. Ad hoc outputs stay local.
- **`[AllowSynchronization]`** is supported alongside `[EnforcePure]`/`[Pure]` (`PS0006`-`PS0008`).

## How It Works

The analyzer (`PurelySharp.Analyzer`) integrates with the C# compilation process:

1.  It identifies method declarations and other symbols.
2.  It checks for the presence and location of the purity attributes (`[EnforcePure]` or `[Pure]`).
3.  If either purity attribute is misplaced (not on a method), it reports `PS0003`.
4.  If a purity attribute is on a method with implementation, it invokes the `PurityAnalysisEngine` to check its body.
    - If the engine determines the method is _not_ pure (based on current rules), it reports `PS0002`.
5.  If no purity attribute is on a method with implementation, it invokes the `PurityAnalysisEngine`.
    - If the engine determines the method _is_ pure (based on current rules), it reports `PS0004`.
6.  **The internal analysis (`PurityAnalysisEngine`) does not cover every C# feature and does not guarantee completeness; unknown or unhandled operations are treated conservatively.**

## Installation

For local development, build packages with `build-nuget.ps1` and add a local NuGet source that points at `artifacts\nuget`. When packages are published, use the same package IDs from your NuGet source.

1.  **Analyzer package:** Add the `PurelySharp` NuGet package to projects that should run build-time analysis. The analyzer package also includes the attributes assembly for NuGet consumption.
    ```powershell
    dotnet add package PurelySharp --version 0.0.4
    ```
2.  **Attributes-only package:** Add `PurelySharp.Attributes` only when you need the attribute definitions without installing the analyzer package.
    ```powershell
    dotnet add package PurelySharp.Attributes --version 0.0.4
    ```
3.  **Visual Studio extension:** Install the generated `PurelySharp.Vsix` extension for real-time feedback in Visual Studio.

## Local build and install (VSIX + NuGet)

Use the provided script to produce a VSIX for Visual Studio plus local NuGet packages for `PurelySharp` and `PurelySharp.Attributes`.

1. Prerequisites
   - Visual Studio 2022 with the "Visual Studio extension development" workload (VSSDK)
   - .NET SDK 9.0.315, selected by the repo `global.json`

2. Build artifacts
   ```powershell
   # From the repo root
   powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
   # Optional: Debug build
   powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Debug
   ```
   The script stops on any failure and prints where artifacts were written. Typical locations:
   - VSIX: `PurelySharp.Vsix\bin\Release\net472\PurelySharp.Vsix.vsix`
   - NuGet: `artifacts\nuget\PurelySharp.<version>.nupkg`
   - NuGet: `artifacts\nuget\PurelySharp.Attributes.<version>.nupkg`

3. Install the VSIX into Visual Studio
   - Close Visual Studio.
   - Double-click the generated `.vsix` and complete the installer.
   - Reopen Visual Studio.

4. Use the local NuGet packages
   - In Visual Studio: Tools -> NuGet Package Manager -> Package Manager Settings -> Package Sources
   - Add a new local source pointing to `artifacts\nuget`
   - In your test project: Manage NuGet Packages -> select the local source -> install `PurelySharp` and `PurelySharp.Attributes` as needed

5. Updating/uninstalling
   - Re-run the build script to produce a new VSIX/NuGet; reinstall the VSIX to update
   - Manage installed extensions via Extensions -> Manage Extensions in Visual Studio

## Usage

1.  Reference the `PurelySharp.Attributes` project (or package, once available).
2.  Add either `[EnforcePure]` or `[Pure]` (from `PurelySharp.Attributes`) to methods you intend to be functionally pure.
3.  Apply the attribute incorrectly to see PS0003.

    ```csharp
    using PurelySharp.Attributes;

    [Pure] // PS0003: Misplaced attribute on class
    public class Calculator
    {
        [Pure]
        public int Add(int a, int b)
        {
            // Simple arithmetic on parameters is treated as pure - no PS0002 here.
            return a + b;
        }

        [Pure]
        public int GetConstant(); // No implementation, PS0002 NOT reported.

        [Pure] // PS0003: Misplaced attribute on field
        private int _counter = 0;
    }
    ```

4.  Observe the `PS0003` diagnostic during build or in the IDE (if VSIX is installed).

Note: Diagnostic messages refer to `[EnforcePure]` and `[Pure]` interchangeably.

To also surface uncaught exceptions and their propagated types, enable:

```ini
is_global = true

[*.cs]
dotnet_diagnostic.PS0010.severity = suggestion
dotnet_diagnostic.PS0011.severity = warning
purelysharp_report_exceptions = true
purelysharp_checked_exceptions = true
```

Example:

```csharp
using System;

public sealed class VoucherService
{
    public AcceptedDocument Render(Voucher voucher) => LoadAcceptedDocument(voucher);

    private static AcceptedDocument LoadAcceptedDocument(Voucher voucher) => RequireAcceptedDocument(voucher);

    private static AcceptedDocument RequireAcceptedDocument(Voucher voucher) => voucher.AcceptedDocument;
}

public sealed class Voucher
{
    private readonly AcceptedDocument? _acceptedDocument;

    public Voucher(AcceptedDocument? acceptedDocument)
    {
        _acceptedDocument = acceptedDocument;
    }

    public AcceptedDocument AcceptedDocument
    {
        get
        {
            if (_acceptedDocument is null)
            {
                throw new InvalidOperationException();
            }

            return _acceptedDocument;
        }
    }
}

public sealed class AcceptedDocument
{
}
```

With both exception-flow options enabled, the analyzer reports:

- `PS0010` on `Render`, because `System.InvalidOperationException` can escape the method.
- `PS0011` on the uncaught `LoadAcceptedDocument(voucher)` call site, with the propagated exception type and source chain.

For source methods in the same compilation, propagation follows the call graph recursively until it reaches the throw origin or a cycle. For metadata/library methods, the analyzer consumes build-regenerated built-in summaries embedded into the analyzer assembly, plus any explicit `PurelySharp.EffectSummary.json` additional files you supply.

## Diagnostics

- **PS0002: Purity Not Verified**

  - **Message:** `Method '{0}' is marked [EnforcePure]/[Pure], but its body contains operations the analyzer cannot prove pure`
  - **Severity:** Error (default; can be overridden in `.editorconfig` / ruleset)
  - **Meaning:** The method is marked for purity analysis, but the engine found at least one operation it treats as impure or could not classify as proven pure.
  - Note: Triggered for methods marked with either `[EnforcePure]` or `[Pure]`.

- **PS0010: Method May Throw Exceptions**

  - **Message:** `Method '{0}' can throw: {1}`
  - **Severity:** Info
  - **Enabled by:** `.editorconfig` option `purelysharp_report_exceptions = true`
  - **Meaning:** The analyzer found uncaught source-level `throw` or throw-expression paths, or propagated exception facts from source callees, and reports the exception type list in the diagnostic message and `purelysharp.exceptions.types` property. `PS0010` also emits structured `purelysharp.exceptions.categories` and `purelysharp.exceptions.sources` properties for report tooling, plus additive `purelysharp.exceptions.edges` JSON when structured callee-edge provenance is available.

- **PS0011: Operation May Throw Uncaught Exceptions**

  - **Message:** `Operation '{0}' may throw uncaught exceptions: {1}`
  - **Severity:** Warning
  - **Enabled by:** `.editorconfig` option `purelysharp_checked_exceptions = true`
  - **Meaning:** The analyzer found an uncaught exception at a specific call, property access, object creation, direct `throw`, definite divide-by-zero, or definite null-dereference site. `PS0011` emits the same structured exception type/category/source evidence as `PS0010`, the same additive `purelysharp.exceptions.edges` JSON when available, plus `purelysharp.exceptions.symbol` for the underlying callee when one is known.

- **PS0003: Misplaced Attribute**
  - **Message:** `The [EnforcePure] attribute can only be applied to method declarations.`
  - **Severity:** Error
  - **Meaning:** A purity attribute (`[EnforcePure]` or `[Pure]`) was found on a declaration type where it is not applicable (e.g., class, struct, field, property, parameter).
  - Note: The message text mentions `[EnforcePure]`, but the rule applies equally to `[Pure]`.

- **PS0004: Missing [EnforcePure] Attribute**
  - **Message:** `Method '{0}' appears to be pure but is not marked with [EnforcePure]. Consider adding the attribute to enforce and document its purity.`
  - **Severity:** Warning
  - **Meaning:** This method seems to only contain operations considered pure, but it lacks the `[EnforcePure]` attribute. Adding the attribute helps ensure its purity is maintained and communicates intent.

- **PS0005: Conflicting purity attributes**
  - **Message:** `Method '{0}' has both [EnforcePure] and [Pure] attributes applied`
  - **Severity:** Warning
  - **Meaning:** Apply only one of `[EnforcePure]` or `[Pure]` to a method. Both attributes together are redundant and can be confusing.

- **PS0006: [AllowSynchronization] requires a purity attribute**
  - **Message:** `Method '{0}' is marked with [AllowSynchronization] but is not marked with [EnforcePure] or [Pure]`
  - **Severity:** Warning
  - **Meaning:** `[AllowSynchronization]` only affects methods participating in purity analysis. Apply `[EnforcePure]` or `[Pure]` for it to have effect.

- **PS0007: Misplaced [AllowSynchronization] Attribute**
  - **Message:** `The [AllowSynchronization] attribute can only be applied to method declarations`
  - **Severity:** Error
  - **Meaning:** `[AllowSynchronization]` configures analyzer behavior for a method and should not be used on non-method declarations.

- **PS0008: Redundant [AllowSynchronization]**
  - **Message:** `Method '{0}' is marked with [AllowSynchronization] but contains no synchronization constructs`
  - **Severity:** Info
  - **Meaning:** Remove `[AllowSynchronization]` when the method does not use synchronization (e.g., lock).

## Building and Testing

You can build the solution and run the tests using the .NET CLI:

```powershell
# Build the solution
dotnet build PurelySharp.sln

# Run tests (analyzer, code fixes, and attribute / purity rules)
dotnet test PurelySharp.sln

# Optional: smoke-build a .NET Framework 4.7.2 consumer with the analyzer enabled
dotnet build PurelySharp.Smoke.Net472/PurelySharp.Smoke.Net472.csproj
```

## Corpus Reporting

`Tools/PurelySharp.CorpusReport` summarizes PurelySharp diagnostics from SARIF/errorlog output, or it can build projects and solutions with `ErrorLog` enabled and summarize the generated SARIF.

```powershell
# Summarize an existing SARIF file
dotnet run --project Tools/PurelySharp.CorpusReport -- artifacts/purelysharp.sarif

# Build a project/solution and write a JSON report
dotnet run --project Tools/PurelySharp.CorpusReport -- --output artifacts/purelysharp-report.json PurelySharp.sln
```

The JSON report includes a stable `SchemaVersion`, `PS0002`, `PS0004`, `PS0009`, `PS0010`, and `PS0011` counts, per-diagnostic evidence rows, impurity categories, exception categories/sources, rule-name counts, operation kinds, unsupported/unknown operation kinds, top impure APIs, catalog/config-source details, catalog-miss candidates, and false-positive candidates based on the structured diagnostic properties emitted by `PS0002`, `PS0010`, and `PS0011`. Exception-flow rows also preserve additive `purelysharp.exceptions.edges` payloads when present. Explanation diagnostics (`PS0009`) remain visible as diagnostic rows but do not double-count impurity aggregates.

For framework and library calibration, `Tools/PurelySharp.EffectSummary` can now emit implementation-derived purity classifications in its own JSON output. This is the preferred starting point for reviewing which manual pure entries are still justified by the current runtime implementation before changing analyzer behavior.

## CI Usage

For strict CI gates, fail builds on enforced purity violations and keep adoption suggestions advisory:

```ini
# .editorconfig
dotnet_diagnostic.PS0002.severity = error
dotnet_diagnostic.PS0004.severity = suggestion
dotnet_diagnostic.PS0009.severity = none
```

For SARIF-producing builds, use MSBuild `ErrorLog` and then summarize the output with the corpus report tool:

```powershell
dotnet build MySolution.sln /p:ErrorLog=artifacts/purelysharp.sarif
dotnet run --project Tools/PurelySharp.CorpusReport -- --output artifacts/purelysharp-report.json artifacts/purelysharp.sarif
```

`PS0002` diagnostics include stable properties for CI triage: impurity category, rule name, operation kind, symbol, catalog/config source, and callee chain. Evidence is preserved through common wrapper operations such as assignments, variable initializers, method arguments, LINQ arguments, property getters, static constructor triggers, object and nested member initializers, collection/spread expressions, user-defined operators/conversions, using/Dispose checks, mutable field reads/writes, dynamic dispatch including dynamic unary/binary operations, extern calls, synchronization, unsafe pointer operations, reflection/environment sources, unresolved delegate targets, array/object creation, array dimension/element/initializer references, direct throw-only bodies, impure throw exception expressions, and conservative recursive-call diagnostics. Use `PurelySharp.Baseline.json` as an additional file when adopting incrementally and suppressing known existing diagnostics by ID, symbol documentation ID, and relative path.

## Contributing

Contributions are welcome! Please feel free to open issues or submit pull requests, especially regarding the implementation of specific purity-checking rules which is the main focus for future development.

## License

This project is licensed under the MIT License.

## Supported Language Features

`[x]` = Implemented in the analyzer for typical uses (still conservative; may emit `PS0002` when unconvinced).
`[ ]` = Not modeled or treated as impure by default.

### Expressions

- [x] Literal expressions (numbers, strings, etc.)
- [x] `nameof` and `typeof` expressions (compile-time resolved)
- [x] Identifiers (local variables, parameters (`in`, `ref readonly`, value), `static readonly` fields)
- [x] Method invocations (Recursive analysis with cycle detection; known pure/impure BCL and user-configurable lists; unknown external callees treated conservatively.)
- [x] Member access (`static readonly` and known-pure BCL property reads; instance fields and mutable statics impure; instance property reads check receiver purity before treating known-pure BCL accessors as pure.)
- [x] Object creation (for immutable types)
- [x] Tuple expressions
- [x] Switch expressions (C# 8.0+)
- [x] Pattern matching
- [x] Null coalescing operators (`??`, `?.`)
- [x] Interpolated strings - Constant interpolation is pure; `FormattableString.Invariant(...)` is allowed when all interpolation expressions are pure, while impure interpolation expressions still report `PS0002`.
- [x] Stack allocations and Span operations
- [x] Indices and ranges (C# 8.0+) - basic range construction is treated as pure when endpoints are pure
- [x] Bit shift operations and basic binary/unary operators
- [x] Async/await expressions
- [x] Unsafe code blocks (Assumed impure)
- [x] Pointer operations (Assumed impure)

### Statements

- [x] Local declarations (with pure initializers)
- [x] Return statements - CFG analysis supports pure return expressions across reachable branches.
- [x] Expression statements (Pure callees ok; assignments to locals ok; instance field / property writes impure.)
- [x] If statements
- [x] Switch statements
- [x] Throw statements/expressions - throw operations report `PS0002`, including direct throw-only bodies, conditional throw expressions, and guard throws.
- [x] Try-catch-finally blocks
- [x] Local functions (Analyzed via invocation.)
- [x] Using statements
- [x] Using declarations (C# 8.0+)
- [x] Lock statements
- [x] Yield statements (iterator methods)
- [x] Fixed statements (Assumed impure)

### Collections and Data Structures

- [x] Immutable collections (System.Collections.Immutable) - Factory/update members are allowed when their element/key equality or comparison work is pure; mutable builders and `ImmutableInterlocked` remain impure.
- [x] Read-only collections (IReadOnly\* interfaces) - Creation assumed impure.
- [x] Arrays (when used in a read-only manner)
- [x] Tuples (creation)
- [x] Collection expressions (C# 12) - `System.Collections.Immutable.*`, `Span<T>` / `ReadOnlySpan<T>`, and owned non-escaping array targets; direct and spread elements are analyzed; mutable collection targets remain impure, and escaping arrays still report mutable-state escape
- [x] Mutable collections (List, Dictionary, etc.) - Creation/modification assumed impure.
- [x] Modifying collection elements - Assumed impure.
- [x] Default equality/comparison dispatch for collections, dictionaries, LINQ, comparers, and indexers is analyzed from the element/key type; unresolved generic type parameters stay conservative.
- [x] Inline arrays (C# 12)

### Method Types

- [x] Regular methods
- [x] Expression-bodied methods
- [x] Extension methods (Analyzed via invocation.)
- [x] Local functions (Analyzed via invocation.)
- [x] Abstract methods (Ignored)
- [x] Recursive methods (Cycle detection exists.)
- [x] Virtual/override methods (Analyzed like regular methods.)
- [x] Generic methods (Handled by symbol analysis.)
- [x] Async methods
- [x] Iterator methods (yield return)
- [x] Unsafe methods
- [x] Operator overloads (Analyzed via invocation/binary op.)
- [x] User-defined conversions (Analyzed via invocation.)
- [x] Static abstract/virtual interface members (C# 11)

### Type Declarations

(Analysis focuses on method bodies, not purity of types themselves)

- [x] Classes
- [x] Interfaces
- [x] Structs
- [x] Records (C# 9)
- [x] Record structs (C# 10)
- [x] Enums
- [x] Delegates - creation and invocation are analyzed; unresolved targets remain conservative.
- [x] File-local types (C# 11)
- [x] Primary constructors (C# 12)

### Member Declarations

- [x] Instance methods
- [x] Static methods
- [x] Constructors - Analyzed if marked.
- [x] Properties (get-only) - Handled.
- [x] Auto-properties (get-only or init-only) - Handled.
- [x] Fields (readonly) - Reading `static readonly` is pure. Reading instance or non-readonly static is impure. Assignment is impure.
- [x] Events
- [x] Indexers - Access/assignment handled.
- [x] Required members (C# 11)
- [x] Partial properties (C# 13)

### Parameter Types

- [x] Value types
- [x] Reference types (passed by value)
- [x] Ref parameters - Treated as impure.
- [x] Out parameters - Writes from pure method bodies and non-local `out` targets are impure; local or discard `out` arguments on known-pure or dispatch-proven calls can be pure.
- [x] In parameters
- [x] Params arrays
- [x] Params collections (C# 13)
- [x] Optional parameters
- [x] Optional parameters in lambda expressions (C# 12)
- [x] Ref readonly parameters

### Special Features

- [x] LINQ methods - Handled via invocation analysis.
- [x] String operations - Constants pure, method calls follow invocation rules.
- [x] Math operations - Basic operators pure, `System.Math` calls follow invocation rules.
- [x] Tuple operations (creation)
- [x] Conversion methods (Parse, TryParse, etc.) - Deterministic cataloged overloads are pure; culture/environment-dependent overloads remain conservative.
- [x] I/O operations (File, Console, etc.) - Explicitly marked impure.
- [x] Network operations - Explicitly marked impure.
- [x] Threading/Task operations - Side-effecting and scheduling members are marked impure; deterministic task/value-task wrappers are cataloged individually.
- [x] Random number generation - Explicitly marked impure.
- [x] Event subscription/invocation - Assumed impure.
- [x] Delegate invocation - DFA target tracking handled for locals and initializer-resolved members.

### Advanced Language Features

- [x] Pattern matching
- [x] Switch expressions
- [x] List patterns (C# 11)
- [x] Top-level statements (C# 9)
- [x] File-scoped namespaces (C# 10) - Implicitly supported.
- [x] Required members (C# 11)
- [x] Nullable reference types annotations (C# 8.0+) - Implicitly supported.
- [x] Caller information attributes
- [x] Source generators - N/A
- [x] Partial classes/methods - Implicitly supported.
- [x] Global using directives (C# 10) - Implicitly supported.
- [x] Generic attributes (C# 11)
- [x] Type alias for any type (C# 12) - Implicitly supported.
- [x] Experimental attribute (C# 12)

### C# 11 Specific Features

- [x] Extended nameof scope
- [x] Numeric IntPtr (nint/nuint)
- [x] Generic attributes
- [x] Unsigned right-shift operator (>>>)
- [x] Checked user-defined operators
- [x] Raw string literals
- [x] UTF-8 string literals
- [x] List patterns
- [x] File-local types
- [x] Required members
- [x] Auto-default structs
- [x] Pattern match Span<char> on constant string
- [x] Newlines in string interpolation expressions
- [x] ref fields and scoped ref
- [x] Generic math support (static virtual/abstract interface members)

### C# 12 Specific Features

- [x] Collection expressions - immutable collection types under `System.Collections.Immutable`, stack-only `Span` / `ReadOnlySpan`, and owned non-escaping array targets, with per-element purity checks; mutable collection targets (e.g. `List<T>`) stay impure, and escaping arrays report mutable-state escape
- [x] Primary constructors
- [x] Inline arrays
- [x] Optional parameters in lambda expressions
- [x] ref readonly parameters
- [x] Type alias for any type
- [x] Experimental attribute
- [x] Interceptors (preview)

### C# 13 Specific Features

- [x] params collections
- [x] Lock object
- [x] Escape sequence \e
- [x] Method group natural type improvements
- [x] Implicit indexer access in object initializers
- [x] ref/unsafe in iterators/async
- [x] ref struct interfaces
- [x] Overload resolution priority
- [x] Partial properties
- [x] field contextual keyword (preview)

### Field/Property Access

- [x] Readonly fields
- [x] Const fields
- [x] Get-only properties
- [x] Init-only properties (C# 9)
- [x] Mutable fields
- [x] Properties with setters
- [x] Static mutable fields
- [x] Event fields
- [x] Volatile fields (both reads and writes are considered impure due to their special memory ordering semantics and thread safety implications)

### Generic and Advanced Constructs

- [x] Generic methods
- [x] Generic type parameters with constraints (no special-case rules; analysis uses symbols as resolved by Roslyn)
- [x] Covariance and contravariance (no extra purity rules; variance does not change body analysis)
- [x] Reflection
- [x] Dynamic typing
- [x] Unsafe code

## Enum Operations

PurelySharp treats enums as pure data types. The following operations with enums are considered pure:

- Accessing enum values
- Converting enums to their underlying numeric type
- Comparing enum values
- Using methods from the `Enum` class

Note that the analyzer includes special handling for `Enum.TryParse<T>()` to treat it as a pure method despite using an `out` parameter.

### Examples

```csharp
public enum Status
{
    Pending,
    Active,
    Completed,
    Failed
}

public class EnumOperations
{
    [EnforcePure]
    public bool IsActiveOrPending(Status status)
    {
        return status == Status.Active || status == Status.Pending;
    }

    [EnforcePure]
    public int GetStatusCode(Status status)
    {
        return (int)status;
    }

    [EnforcePure]
    public bool ParseStatus(string value, out Status status)
    {
        return Enum.TryParse(value, out status);
    }

    [EnforcePure]
    public Status GetStatusFromValue(int value)
    {
        return (Status)value;
    }
}
```

## Delegate Operations

PurelySharp supports delegate types and operations. The purity analysis for delegates focuses on both the creation of delegates and their invocation:

### Delegate Purity Rules

- **Delegate Type Definitions**: Defining delegate types is always pure.
- **Delegate Creation**:
  - Creating a delegate from a pure method is pure.
  - Creating a lambda expression is pure if the lambda body is pure and it doesn't capture impure state.
  - Creating an anonymous method is pure if its body is pure and it doesn't capture impure state.
- **Delegate Invocation**:
  - Invoking a delegate is pure if the delegate target is pure and all arguments are pure.
  - Stored delegate fields/properties can also be resolved when their initializer target is known.
  - If the analyzer can't determine the purity of the delegate target, it conservatively marks the invocation as impure.
  - Delegate arguments passed to LINQ and delegate-invoking `List<T>` / `Array` helper methods are resolved too; unresolved delegate parameters remain conservative because those APIs invoke the delegate.
- **Delegate Combination**:
  - Combining delegates (`+=`, `+`) is pure if both delegate operands are pure.
  - Removing delegates (`-=`, `-`) is pure if both delegate operands are pure.

### Examples

```csharp
// Define delegate types (always pure)
public delegate int Calculator(int x, int y);
public delegate void Logger(string message);

public class DelegateOperations
{
    // Pure delegate field
    private readonly Func<int, int, int> _adder = (x, y) => x + y;

    [EnforcePure]
    public int Add(int x, int y)
    {
        // Creating and invoking a pure lambda delegate
        Calculator calc = (a, b) => a + b;
        return calc(x, y);
    }

    [EnforcePure]
    public IEnumerable<int> ProcessNumbers(IEnumerable<int> numbers)
    {
        // Using inline pure delegates with LINQ (pure)
        return numbers.Where(n => n > 0)
                     .Select(n => n * 2);
    }

    [EnforcePure]
    public Func<int, int> GetMultiplier(int factor)
    {
        // Higher-order function returning a pure delegate
        return x => x * factor;
    }

    [EnforcePure]
    public int CombineDelegates(int x, int y)
    {
        // Combining pure delegates
        Func<int, int> doubler = n => n * 2;
        Func<int, int> incrementer = n => n + 1;

        // Combined delegate is pure if components are pure
        Func<int, int> combined = n => incrementer(doubler(n));

        return combined(x) + combined(y);
    }

    // This would generate a diagnostic
    [EnforcePure]
    public void ImpureDelegateExample()
    {
        int counter = 0;

        // Impure delegate - captures and modifies a local variable
        Action incrementCounter = () => { counter++; };

        // Invoking impure delegate
        incrementCounter(); // Analyzer will flag this
    }
}
```

Note that delegate invocations are analyzed conservatively. If the analyzer cannot determine the purity of a delegate, it will mark the invocation as impure.

## Attributes

- [x] `[EnforcePure]` - Marks a method that should be analyzed for purity
- [x] `[AllowSynchronization]` - Marks synchronization intent for `PS0006`-`PS0008`; lock operations remain conservative for purity

## Namespaces with Common Impure Members

- System.IO
- System.Net
- System.Data
- System.Threading
- System.Threading.Tasks
- System.Diagnostics
- System.Security.Cryptography
- System.Runtime.InteropServices

## Types with Common Impure Members

- Random
- DateTime
- File
- Console
- Process
- Task
- Thread
- Timer
- WebClient
- HttpClient
- StringBuilder
- Socket
- NetworkStream

## Common Impure Operations

- Modifying fields or properties
- Reading or writing volatile fields
- Using Interlocked operations
- Calling methods with side effects
- I/O operations (file, console, network)
- Ambient environment, application-context, application-domain, or configuration state reads
- Side-effecting async/scheduling operations
- Locking (thread synchronization)
- Event subscription/raising
- Unsafe code and pointer manipulation
- Creating mutable collections

## Cross-Framework and Language Version Support

- [x] C# 8.0+ language features
- [x] Different target frameworks - analyzer is `netstandard2.0`; in-repo `PurelySharp.Smoke.Net472` builds with the analyzer on .NET Framework 4.7.2; primary regression suite runs on .NET 8 (`PurelySharp.Test`)

## Examples

### Pure Method Example

The analyzer ensures that methods marked with `[EnforcePure]` don't contain impure operations:

```csharp
using System;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public int PureHelperMethod(int x)
    {
        return x * 2; // Pure operation
    }

    [EnforcePure]
    public int TestMethod(int x)
    {
        // Call to pure method is also pure
        return PureHelperMethod(x) + 5;
    }
}
```

### Impure Method Examples

The analyzer detects impure operations and reports diagnostics:

#### Modifying State

```csharp
using System;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

public class TestClass
{
    private int _state;

    [EnforcePure]
    public int TestMethod(int value)
    {
        _state++; // Impure operation: modifies class state
        return _state;
    }
}

// Analyzer diagnostic PS0002 - method is marked pure but contains impure operations
```

#### I/O Operations

```csharp
using System;
using System.IO;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string path)
    {
        File.WriteAllText(path, "test"); // Impure operation: performs I/O
    }
}

// Analyzer diagnostic PS0002 - method is marked pure but contains impure operations
```

#### Console Output

```csharp
using System;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        Console.WriteLine("Hello World"); // Impure operation: console output
        return 42;
    }
}

// Analyzer diagnostic PS0002 - method is marked pure but contains impure operations
```

#### Static Field Access

```csharp
using System;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

public class TestClass
{
    private static string sharedState = "";

    [EnforcePure]
    public string TestMethod()
    {
        // Reading from static field is considered impure
        return sharedState;
    }
}

// Analyzer diagnostic PS0002 - method is marked pure but contains impure operations
```

#### Volatile Field Access

```csharp
using System;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

public class TestClass
{
    private volatile int _counter;

    [EnforcePure]
    public int GetCounter()
    {
        // Both reading and writing to volatile fields is considered impure
        // due to their special memory ordering semantics
        return _counter;
    }

    [EnforcePure]
    public void UpdateCounter(int value)
    {
        _counter = value; // Writing to volatile field is impure
    }
}

// Analyzer diagnostic PS0002 - volatile reads/writes are treated as impure
```

#### Thread Synchronization with Volatile Fields

```csharp
using System;
using System.Threading;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Method)]
public class AllowSynchronizationAttribute : Attribute { }

public class TestClass
{
    private volatile bool _initialized;
    private readonly object _lock = new object();

    [EnforcePure]
    [AllowSynchronization] // Even with AllowSynchronization, volatile read is impure
    public void EnsureInitialized()
    {
        if (!_initialized) // Reading volatile field is impure
        {
            lock (_lock)
            {
                if (!_initialized) // Reading volatile field again is impure
                {
                    Initialize();
                    _initialized = true; // Writing to volatile field is impure
                }
            }
        }
    }

    private void Initialize() { /* ... */ }
}

// Analyzer diagnostic PS0002 - volatile access remains impure even with [AllowSynchronization]
```

#### Atomic Operations with Interlocked

```csharp
using System;
using System.Threading;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

public class TestClass
{
    private volatile int _counter;

    [EnforcePure]
    public int IncrementAndGet()
    {
        // Using Interlocked with volatile fields is impure
        // since it modifies shared state in a thread-safe manner
        return Interlocked.Increment(ref _counter);
    }

    [EnforcePure]
    public int CompareExchange(int newValue, int comparand)
    {
        // All Interlocked operations are impure
        return Interlocked.CompareExchange(ref _counter, newValue, comparand);
    }
}

// Analyzer diagnostic PS0002 - Interlocked operations are treated as impure
```

### More Complex Examples

#### LINQ Operations (Pure)

LINQ operations are generally considered pure as they work on immutable views of data:

```csharp
using System;
using System.Linq;
using System.Collections.Generic;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> FilterAndTransform(IEnumerable<int> numbers)
    {
        // LINQ operations are pure
        return numbers
            .Where(n => n > 10)
            .Select(n => n * 2)
            .OrderBy(n => n);
    }
}

// No analyzer errors - method is pure
```

#### Iterator Methods (Pure)

Iterator methods using `yield return` can be pure:

```csharp
using System;
using System.Collections.Generic;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> GenerateFibonacciSequence(int count)
    {
        int a = 0, b = 1;

        for (int i = 0; i < count; i++)
        {
            yield return a;
            (a, b) = (b, a + b); // Tuple deconstruction for swapping
        }
    }
}

// No analyzer errors - method is pure
```

#### Lock Statements with AllowSynchronization

`[AllowSynchronization]` records synchronization intent and suppresses redundant synchronization-attribute diagnostics when a lock is present. Lock operations and mutable shared state remain conservative for purity analysis.

```csharp
using System;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public class AllowSynchronizationAttribute : Attribute { }

public class TestClass
{
    private readonly object _lockObj = new object();
    private readonly Dictionary<string, int> _cache = new Dictionary<string, int>();

    [EnforcePure]
    [AllowSynchronization]
    public int GetOrComputeValue(string key, Func<string, int> computeFunc)
    {
        lock (_lockObj) // Still reported as impure synchronization for PS0002
        {
            if (_cache.TryGetValue(key, out int value))
                return value;

            // Compute is allowed if the function is pure
            int newValue = computeFunc(key);
            _cache[key] = newValue; // Mutable shared state remains impure
            return newValue;
        }
    }
}

// Analyzer reports PS0002: synchronization and mutable shared state remains impure.
```

#### Switch Expressions and Pattern Matching

Modern C# pattern matching and switch expressions are supported:

```csharp
using System;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

public class Shape { }
public class Circle : Shape { public double Radius { get; } }
public class Rectangle : Shape { public double Width { get; } public double Height { get; } }

public class TestClass
{
    [EnforcePure]
    public double CalculateArea(Shape shape)
    {
        // Switch expression with pattern matching
        return shape switch
        {
            Circle c => Math.PI * c.Radius * c.Radius,
            Rectangle r => r.Width * r.Height,
            _ => 0
        };
    }
}

// No analyzer errors - method is pure
```

#### Working with Immutable Collections

Methods that use immutable collections remain pure:

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public ImmutableDictionary<string, int> AddToCounters(
        ImmutableDictionary<string, int> counters,
        string key)
    {
        // Working with immutable collections preserves purity
        if (counters.TryGetValue(key, out int currentCount))
            return counters.SetItem(key, currentCount + 1);
        else
            return counters.Add(key, 1);

        // Note: The original collection is not modified
    }
}

// No analyzer errors - method is pure
```

#### Complex Method with Multiple Pure Operations

Complex methods combining multiple pure operations:

```csharp
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;

[AttributeUsage(AttributeTargets.Method)]
public class EnforcePureAttribute : Attribute { }

public class TestClass
{
    [EnforcePure]
    public (double Average, int Min, int Max, ImmutableList<int> Filtered) AnalyzeData(
        IEnumerable<int> data, int threshold)
    {
        // Local function (must also be pure)
        bool IsOutlier(int value) => value < 0 || value > 1000;

        // Use LINQ to process the data
        var filteredData = data
            .Where(x => !IsOutlier(x) && x >= threshold)
            .ToImmutableList();

        // Multiple computations on the filtered data
        var average = filteredData.Average();
        var min = filteredData.Min();
        var max = filteredData.Max();

        // Return a tuple with the results
        return (Average: average, Min: min, Max: max, Filtered: filteredData);
    }
}

// No analyzer errors - method is pure
```

### Limitations and Edge Cases

The following examples demonstrate cases where the analyzer may fail to correctly identify impure operations:

#### Indirect Method Impurity

The analyzer does not fully trace opaque/library calls. For virtual and interface dispatch, it expands concrete candidates within the current compilation; it can still narrow casts and conditional receiver branches down to a single sealed in-compilation implementation, and when no concrete target can be proven and no external implementations are possible, it assumes purity.

## Constructor Analysis

When applying `[EnforcePure]` to constructors, the analyzer applies special rules to account for the unique purpose of constructors. A constructor marked as pure must follow these rules:

1. **Instance field/property assignment is permitted**: Unlike regular methods, constructors can assign values to instance fields and properties of the containing type.

2. **Static field modification is not permitted**: Modifying static fields is still considered impure, as this affects state beyond the instance being constructed.

3. **Impure method calls are not permitted**: Calling impure methods (like I/O operations) from a pure constructor is not allowed.

4. **Collection initialization is permitted**: Creating and initializing collections (e.g., `new List<int> { 1, 2, 3 }`) is allowed if the collection is assigned to an instance field.

5. **Base constructor calls**: If a constructor calls a base constructor, the base constructor must also be pure.

### Examples

#### Pure Constructor

```csharp
[AttributeUsage(AttributeTargets.Constructor)]
public class EnforcePureAttribute : Attribute { }

public class Person
{
    private readonly string _name;
    private readonly int _age;
    private readonly List<string> _skills;

    [EnforcePure]
    public Person(string name, int age)
    {
        _name = name;
        _age = age;
        _skills = new List<string>(); // Allowed: initializing a collection field
    }
}
```

#### Impure Constructor (Static Field Modification)

```csharp
public class Counter
{
    private static int _instanceCount = 0;
    private readonly int _id;

    [EnforcePure]
    public Counter() // Error: Modifies static state
    {
        _id = ++_instanceCount; // Impure: modifies static field
    }
}
```

#### Impure Constructor (Calling Impure Methods)

```csharp
public class Logger
{
    private readonly string _name;

    [EnforcePure]
    public Logger(string name) // Error: Calls an impure method
    {
        _name = name;
        InitializeLog(); // Calls impure method
    }

    private void InitializeLog()
    {
        Console.WriteLine($"Logger {_name} initialized"); // Impure operation
    }
}
```

## Demo project

A ready-to-run demo app is included in the solution: `PurelySharp.Demo`.

- What it shows
  - PS0002: Methods marked `[EnforcePure]` performing impure operations (state mutation, I/O, volatile reads, array mutation)
  - PS0004: Methods that appear pure but are missing `[EnforcePure]`
  - PS0003 is intentionally not demonstrated in the demo to keep the focus on core purity rules

- How it's wired
  - References `PurelySharp.Analyzer` and `PurelySharp.CodeFixes` as analyzers via project references
  - References `PurelySharp.Attributes` as a normal project reference
  - Local `.editorconfig` in `PurelySharp.Demo` tunes severities: PS0002=warning, PS0004=suggestion, PS0003=none

- Run the demo
  ```powershell
  # Build the whole solution (ensures analyzers are built)
  dotnet build .\PurelySharp.sln -c Release

  # Build just the demo project
  dotnet build .\PurelySharp.Demo\PurelySharp.Demo.csproj -c Release
  ```

- In Visual Studio
  - Install the VSIX (see above) for live diagnostics while editing
  - Open `PurelySharp.Demo` and inspect `Program.cs` to see the diagnostics inline
