# High-Level Architecture Design Document

**Project:** Open-Source Local A/B Comparison Tool

**Stack:** .NET / Blazor (Hybrid or Local Server)

**Storage Architecture:** Local Flat-File Workspace Pattern

---

## 1. System Overview & Objectives

This document defines the high-level architecture for a local-first, open-source A/B comparison tool. The application is designed to run entirely on a user's local machine, executing concurrent HTTP requests against two target endpoints (A and B), evaluating response differences, and generating local reports.

### Core Architectural Drivers

* **Zero Dependency:** No external database installations (e.g., SQL Server, PostgreSQL) or container runtimes (Docker) required.
* **Git-Friendly:** Test configurations must be easily sharable and trackable via standard version control systems.
* **Resource-Constrained Concurrency:** The system must maximize HTTP request/processing throughput without freezing the host machine's UI or crashing due to memory bloat.

---

## 2. Architectural Blueprint

The application employs an **In-Process Decoupled Architecture**. Instead of physical microservices separated by networks, components are isolated logically using native .NET memory channels and dependency injection.

### Component Breakdown

#### A. Presentation Layer (Blazor Core)

* **Role:** Manages user configuration input, state visualization during active runs, and interactive diff-reporting dashboards.
* **State Management:** On startup, reads project files into an in-memory collection. UI components bind directly to these C# objects, eliminating database query overhead.

#### B. In-Memory Pipeline (`System.Threading.Channels`)

* **Role:** Acts as the internal asynchronous broker (replacing the need for a queue engine like RabbitMQ or Redis).
* **Mechanism:** Provides a high-throughput, thread-safe Producer/Consumer channel. The Blazor UI produces "Request Tasks," and the worker pool consumes them.

#### C. Execution & Comparison Engine (Background Workers)

* **Role:** An array of `IHostedService` instances running on background threads.
* **Network Engine:** Utilizes a centrally configured `SocketsHttpHandler` to execute concurrent outbound HTTP requests to Target A and Target B while mitigating local socket exhaustion.
* **Diffing Engine:** Uses a stream-based parsing strategy (e.g., `System.Text.Json` or text-diffing algorithms) to calculate structural and value deltas instantly without loading massive strings into memory.

#### D. Storage Layer (The File-System Workspace)

* **Role:** Replaces traditional relational databases by using the host machine's native directory structure.

---

## 3. Storage Hierarchy (Workspace Model)

Data is written to a dedicated, user-selected workspace directory. The file system structure is organized deterministically:

```text
📂 [User_Selected_Workspace_Root]/
├── 📄 .abproject                    # Workspace marker/metadata file
├── 📂 Configs/                     # Version-controlled test specifications
│   ├── production-api-audit.json    # Request parameters, headers, matching rules
│   └── checkout-v2-smoke.json
└── 📂 Runs/                        # Historical execution records
    ├── 📂 Run_20260630_150000/      # Unique timestamped directory per test run
    │   ├── 📄 summary.json          # Metrics: Aggregated latencies, success rates, diff counts
    │   ├── 📄 diff_req_001.json     # Detailed comparison report for request #1
    │   └── 📄 diff_req_002.json     # Detailed comparison report for request #2
    └── 📂 Run_20260630_164500/
        └── 📄 summary.json

```

---

## 4. Key Data Flows

### Execution Flow

1. **Initiation:** The user selects a configuration file and clicks "Run" in the Blazor UI.
2. **Initialization:** The Engine reads the configuration, provisions a new timestamped directory under `Runs/`, and generates a `CancellationToken`.
3. **Queueing:** The Engine pushes individual target URLs/payloads into the `System.Threading.Channel`.
4. **Processing:** Multiple background workers pull from the channel concurrently:
* Fire HTTP request to Target A.
* Fire HTTP request to Target B.
* Stream both responses into the comparison logic.
* Write individual `diff_req_XXX.json` files directly to disk.


5. **Finalization:** Once the channel is empty, the engine aggregates total execution times, error rates, and delta tallies, writing the final `summary.json` file.

### Reporting Flow

1. **Dashboard Load:** On application boot or history navigation, the Blazor app scans the `Runs/` directory.
2. **Lightweight Read:** The app reads **only** the `summary.json` files into memory.
3. **Rendering:** Blazor lists historical runs using standard LINQ expressions (`.OrderBy()`) on the in-memory summary collection.
4. **Lazy-Loading:** Detailed files (`diff_req_XXX.json`) are only opened and parsed if the user explicitly clicks a specific request row in the reporting UI.

---

## 5. Technical Constraints & Design Rules

* **Zero Memory Leaks:** Raw HTTP responses must never be saved as large strings (`string`). They must be evaluated directly via `Stream` contexts to protect local RAM during large test cycles.
* **UI Fluidity:** All disk I/O and network operations *must* utilize asynchronous execution (`System.IO.File.WriteAllTextAsync`, `HttpClient.SendAsync`) to prevent thread-blocking on the Blazor rendering loop.
* **Isolation:** The UI communicates with the core engine solely via events and channel triggers, allowing the core engine to be easily wrapped into a command-line interface (CLI) tool if needed in the future.