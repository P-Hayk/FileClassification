# FileClassification

A .NET 9 distributed system that accepts `.txt` file uploads, stores them in PostgreSQL using the Large Object API, and classifies each file's primary language (Armenian, Russian, English, or Unknown) via a background worker.

## Architecture

The solution follows Clean Architecture and is split into four projects:

```
FileClassification.API          – ASP.NET Core REST API
FileClassification.Worker       – .NET background worker service
FileClassification.Application  – Domain logic, CQRS, interfaces
FileClassification.Infrastructure – EF Core, Npgsql, PostgreSQL LO storage
```

### Component overview

```
┌─────────────┐    HTTP     ┌─────────────────────┐
│   Client    │ ──────────► │  FileClassification  │
│ (Browser /  │             │       .API           │
│   Postman)  │             │  POST /api/files     │
└─────────────┘             │  GET  /api/files     │
                            │  GET  /api/files/{id}│
                            │  PATCH ../cancel     │
                            │  PATCH ../resume     │
                            │  DELETE /api/files   │
                            └──────────┬──────────┘
                                       │ EF Core / Npgsql
                            ┌──────────▼──────────┐
                            │     PostgreSQL       │
                            │  Files table         │
                            │  Large Objects (LO)  │
                            └──────────▲──────────┘
                                       │ EF Core / Npgsql
                            ┌──────────┴──────────┐
                            │  FileClassification  │
                            │      .Worker         │
                            │                      │
                            │  FileProcessingWorker│
                            │  StalledFileReset-   │
                            │       Worker         │
                            └─────────────────────┘
```

### File state machine

```
            Upload
              │
              ▼
          ┌─────────┐
          │ Pending │◄─────────────────────────┐
          └────┬────┘                           │
               │ Worker claims                  │ StalledFileResetWorker
               ▼                                │ (no heartbeat > timeout)
          ┌──────────┐   Cancel   ┌──────────┐  │
          │Processing│ ─────────► │ Inactive │  │
          └────┬─────┘            └────┬─────┘  │
               │                       │ Resume  │
          ┌────┴────┐                  └─────────┘
          │         │
      Success    Failure
          │         │
          ▼         ▼
     ┌─────────┐ ┌────────┐
     │Completed│ │ Failed │
     └─────────┘ └────────┘
```

## Projects

### FileClassification.API

ASP.NET Core Web API exposing file management endpoints. Uses **MediatR** to dispatch commands and queries to the Application layer.

| Method | Route | Description |
|--------|-------|-------------|
| `POST` | `/api/files` | Upload a `.txt` file for classification |
| `GET` | `/api/files` | List all files with their current state |
| `GET` | `/api/files/{id}` | Get a single file record |
| `PATCH` | `/api/files/{id}/cancel` | Cancel a file that is currently processing |
| `PATCH` | `/api/files/{id}/resume` | Re-queue a cancelled (Inactive) file |
| `DELETE` | `/api/files/{id}` | Delete a file (must not be Processing) |

Interactive API docs are available at `/scalar/v1` (powered by Scalar).

### FileClassification.Worker

.NET Generic Host background service. Contains two hosted workers:

**`FileProcessingWorker`** polls the database for `Pending` files, claims them atomically, and processes them concurrently up to `ConcurrencyLimit`. For each file it:
1. Opens a read stream to the PostgreSQL Large Object.
2. Runs the language classifier, reporting progress as it reads.
3. Writes a heartbeat to the database on a fixed interval.
4. Finalizes the record with `Completed` or `Failed` state and classification result.
5. If the heartbeat detects the file is no longer active (externally cancelled), it stops processing without writing a final state.

**`StalledFileResetWorker`** runs on the same poll interval and resets any file stuck in `Processing` state whose last heartbeat is older than `HeartbeatTimeoutSeconds` back to `Pending`.

### FileClassification.Application

Pure domain/application layer with no infrastructure dependencies.

- **CQRS** via MediatR: commands (`UploadFile`, `CancelFile`, `ResumeFile`, `DeleteFile`) and queries (`GetAllFiles`, `GetFileById`).
- **`LanguageDetector`**: character-range scanner that counts Armenian (U+0531–U+0587), Russian (Cyrillic U+0410–U+044F + Ё/ё), and English (A–Z, a–z) characters in a streaming pass. Reports the dominant language and its percentage share of classified characters. Returns `Unknown` if classified characters fall below `MinLanguageRatio`.
- **`FileProcessingService`**: thin wrapper that calls the classifier and maps the result to a `ProcessingResult`.
- **`FileWorkerService`**: thin wrapper delegating worker-side repository calls (claim, heartbeat, finalize, reset-stalled).
- **`LoggingBehavior`**: MediatR pipeline behavior that logs every command/query.

### FileClassification.Infrastructure

- **`AppDbContext`** (EF Core + Npgsql): maps `FileRecord` to a `Files` table. `State` and `Language` columns stored as strings; `DataOid` stored as PostgreSQL `oid`.
- **`FileRepository`**: all database operations, including Large Object management (create on upload, open-read on processing, unlink on delete). Worker claim uses an optimistic row-level update (`WHERE State = 'Pending'`) so multiple worker replicas cannot double-process the same file.
- **`LargeObjectReadStream`**: wraps a Npgsql Large Object stream together with its owning connection and transaction, ensuring proper cleanup when the caller disposes the stream.

## Language detection

The classifier makes a single streaming pass over the file content using a `char[24576]` buffer. For each character it increments one of three language counters based on Unicode ranges:

| Language | Unicode ranges |
|----------|---------------|
| Armenian | U+0531–U+0556 (uppercase), U+0561–U+0587 (lowercase) |
| Russian | U+0410–U+044F (Cyrillic), U+0401 Ё, U+0451 ё |
| English | A–Z, a–z |

The dominant language is chosen by the highest count. If the total classified characters divided by total characters is below `MinLanguageRatio` (default `0.1`), the file is classified as `Unknown`.

The confidence score is the dominant language's character count as a percentage of all classified characters.

## Getting started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) and Docker Compose

### Run with Docker Compose

```bash
docker compose up --build
```

This starts:
- `postgres` — PostgreSQL 17 on port `5432`
- `api` — REST API on port `8080`
- `worker` — background classification worker
- `frontend` — React UI on port `80` (built from `../../ReactProjects/folder-uploader`)

The API is available at `http://localhost:8080`.  
API docs (Scalar) are at `http://localhost:8080/scalar/v1`.

### Run locally (without Docker)

1. Start a local PostgreSQL instance and create a database named `fileclassification`.
2. Update the connection string in `FileClassification.API/appsettings.Development.json` and `FileClassification.Worker/appsettings.Development.json` if needed.
3. Run the API:
   ```bash
   dotnet run --project FileClassification.API
   ```
4. Run the worker in a second terminal:
   ```bash
   dotnet run --project FileClassification.Worker
   ```

The database schema is created automatically on API startup via `EnsureCreated`.

## Configuration

### API — `appsettings.json`

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:DefaultConnection` | `localhost` | PostgreSQL connection string |
| `UploadLimits:MaxRequestBodySizeBytes` | `524288000` (500 MB) | Maximum upload size |
| `LanguageDetector:MinLanguageRatio` | `0.1` | Minimum fraction of classified characters required to assign a language |

### Worker — `appsettings.json`

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:DefaultConnection` | `localhost` | PostgreSQL connection string |
| `WorkerSettings:ConcurrencyLimit` | `3` | Max files processed in parallel per worker instance |
| `WorkerSettings:PollIntervalSeconds` | `10` | How often the worker polls for new files |
| `WorkerSettings:ProgressUpdateIntervalSeconds` | `10` | Heartbeat interval |
| `WorkerSettings:HeartbeatTimeoutSeconds` | `20` | Seconds without a heartbeat before a file is considered stalled |

## Horizontal scaling

Multiple worker instances can run concurrently against the same database. The `ClaimPendingAsync` logic issues a per-row `UPDATE … WHERE State = 'Pending'` check so only one worker wins the claim for each file. The `StalledFileResetWorker` in each instance will independently reset files whose worker has died.

## Logging

Both the API and the Worker use **Serilog** configured to write to the console and to a rolling daily log file at `logs/log-<date>.txt` (last 7 days retained).
