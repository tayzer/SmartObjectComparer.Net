# Comparison Rules

A raw comparison of two real API versions produces mostly noise: trace IDs, timestamps, generated keys, collection ordering, whitespace. Rules are how you tell ParityBench which differences don't count, so what's left is signal.

There are five mechanisms, in rough order of how blunt they are:

| Mechanism | Scope | Use when |
|---|---|---|
| Comparison flags | Whole run | The whole comparison should be lenient about a *kind* of difference |
| Ignore rules | One exact property path | You know the precise field |
| Smart ignores | Every path matching a name, pattern, or type | The field appears in many places, or you don't know all of them |
| Mask rules | One property path, value rewritten before comparing | The value is sensitive, or only part of it is meaningful |
| Accepted differences | A recurring difference *shape*, across runs | It's a known, triaged difference you don't want to keep re-reading |

Rules do not change the stored response. They apply during comparison, and to the report. Re-running with different rules re-classifies the same data.

## Comparison flags

Whole-run switches on `ComparisonOptions`, exposed as checkboxes in the app.

| Flag | Default | Effect |
|---|---|---|
| `IgnoreCollectionOrder` | off | Collections compare as sets rather than sequences |
| `IgnoreStringCase` | off | String comparison is case-insensitive |
| `IgnoreTrailingWhitespaceAtEnd` | off | Trailing whitespace on string values is not a difference |
| `TreatNullAndEmptyCollectionsAsEqual` | off | `null` and `[]` are the same thing |
| `IgnoreXmlNamespaces` | **on** | XML namespace prefixes and declarations are not a difference |
| `MaxDifferences` | `100` | Stop collecting differences for a pair after this many. Must be > 0 |

`IgnoreCollectionOrder` is the expensive one. On large collections it forces unordered matching; the comparer tries deterministic scalar/identifier-based matching first and only falls back to full unordered matching when it can't find a stable key. If a large run is slow, this is the first flag to question.

## Ignore rules

One exact property path per line. The path is a dotted path through the canonical comparison model.

```text
Body.ConsumerReportResponse.Header.TraceId
Body.ConsumerReportResponse.Metadata.GeneratedAt
```

Blank lines are skipped; lines starting with `#` are comments.

Use these when you know the exact field. If the same logical field appears at ten paths, use a smart ignore instead.

## Smart ignores

`Kind=Value`, one per line. Four kinds:

| Kind | Matches | Example |
|---|---|---|
| `PropertyName` | Any property with this name, at any depth | `PropertyName=TraceId` |
| `NamePattern` | Any property whose name matches the pattern | `NamePattern=*Timestamp` |
| `PropertyType` | Any property of this type | `PropertyType=DateTime` |
| `CollectionOrdering` | Ordering differences within the named collection only | `CollectionOrdering=Items` |

```text
# generated identifiers, everywhere they appear
PropertyName=ReportId
PropertyName=ProviderTraceId

# anything that looks like a timing field
NamePattern=*Milliseconds
```

`CollectionOrdering` is the targeted alternative to the global `IgnoreCollectionOrder` flag — use it when exactly one collection has unstable ordering and you don't want to pay for unordered matching across the whole model.

An unknown kind, or a line without `=`, fails the run with the offending line number rather than being silently skipped.

## Mask rules

`<propertyPath>[|option=value]…`, one per line. Masking rewrites the value on **both sides** before comparison, so two different-but-equally-masked values compare equal, and the sensitive value never reaches the report.

```text
Body.ConsumerReportResponse.Subject.NationalIdentifier|preserveLast=4
Body.ConsumerReportResponse.Subject.Email|mask=#
```

| Option | Default | Meaning |
|---|---|---|
| `preserveLast` | `0` | Leave this many trailing characters visible. Must be a non-negative whole number |
| `mask` | `*` | The masking character. Exactly one character |

`…|preserveLast=4` turns `8891234567` into `******4567` on both sides — so a genuinely different last-four still fails, while the rest is neither compared nor stored in the report.

Masking applies to the baseline side too, using the *current* run's rules. A mask you add after a capture still hides the field on both sides of a replay.

## Where rules come from

Rules stack from three places, and later sources add to earlier ones:

1. **Plugin defaults.** A plugin's `IComparisonDefinition` carries `ComparisonRuleDefaults` — the fields its author already knows are noisy. Every run using that comparison starts from these, and the Rules Studios show them as read-only chips so you can see what you've already inherited.
2. **The run profile.** A saved profile's `comparison` block persists the rules you settled on for that environment.
3. **The run itself.** Whatever you type in the studios before starting.

## The Rules Studios

In the app, **Compare Requests → step 2** has two panels:

- **Ignore Rules Studio** — browse the actual comparison type's property tree and tick fields rather than typing paths. It also previews the profile's inherited defaults.
- **Mask Rules Studio** — same tree, with the mask options per field.

Both accept free text in the same syntax as above, so anything you can click you can also paste.

The studios browse the selected comparison's CLR type. If the tree says "select a model", you haven't chosen a response model or a plugin run profile yet.

## Accepted differences

An accepted difference is a triaged, recurring difference you've decided is expected — a known behaviour change you don't want cluttering every subsequent run's results.

Unlike the rules above, an accepted difference is matched by **fingerprint**, not by path. The fingerprint normalizes the property path and both values, replacing volatile-looking content — GUIDs, ISO dates, long numbers, long hex tokens — and names containing tokens like `id`, `trace`, `session`, `timestamp` with placeholders. So one accepted difference covers the same *shape* of difference across every pair and every future run, rather than one literal occurrence.

Each profile carries the sample that created it (path and both values), a status, and optionally a ticket id and notes — so a difference that's accepted because of a known open ticket stays traceable back to it.

Manage these in the accepted-differences panel in the app. They apply to result presentation, so a run whose only surviving differences are accepted reads as clean without you having weakened the comparison itself.

## Choosing between them

- The difference is **structural or type-wide** → a comparison flag.
- You know the **exact field** → an ignore rule.
- The field **recurs at many paths**, or you keep finding new ones → a smart ignore.
- The value is **sensitive**, or only partly meaningful → a mask rule.
- The difference is **real, known, and triaged** → an accepted difference. Don't ignore it; ignoring it hides a genuine behaviour change from the next person.

## See also

- [Reports and Results](reports-and-results.md) — reading what survived your rules
- [Building a Plugin](building-a-plugin.md) — shipping sensible `ComparisonRuleDefaults` with a comparison
- [Baseline vs Live](baseline-vs-live.md) — how rules apply to a replayed baseline
