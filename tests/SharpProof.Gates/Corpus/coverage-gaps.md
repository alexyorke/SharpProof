# Coverage gaps in the newer OSS sources

> This triage is a dated, hand-made analysis of master `4ae8b6e93`. Its counts are not updated: since then, type initializers that only construct fresh values are admitted, and more methods are Supported. For current numbers see the generated [verdicts-by-source.md](verdicts-by-source.md). The buckets below remain the best description of *which C# constructs* stop the analysis.

This is a triage of the 300 methods from MoreLINQ, NodaTime and Humanizer (`OSS0201`–`OSS0500`). The original algorithms source is not included. The data comes from master `4ae8b6e93` (manifest 9, before the date/time models below). Each method is counted once, under the **first** reason SharpProof cannot handle it.

## How the reasons were found

1. **Snapshot reason codes.** [expected.canonical.snapshot](expected.canonical.snapshot) records each method's verdict and SP diagnostics. Of the 286 Unknowns:
   - 208 carry SP0047 `Advisory:Lowering:UnsupportedOperationKind`.
   - 63 carry SP0002: the body lowered, but its effects were not proved.
   - 5 carry SP0047 `Advisory:EntryInitialization:TypeInitializer`.
   - The rest carry other SP0047 lowering or binding gaps.
2. **Lowering trace.** `UnsupportedOperationKind` names the statement, not the construct, so each method was lowered again with the advisory pipeline: `TotalBodyLowering` with opaque calls, which is what SP0047 reports from. The trace walks the approximated operations under the first abstention down to the innermost one.
   - When that operation calls source code, the trace follows the callee. It checks the inliner's own refusals first, in `TotalSourceCallSession.TryPrepare` order:
     1. dispatch;
     2. struct receiver;
     3. `ref` parameters;
     4. value domain;
     5. capturing local function;
     6. iterator;
     7. a type initializer.
   - Only then does the trace explain the callee's body.
   - For SP0002 methods, the first opaque call that has all effects is the reason. Exception constructors on throw paths come last.
3. **Type initializers.** The "Root type has an initializer" column counts methods whose own declaring type has a non-constant static initializer. Such a method also gets SP0047 `EntryInitialization` once its body lowers, so fixing its first reason alone cannot prove it.

## Buckets

| # | First reason | Methods | MoreLINQ | NodaTime | Humanizer | Root type has an initializer | Examples |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Already Supported (Proven/Refuted) | 14 | 2 | 3 | 9 | 0 | `OSS0206` NotNull, `OSS0216` Assert, `OSS0315` IsLeapYear, `OSS0317` GetMonthsInYear |
| 2 | Entry initialization: the method or a callee it calls is declared in a type with a non-constant static initializer. **Skipped**: another session owns type-initializer effects. | 123 | 48 | 37 | 38 | 101 | `OSS0204` Assert, `OSS0243` IdFn, `OSS0310` CivilToScriptural, `OSS0457` Convert |
| 3 | Interface, virtual and abstract dispatch, including `foreach` over interfaces | 26 | 5 | 16 | 5 | 19 | `OSS0213` Consume, `OSS0359` Parse, `OSS0398` GetZoneInterval, `OSS0473` Convert |
| 4 | Source value types: `this` field reads, constructors and instance calls on (readonly) structs | 25 | 5 | 20 | 0 | 7 | `OSS0251` AsListLike, `OSS0326` ForOffset, `OSS0345` CompareTo, `OSS0346` FromTrustedDuration |
| 5 | Iterators (`yield`) and async sequences | 24 | 24 | 0 | 0 | 23 | `OSS0209` Cartesian, `OSS0273` Repeat, `OSS0281` Sequence |
| 6 | BCL date/time value types: `DateTime`, `DateOnly`, `TimeOnly`, `TimeSpan`, `DateTimeOffset` | 21 | 0 | 0 | 21 | 0 | `OSS0410` Humanize, `OSS0412` Humanize, `OSS0414` Humanize, `OSS0433` Convert |
| 7 | `ref`/`out` parameters: source callees and `TryGetValue` | 13 | 1 | 3 | 9 | 7 | `OSS0212` TryGetValue, `OSS0341` Add, `OSS0470` CollectPartsUnderAHundred, `OSS0485` ConvertImpl |
| 8 | Generic type parameters: null tests on `T`, `EqualityComparer<T>.Default`, generic math, generic constructors | 10 | 5 | 5 | 0 | 1 | `OSS0228` IndexOf, `OSS0252` Create, `OSS0361` Add, `OSS0363` ParsePattern |
| 9 | Lambdas, delegates, capturing local functions and LINQ | 10 | 5 | 4 | 1 | 5 | `OSS0234` Fold, `OSS0248` Lag, `OSS0383` CreateYearOfEraHandler, `OSS0430` Humanize |
| 10 | Interpolated strings, `params object[]` formatting, and string `+=` on elements | 7 | 0 | 3 | 4 | 1 | `OSS0302` GetIslamicId, `OSS0385` GetQuotedString, `OSS0401` AppendArticlePrefix, `OSS0443` DateHumanize_Now |
| 11 | Other unmodeled BCL members: culture-sensitive string and number APIs, `StringBuilder`, ambient `CultureInfo`, exception constructors | 6 | 1 | 1 | 4 | 1 | `OSS0365` Format2DigitsNonNegative, `OSS0409` ResolveForUiCulture, `OSS0444` GetResourceKey, `OSS0465` Convert |
| 12 | `typeof` and runtime type tests | 4 | 0 | 2 | 2 | 0 | `OSS0391` CanConvertFrom, `OSS0400` ForZone, `OSS0421` DehumanizeTo, `OSS0491` Convert |
| 13 | Writes into a freshly allocated array: the verifier cannot replay the candidate and reports `CounterexampleNotReplayable` | 3 | 0 | 3 | 0 | 3 | `OSS0308`, `OSS0314`, `OSS0316` GenerateTotalDaysByMonth |
| 14 | Floating-point arithmetic | 3 | 0 | 0 | 3 | 3 | `OSS0426` ToHeading, `OSS0461` Convert, `OSS0468` Convert |
| 15 | Control-flow graph shape (`UnsupportedControlFlow`) | 3 | 0 | 0 | 3 | 1 | `OSS0427` BuildDefault, `OSS0492` Convert, `OSS0493` Convert |
| 16 | Collection expressions and span conversions | 2 | 1 | 0 | 1 | 2 | `OSS0201` Acquire, `OSS0448` GetResourceKey |
| 17 | Source constructors that are not admitted: field initializers, base calls, field writes from calls | 2 | 1 | 1 | 0 | 0 | `OSS0217` Disposable, `OSS0337` ToClock |
| 18 | Enum relational and bitwise operators, and enum casts | 2 | 0 | 2 | 0 | 0 | `OSS0318` GetLocalDate, `OSS0386` HasAny |
| 19 | Nullable value types | 1 | 1 | 0 | 0 | 1 | `OSS0214` CountDown |
| 20 | `lock` on `System.Threading.Lock` | 1 | 1 | 0 | 0 | 0 | `OSS0229` Dispose |
| | **Total** | **300** | **100** | **100** | **100** | **175** | |

## What the table hides

- **Entry initialization dominates.** It is the first reason for 123 methods. It also blocks the declaring type of 175 methods whatever their first reason is.
  - In MoreLINQ, `MoreEnumerable` has a static initializer, so every call between its methods is refused before the callee's body is read.
  - Iterators show the effect: 23 of the 24 iterator methods sit in such a type, so modeling iterator calls alone would prove none of them.
- **Default-message exception constructors are a second blocker that runs through every source.** Examples are `new ArgumentNullException(nameof(x))` and NodaTime's `Preconditions.Check*`. Such a constructor builds its message from runtime resources, which can call back into a user-defined culture, so it is modeled as an opaque call that may do anything (see `ExceptionResourceEffects`).
  - 99 of the 300 bodies construct one directly.
  - None of the Proven OSS methods contains a `throw`.
  - This is deliberate conservative policy, not a gap to close here.
- **Catalog effects cannot refute.** A cataloged write to a parameter (`List<T>.Add`) or a culture read ends as `CounterexampleNotReplayable`: the worker cannot replay a witness through an opaque call. Modeling culture-sensitive members therefore cannot turn the bucket 11 methods into Refuted.

## Choice for the follow-up PR

Bucket 6, BCL date/time value types, is the largest bucket that can be closed soundly in one change without running into a type initializer.

- **Entry initialization** is owned by another session.
- **Dispatch** cannot be closed: the callee is unknown.
- **Source structs** need a value-type model for field projections, construction, `default` values and copies. That is more than one PR.
- **Iterators** sit behind type initializers in 23 of 24 cases.
- **Date/time types** need one frontend rule and specified catalog facts. None of their 21 declaring types has a type initializer.

The change:

- **Operators.** The frontend rejected every user-defined operator. It now lowers a non-lifted binary operator whose method is a static, non-virtual metadata method as a call to that method, with the catalog's effects. Source operators, lifted operators, `&&`/`||` with user-defined truth operators, and static abstract (generic math) operators still abstain.
- **Pure members.** The default catalog models 56 documented pure, non-throwing, non-allocating members:
  - the calendar and time-of-day getters of `DateTime`, `DateOnly`, `TimeOnly` and `TimeSpan`;
  - `DateTimeOffset.UtcDateTime` and `UtcTicks`;
  - the six comparison operators of `DateTime`, `DateOnly`, `TimeOnly` and `TimeSpan`.
- **Unmodeled members.** The following stay unmodeled and Unknown:
  - constructors and `TimeSpan.FromSeconds`. A metadata struct construction is not lowered as a call, and the validating constructors (`new DateTime(y, m, d)`, `new TimeSpan(d, h, m, s)`) throw `ArgumentOutOfRangeException` or `OverflowException` with resource messages;
  - date arithmetic (`DateTime - DateTime`, `DateTime + TimeSpan`), because it can overflow;
  - `DateTimeOffset` operators, which the catalog does not list;
  - culture-sensitive formatting (`DateTime.ToString(format)`);
  - `DateTime.Now` and `UtcNow`, which are nondeterministic.

After the change, the remaining first reasons for these 21 methods are entry initialization (the `Configurator` registries), `Math.Abs(long)`, validating constructors, and culture formatting. The corpus gate measured no verdict change: the canonical snapshot is unchanged, and every one of the 21 stays Unknown at its next reason.
