# File/Folder Comparison — Error Detail & Raw File Preview

## Problem Statement

When a file pair fails deserialization in the File/Folder Comparison tab (malformed XML, wrong domain model, missing files, etc.), the backend correctly captures the error into `FilePairComparisonResult.ErrorMessage` and the grid displays a warning icon with a tooltip. However, clicking **"View"** on an errored pair renders **nothing** — the `DetailedDifferencesView` is guarded by `DifferenceSummary != null`, which is always null for error results since no `Summary` was produced.

The user has no way to:
1. See the full error message and understand what went wrong.
2. Inspect the raw file content to diagnose the issue.
3. Compare the raw file content side-by-side when deserialization fails on one or both sides.

## Current Behavior

```
User clicks "View" on errored pair
  → SelectPairResult() sets DifferenceSummary = selectedPair.Summary (null)
  → Render guard: @if (... && DifferenceSummary != null) → FALSE
  → Nothing renders. Page scrolls to empty space.
```

### Error Capture Points (Already Working)

| Location | What It Catches |
|---|---|
| `DirectoryComparisonService.CompareDirectoriesAsync` catch block | All exceptions during file stream comparison |
| `DirectoryComparisonService.CompareFolderUploadsAsync` catch block | Same, for browser-uploaded folder pairs |
| `HighPerformanceComparisonPipeline` Stage 1 | Deserialization failures (passes `DeserializedFilePair.ErrorMessage` through channel) |
| `HighPerformanceComparisonPipeline` Stage 2 | Comparison failures on deserialized objects |
| `ComparisonOrchestrator` | Null deserialization results, format mismatches |

All paths populate `FilePairComparisonResult.ErrorMessage` and `ErrorType`. The grid already renders these with a warning icon. The gap is **only in the detail view**.

## Solution Overview

Two components:

1. **`ErrorDetailView.razor`** — A new Blazor component that renders when a selected pair has `HasError == true`. Shows the error message, error type, diagnostic tips, and optional raw file preview.
2. **`Home.razor` routing change** — Add an `else if` branch so errored pairs render `ErrorDetailView` instead of silently skipping.
3. **Reuse `RawTextComparisonService.ComputeLineDifferences`** — Extract the static diff algorithm from `RawTextComparisonService` into a shared utility so both Request Comparison and File/Folder Comparison can use it, or expose a simpler file-path-based overload on the service.

## Implementation Plan

### Task 1: Add file-path-based raw diff method to `RawTextComparisonService`

**File:** `ComparisonTool.Core/RequestComparison/Services/RawTextComparisonService.cs`

Add a new public method that accepts two file paths directly (instead of `ClassifiedExecutionResult`):

```csharp
/// <summary>
/// Compares two files on disk as raw text. Used as a fallback when domain-model
/// deserialization fails for file/folder comparison.
/// </summary>
public async Task<List<RawTextDifference>> CompareFilesRawAsync(
    string? file1Path,
    string? file2Path,
    CancellationToken cancellationToken = default)
```

This method:
- Calls the existing `ReadResponseBodyAsync(filePath, ct)` for both paths.
- Calls the existing `ComputeLineDifferences(linesA, linesB)`.
- Returns the raw diff list.
- Handles missing/null paths gracefully (returns empty diff or "File not found" entry).

**Why:** Reuses the existing truncation + line diff logic without duplicating code. The `ReadResponseBodyAsync` and `ComputeLineDifferences` methods are already private — this just exposes them via a clean public overload.

**Estimate:** ~20 lines of new code.

---

### Task 2: Create `ErrorDetailView.razor` component

**File:** `ComparisonTool.Web/Components/Comparison/ErrorDetailView.razor` (NEW)

A Blazor component that renders when a file pair has `HasError == true`.

**Parameters:**
```csharp
[Parameter] public FilePairComparisonResult SelectedPair { get; set; }
[Parameter] public List<RawTextDifference>? RawFileDifferences { get; set; }
```

**UI Layout:**

```
┌─────────────────────────────────────────────────────┐
│ ⚠ Comparison Error                                  │
│ FileName1 vs FileName2                              │
├─────────────────────────────────────────────────────┤
│ MudAlert (Severity.Error):                          │
│   Error Type: InvalidOperationException             │
│   Message: "There is an error in XML document (2,5)"│
├─────────────────────────────────────────────────────┤
│ MudAlert (Severity.Info):                           │
│   Diagnostic Tips:                                  │
│   • Check that the selected domain model matches    │
│     the file format                                 │
│   • Verify the file contains valid XML/JSON         │
│   • Check the file is not empty or truncated        │
│   • For HTTP responses, the file may contain an     │
│     error page instead of expected content           │
├─────────────────────────────────────────────────────┤
│ Raw File Content (collapsible, if RawFileDifferences│
│ is provided):                                       │
│   [Reuse RawTextDifferencesView or inline the same  │
│    diff table layout]                               │
└─────────────────────────────────────────────────────┘
```

**Key behaviors:**
- Always visible when `HasError` is true — no null-guard issues.
- Error type badge uses color-coding: `FileNotFoundException` → orange, deserialization errors → red, null result → amber.
- Diagnostic tips are contextual based on `ErrorType`:
  - `InvalidOperationException` with "XML" in message → "Check XML is well-formed"
  - `FileNotFoundException` → "The expected file does not exist at the path"
  - `InvalidOperationException` with "null" → "Deserialization succeeded but returned null — check root element name"
- Raw file diff section is collapsed by default (expandable) since files could be large.

**Estimate:** ~120 lines (Razor + code-behind).

---

### Task 3: Update `Home.razor` to route errored pairs to `ErrorDetailView`

**File:** `ComparisonTool.Web/Components/Pages/Home.razor`

**Current** (lines ~295-304):
```razor
@if (FolderComparisonResult != null && SelectedPairIndex >= 0 && DifferenceSummary != null)
{
    <DetailedDifferencesView ... />
}
```

**Changed to:**
```razor
@if (FolderComparisonResult != null && SelectedPairIndex >= 0)
{
    var selectedPair = FolderComparisonResult.FilePairResults[SelectedPairIndex];
    
    @if (selectedPair.HasError)
    {
        <ErrorDetailView SelectedPair="@selectedPair"
                         RawFileDifferences="@FolderRawFileDifferences" />
    }
    else if (DifferenceSummary != null)
    {
        <DetailedDifferencesView ... />
    }
}
```

**Also update `SelectPairResult()`** to compute raw file diffs when the selected pair has an error:
```csharp
private List<RawTextDifference>? FolderRawFileDifferences;

private async Task SelectPairResult(int index)
{
    // ... existing code ...
    
    if (selectedPair.HasError)
    {
        // Compute raw file diff for error detail view
        FolderRawFileDifferences = await RawTextComparisonService.CompareFilesRawAsync(
            /* file1Path */, /* file2Path */, ct);
    }
    else
    {
        FolderRawFileDifferences = null;
    }
}
```

**Note:** We need access to the original file paths. Currently `FilePairComparisonResult` only stores `File1Name` / `File2Name` (filenames, not full paths). Two options:

- **Option A (Preferred):** Add `File1Path` and `File2Path` properties to `FilePairComparisonResult` so the full paths are available at the UI layer. These are already known at the point where error results are created in `DirectoryComparisonService` and `HighPerformanceComparisonPipeline`.
- **Option B:** Reconstruct paths from the directory paths stored in the comparison config + the relative filenames. More brittle.

**Estimate:** ~30 lines changed in Home.razor + ~5 lines new field/property.

---

### Task 4: Add `File1Path` / `File2Path` to `FilePairComparisonResult`

**File:** `ComparisonTool.Core/Comparison/Results/FilePairComparisonResult.cs`

Add:
```csharp
/// <summary>
/// Gets or sets the full path to file 1. Used for raw file preview on error.
/// Null for request comparison results (which use response paths on the execution result).
/// </summary>
public string? File1Path { get; set; }

/// <summary>
/// Gets or sets the full path to file 2. Used for raw file preview on error.
/// </summary>
public string? File2Path { get; set; }
```

**Then populate these in all error-result creation sites:**

| File | Location |
|---|---|
| `DirectoryComparisonService.CompareDirectoriesAsync` | Error catch block (~line 187) |
| `DirectoryComparisonService.CompareFolderUploadsAsync` | Error catch block (~line 499) |
| `HighPerformanceComparisonPipeline.RunDeserializationStageAsync` | Error `DeserializedFilePair` creation (~line 252) |
| `HighPerformanceComparisonPipeline.RunComparisonStageAsync` | Error result creation (~line 336) |

Also populate on **success** results so it's consistently available (useful for future features like "open in editor"):

| File | Location |
|---|---|
| `DirectoryComparisonService.CompareDirectoriesAsync` | Success result creation (~line 164) |
| `DirectoryComparisonService.CompareFolderUploadsAsync` | Success result creation (~line 466) |
| `HighPerformanceComparisonPipeline.RunComparisonStageAsync` | Success result creation (~line 322) |

**Estimate:** 2 new properties + ~14 lines of assignments across 7 sites.

---

### Task 5: Register `RawTextComparisonService` for DI availability in Home.razor

**File:** `ComparisonTool.Web/Program.cs`

`RawTextComparisonService` is already registered as a singleton (added in the Non-Success Response Comparison feature). Just need to inject it in Home.razor:

**File:** `ComparisonTool.Web/Components/Pages/Home.razor`

Add at the top with other `@inject` directives:
```razor
@inject RawTextComparisonService RawTextComparisonService
```

**Estimate:** 1 line.

---

### Task 6: Unit tests

**File:** `ComparisonTool.Tests/Unit/RequestComparison/RawTextComparisonServiceTests.cs` (extend existing)

Add tests for the new `CompareFilesRawAsync` method:

| Test | Description |
|---|---|
| `CompareFilesRawAsync_IdenticalFiles_ReturnsNoDifferences` | Two files with same content → empty diff list |
| `CompareFilesRawAsync_DifferentFiles_ReturnsDifferences` | Two files with different content → non-empty diff list |
| `CompareFilesRawAsync_OneFileMissing_HandlesGracefully` | One path is null or nonexistent → returns appropriate diff entries |
| `CompareFilesRawAsync_BothFilesMissing_ReturnsEmpty` | Both paths null → empty or informational diff |
| `CompareFilesRawAsync_EmptyFiles_HandlesGracefully` | Both files exist but are empty |

**Estimate:** ~60 lines of new tests.

---

### Task 7: Build and run all tests

- `dotnet build ComparisonTool.Web/ComparisonTool.Web.csproj`
- `dotnet test ComparisonTool.Tests/ComparisonTool.Tests.csproj`
- Verify 0 failures.

---

## Task Summary

| # | Task | Files | Est. Lines |
|---|---|---|---|
| 1 | Add `CompareFilesRawAsync` method | `RawTextComparisonService.cs` | ~20 |
| 2 | Create `ErrorDetailView.razor` | NEW component | ~120 |
| 3 | Update `Home.razor` routing + SelectPairResult | `Home.razor` | ~35 |
| 4 | Add `File1Path`/`File2Path` to result model + populate | `FilePairComparisonResult.cs`, `DirectoryComparisonService.cs`, `HighPerformanceComparisonPipeline.cs` | ~20 |
| 5 | Inject `RawTextComparisonService` in Home.razor | `Home.razor` | ~1 |
| 6 | Unit tests for `CompareFilesRawAsync` | `RawTextComparisonServiceTests.cs` | ~60 |
| 7 | Build + run all tests | — | — |

**Execution order:** 4 → 1 → 2 → 5 → 3 → 6 → 7

Task 4 must come first because it provides the file paths needed by the UI. Task 1 provides the service method. Task 2 creates the component. Tasks 3 and 5 wire everything together. Task 6 validates.

## Risk & Edge Cases

- **Large files:** `ReadResponseBodyAsync` already truncates at 5 KB — safe.
- **Binary files:** UTF-8 decoding of binary content will produce garbled text. Acceptable for MVP; a future enhancement could detect binary content and show a hex preview or "binary file" notice.
- **Temp file cleanup:** For browser-uploaded folders, temp files may be cleaned up before the user clicks "View". The `CompareFilesRawAsync` method should handle missing files gracefully (it already does via the existing `ReadResponseBodyAsync` null/missing check).
- **Thread safety:** `RawTextComparisonService` is stateless and registered as singleton — safe for concurrent access.
- **No breaking changes:** The `File1Path`/`File2Path` properties are nullable and optional. Existing code that doesn't set them will simply have `null`, and the raw preview will gracefully degrade to "File not available".
