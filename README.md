# FileClassification

A small .NET 9 service that ingests `.txt` files and classifies each one by its dominant
language — Armenian, Russian, English, or Unknown. Files are uploaded over HTTP, stored
in PostgreSQL as Large Objects, and processed by a background worker. There's no
filesystem and no blob storage; the database is the only persistence layer.

## Run it

```bash
docker compose up --build
```

This starts Postgres, the API on `:8080`, and the worker. API docs (Scalar) at
`http://localhost:8080/scalar/v1` in Development.

Locally without Docker:

```bash
dotnet run --project FileClassification.API
dotnet run --project FileClassification.Worker
```

A Postgres on `localhost:5432` with a database called `fileclassification` is enough;
schema is created on first start via `EnsureCreated` (this would be a migration in a
real deployment).

## Endpoints

| Method   | Route                       | Notes                                          |
|----------|-----------------------------|------------------------------------------------|
| `POST`   | `/api/files`                | Upload one `.txt` (multipart). Returns `{id}`. |
| `GET`    | `/api/files`                | List everything with current state + progress. |
| `GET`    | `/api/files/{id}`           | Single record.                                 |
| `PATCH`  | `/api/files/{id}/cancel`    | Only valid while `Processing`.                 |
| `PATCH`  | `/api/files/{id}/resume`    | Re-queue a `Inactive` or `Failed` file.        |
| `DELETE` | `/api/files/{id}`           | Anything except `Processing`.                  |

## How it works

The API just persists. On upload it opens a transaction, creates a PostgreSQL Large
Object, streams the request body into it, inserts the `Files` row referencing the
`oid`, and commits. The HTTP request never buffers the whole file in memory.

The worker polls `Files WHERE State='Pending'` and claims rows atomically using a
single `UPDATE ... WHERE Id IN (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING *` — so
multiple worker replicas can run side-by-side without stepping on each other and
without taking a table-level lock. Each claimed file is read back out of its Large
Object as a stream and fed to `LanguageDetector`, which counts characters in three
Unicode ranges in a single pass. The dominant language and its share of classified
characters becomes the result.

While a file is being classified the worker writes a heartbeat (the current progress
percent) every few seconds. The heartbeat doubles as a cancellation channel: if the
heartbeat UPDATE affects zero rows the file's state must have been changed externally
(usually by `PATCH /cancel`), and the processing task cancels itself. A separate
`StalledFileResetWorker` sweeps any row that's been in `Processing` longer than the
heartbeat timeout back to `Pending`, so a crashed worker can't strand files.

## Performance notes

The whole pipeline is built around streaming, so the same code path works for a 1 KB
text file and a 500 MB one. Uploads stream from the request body straight into the
Large Object write; classification streams from the Large Object read straight
through `StreamReader`. Nothing ever buffers a full file in memory.

Workers claim their next batch in a single statement —
`UPDATE … WHERE Id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING *`. Locked rows
get skipped rather than waited on, so multiple worker replicas can grind through the
queue side-by-side without taking a table-level lock and without a SELECT/UPDATE
race. All state transitions (cancel, resume, heartbeat, finalise, stalled-reset) are
single statements via `ExecuteUpdateAsync`; queries use `AsNoTracking`. Between those
two, EF never sits in the hot path.

Both the API and the worker share a singleton `NpgsqlDataSource`, which is also how
the worker gets the dedicated connections it holds open while reading from a Large
Object — so even the long-lived read paths are pooled.

The classifier's inner loop rents a 16K-char buffer from `ArrayPool<char>`, iterates
over a `ReadOnlySpan<char>` slice to drop bounds-checks, branches on three contiguous
Unicode ranges (English first, since that's by far the most common path on real text),
and only reports a progress update when the rounded percent actually changes. That
last detail matters on a 500 MB file: without it, `IProgress<T>` gets called tens of
thousands of times per second for no UI benefit.

Worker concurrency per process is a `SemaphoreSlim` (default 4), and multiple worker
processes can run side-by-side — the `SKIP LOCKED` claim keeps that race-free.

What isn't here: there's no SignalR / WebSocket / SSE push. The UI polls
`GET /api/files` while anything is active and stops when nothing is. With pagination
on the API this scales fine; if it stopped scaling, `LISTEN/NOTIFY` or a SignalR hub
would be the next move.

## Configuration

Both services read connection strings, log sinks, and per-feature settings from their
`appsettings.json`. The knobs worth knowing:

- `UploadLimits:MaxRequestBodySizeBytes` (API) — default 500 MB.
- `LanguageDetector:MinLanguageRatio` (API + worker) — minimum share of classified
  characters required to assign a language; below this the result is `Unknown`.
- `WorkerSettings:ConcurrencyLimit` — parallel files per worker process. Default 4.
- `WorkerSettings:PollIntervalSeconds` — how often the worker polls for new work and
  how often the stalled-file sweeper runs. Default 2.
- `WorkerSettings:ProgressUpdateIntervalSeconds` — heartbeat cadence. Default 3.
- `WorkerSettings:HeartbeatTimeoutSeconds` — how long a heartbeat may go missing before
  the file is considered stalled. Default 30.

## Tests

Two projects under `tests/`:

- `FileClassification.UnitTests` — fast, no infra. CQRS handlers, the language
  detector, the processing service.
- `FileClassification.IntegrationTests` — spins up real PostgreSQL via
  [Testcontainers](https://dotnet.testcontainers.org/) and exercises `FileRepository`
  against it: LO round-trip, state transitions, and a concurrency test that proves the
  `SKIP LOCKED` claim actually partitions rows between simultaneous workers without
  double-claiming.

```bash
dotnet test                                                    # everything
dotnet test tests/FileClassification.UnitTests                 # unit only
dotnet test tests/FileClassification.IntegrationTests          # needs Docker
```

## What's not in here

Deliberately scoped out for this submission:

- No authentication, no rate limiting, no CORS configuration.
- `EnsureCreated` instead of EF migrations.
- Scalar docs are Development-only.
- No SignalR / SSE; the UI polls a single list endpoint.
