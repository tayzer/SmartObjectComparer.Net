# Baseline vs Live comparisons

Use this when the two versions you need to compare are never running at the same time:
version A exists today, version B ships later, and A is switched off in between.

You capture A's behaviour once into a **baseline package**, then replay it against B
whenever B becomes available. Nothing is sent to A during the replay.

## The two modes

| Mode | What runs | What you get |
|---|---|---|
| **Capture baseline** | One endpoint is called with your request files | A versioned package: the requests, the responses, and the comparison model each response mapped to |
| **Baseline vs Live** | Only the live endpoint is called; the expected side is read from the package | The normal comparison report, labelled *Baseline vs Live* |

Both require a **plugin run profile**: a baseline stores the comparison model that
plugin's comparison defines, so a run without one has no model to store or replay.
Plain live-vs-live runs are unchanged and need no profile.

## 1. Capture a baseline

In the desktop app:

1. **Compare Requests** tab → **Comparison Mode** → *Capture baseline*.
2. Pick your plugin run profile (this fills in the endpoint and comparison settings).
3. Name the baseline — for example `orders-v4-pre-upgrade`. The hint under the field
   tells you which version this run will write; existing versions are never replaced.
4. Add your request files as usual and start the run.

From the CLI:

```bash
paritybench request ./requests --run-profile client-lookup --capture-baseline "orders-v4-pre-upgrade"
```

Every scenario that returned a success status is written to the package. A scenario
whose call failed, or returned a non-2xx status, is reported in the run but deliberately
left out — a response the endpoint could not produce must not become an expected result.

The package lands in `<workspace>/baselines/<id>/v<n>/`:

```
baseline.json                             provenance + one entry per scenario
requests/<path>                           the exact request that produced it
responses/raw/<path>                      the response as it came off the wire
responses/canonical/<path>.json           the comparison model — the expected side
```

## 2. Replay it against the new version

1. **Comparison Mode** → *Baseline vs Live*.
2. Pick the baseline. The panel shows when it was captured, from which endpoint,
   with which plugin version and environment.
3. Set **Endpoint B (live)** to the newly deployed version. Endpoint A is read-only:
   it names the captured endpoint for the report but is never called.
4. Start the run. The requests come from the package, so there is nothing to upload.

From the CLI:

```bash
paritybench request --run-profile client-lookup --baseline orders-v4-pre-upgrade@1 --endpoint-b https://new.example.test/lookup
```

Omit `@1` to use the latest version.

Your ignore rules, smart-ignore rules, masking and comparison flags apply exactly as
they do to a live-vs-live run. Mask rules are applied to the baseline side too, using
the *current* run's rules — a mask you added since the capture still hides the field on
both sides.

## 3. Read the report

A replay report is titled **Baseline vs Live** and carries a banner naming the package,
when it was captured, from which endpoint and environment. It also states plainly that
data, configuration and external dependencies may have changed since the capture, so a
difference is not automatically a software regression.

The banner turns into a warning when either:

- the **plugin version** differs between capture and replay — the mapping itself may
  have changed, which is not the same as the endpoint's behaviour changing; or
- the **environment** differs — you are comparing across environments as well as
  across time.

## Managing packages

The **Baselines** tab lists every captured version with its provenance and scenario
list, and offers export, import and delete.

- **Export** writes a single `.pbbaseline` file (a zip of the package directory).
- **Import** always adds a *new version*, so bringing a package back from another
  machine can never overwrite one already in your library.

The same operations from the CLI:

```bash
paritybench baseline list
```

```bash
paritybench baseline export orders-v4-pre-upgrade@1 ./orders-v4.pbbaseline
```

```bash
paritybench baseline import ./orders-v4.pbbaseline
```

```bash
paritybench baseline delete orders-v4-pre-upgrade@1
```

## What a baseline does and does not freeze

A baseline freezes **the comparison model each scenario produced**, not the raw
response. Replay deserializes that stored model directly, so an approved expected result
stays exactly as it was captured even if the plugin's mapping changes later. That is
also why a plugin upgrade between capture and replay can surface as differences: the
report flags the version change so you can tell that apart from a behaviour change.

A baseline does **not** capture the data, configuration or downstream services behind
the endpoint. If the customer record changed between capture and replay, the comparison
will say so — that is the caveat the report banner exists for. Re-capture when the
underlying data has moved on.

## Troubleshooting

**"Baseline … was captured with comparison X, but this run selected Y."**
The package belongs to a different plugin comparison. Select the run profile the
baseline was captured with.

**"Baseline … has no captured scenario for '…'."**
The live run included a request the package never saw. In the normal flow the requests
come from the package, so this means the batch was assembled from somewhere else.

**"Baseline capture and replay require a plugin comparison."**
Select a plugin run profile before starting the run.

**A capture run failed and no baseline appeared.**
That is intended: a package is only sealed once the run that produced it finished, so a
partial capture leaves nothing behind. Fix the cause and capture again.
