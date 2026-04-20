# Pagination in Direct CLI Mode

## Overview

This document analyzes how the cursor-based pagination design (see [`pagination-design-proposal.md`](pagination-design-proposal.md)) behaves in **direct CLI mode** — where the user invokes `azmcp.exe` directly from the command line as a one-shot process (e.g., `azmcp.exe monitor workspace list`).

**Verdict: Pagination does NOT work in direct CLI mode.** The cursor-based design is fundamentally incompatible with one-shot process invocations because cursors are stored in in-memory cache that is destroyed when the process exits.

---

## What Is Direct CLI Mode?

Direct CLI mode is when a user runs `azmcp.exe` as a standalone command-line tool:

```powershell
# Single command invocation — process starts, executes, prints JSON, exits
azmcp.exe acr registry list --subscription my-sub
azmcp.exe monitor workspace list --subscription my-sub --resource-group my-rg
```

Each invocation follows this lifecycle:

```
┌──────────────────────────────────────────────────────────────┐
│  azmcp.exe acr registry list --subscription my-sub           │
│                                                              │
│  1. Process starts                                           │
│  2. ServiceCollection built (IMemoryCache, CursorRegistry)   │
│  3. Command parsed via System.CommandLine                    │
│  4. ExecuteAsync() runs: fetches page 1, creates cursor      │
│  5. JSON response written to stdout                          │
│  6. Process exits → ALL IN-MEMORY STATE IS DESTROYED         │
└──────────────────────────────────────────────────────────────┘
```

**Key characteristic:** Each `azmcp.exe` invocation is an independent, short-lived process. There is no persistent server, no long-running cache, and no way to preserve state between invocations.

---

## Why Pagination Fails in Direct CLI Mode

### The fundamental problem

The pagination design stores cursor state in `SingleUserCliCacheService`, which is backed by `IMemoryCache` — an in-process, in-memory cache. When the `azmcp.exe` process exits after printing its response, the entire `IMemoryCache` (including all pagination cursors) is destroyed.

```
Invocation 1:
  azmcp.exe acr registry list --subscription my-sub
  → Returns: { pagination: { nextCursor: "c_abc123", pageSize: 5 } }
  → Cursor "c_abc123" stored in IMemoryCache
  → Process exits → IMemoryCache destroyed → cursor gone forever

Invocation 2:
  azmcp.exe acr registry list --subscription my-sub --next-cursor c_abc123
  → New process starts → fresh IMemoryCache (empty)
  → CursorRegistry.GetAsync("c_abc123") → returns null (not found)
  → Throws ArgumentException: "The pagination cursor is invalid, expired..."
  → Returns 400 error
```

### The cursor lifecycle breakdown

| Step | What happens | Works? |
|---|---|---|
| First page request (no cursor) | Tool fetches page 1, stores cursor in IMemoryCache, returns `nextCursor` | ✅ Response is valid |
| Next page request (with cursor) | New process starts, cursor registry is empty, lookup fails | ❌ **Always fails** |

The cursor value returned in the first response is **meaningless** — it refers to state that no longer exists anywhere.

---

## What the User Sees

```powershell
PS> azmcp.exe acr registry list --subscription my-sub

{
  "status": 200,
  "results": {
    "registries": [ ... 5 items ... ],
    "pagination": { "nextCursor": "c_a1b2c3d4e5f6", "pageSize": 5 }
  },
  "duration": 1234
}

PS> azmcp.exe acr registry list --subscription my-sub --next-cursor c_a1b2c3d4e5f6

{
  "status": 400,
  "message": "The pagination cursor is invalid, expired, or does not match the current request parameters. Please retry the request without a cursor to start from the first page. To mitigate this issue, please refer to the troubleshooting guidelines here at https://aka.ms/azmcp/troubleshooting.",
  "duration": 89
}
```

The user **can never page beyond the first result set** in direct CLI mode.

---

## Contrast: Direct CLI Mode vs. MCP Server Mode (stdio)

The pagination design was built for **MCP server mode**, where the server runs as a long-lived child process:

| Aspect | Direct CLI mode (`azmcp.exe <cmd>`) | MCP Server mode (stdio transport) |
|---|---|---|
| **Process lifetime** | Seconds (one-shot) | Hours (persistent child process) |
| **IMemoryCache lifetime** | Per-invocation (destroyed on exit) | Per-session (lives until client disconnects) |
| **Cursor persistence** | ❌ Impossible | ✅ Works (same process across calls) |
| **Who calls the tool** | Human user in terminal | LLM agent via JSON-RPC |
| **Multi-call coordination** | Manual (user types next command) | Automatic (agent holds cursor in context) |
| **Use case** | Quick lookups, scripting, debugging | Interactive AI-assisted workflows |

In MCP server mode, the server process stays alive between `tools/call` requests. The `IMemoryCache` and `PaginationCursorRegistry` persist in memory, so cursors created on page 1 are available when the agent requests page 2. This is the scenario pagination was designed for.

---

## Potential Solutions (Not Yet Implemented)

If pagination support in direct CLI mode is desired in the future, these approaches could work:

### Option 1: File-based cursor persistence

Write cursor state to a temporary file (e.g., `~/.azmcp/cursors/<cursor-id>.json`) instead of or in addition to IMemoryCache. Each process invocation reads/writes from disk.

**Pros:** Simple, no infrastructure, works across process invocations.
**Cons:** File cleanup complexity, potential stale data on disk, security of cursor files.

### Option 2: Offset-based stateless pagination

Instead of opaque cursors, accept explicit `--skip` and `--limit` parameters:

```powershell
azmcp.exe acr registry list --subscription my-sub --limit 5
azmcp.exe acr registry list --subscription my-sub --limit 5 --skip 5
azmcp.exe acr registry list --subscription my-sub --limit 5 --skip 10
```

**Pros:** Completely stateless, works naturally in CLI mode, user controls paging.
**Cons:** Only works for backends that support offset-based queries (Resource Graph). ARM SDK continuation tokens and REST API `nextLink` URLs require server-side state. Risk of item skipping/duplication if resources change between calls.

### Option 3: Fetch-all with client-side truncation

Return all results but add a `--max-results` or `--top` flag for the user to cap output:

```powershell
azmcp.exe acr registry list --subscription my-sub --top 10
```

**Pros:** Simple, no pagination state needed, user controls output size.
**Cons:** Still fetches all pages from Azure (latency/memory), just truncates output. Doesn't solve the "wasted work" problem for large result sets.

### Option 4: Hybrid approach

- If `--skip`/`--limit` are provided, use stateless offset-based paging (Resource Graph only).
- If running as MCP server (stdio), use cursor-based paging as designed.
- If running in direct CLI mode with no `--skip`, fetch and return all results (current pre-pagination behavior).

---

## Can Users Control Result Sets in Direct CLI Mode Today?

**No — there is no general-purpose mechanism for controlling result set size or paging through results in direct CLI mode.** The vast majority of list commands fetch the entire result set from Azure and return it in a single JSON response with no user-configurable limit.

### What exists today

A small number of tools have **ad-hoc, tool-specific** mechanisms for limiting results. These are not standardized and are not available on most commands:

| Tool | Mechanism | Option | Notes |
|---|---|---|---|
| **Workbooks** (`workbooks list`) | `--max-results` | User-facing option (default: 50, max: 1000) | Appends `\| limit N` to the Resource Graph KQL query. |
| **Monitor activity log** (`monitor activitylog list`) | `--limit` | User-facing option | Limits the number of activity log entries returned. |
| **Kusto** (`kusto database/table list`) | `--limit` | User-facing option | Limits results from Kusto `.show` commands. |
| **Deploy** (`deploy logs`) | `--limit` | User-facing option (default: 200) | Limits the number of log rows retrieved. |
| **Resource Graph commands** (12 tools) | Internal `limit`/`skip` params | **Not exposed to the user** | `BaseAzureResourceService.ExecuteResourceQueryAsync()` accepts `limit` and `skip` internally but individual tools hardcode `limit: 50` (or use the pagination default). Users cannot override these. |

### What most commands do

The majority of list commands (ARM SDK-based, data-plane SDK-based, REST API-based) **consume all pages from Azure and return everything**:

```csharp
// Typical pattern (e.g., CosmosListCommand, RedisResourceListCommand):
var accounts = await cosmosService.GetCosmosAccounts(subscription, tenant, retryPolicy, cancellationToken);
// Returns ALL accounts — no limit, no skip, no paging control
context.Response.Results = ResponseResult.Create(new(accounts ?? []), ...);
```

There is no `--top`, `--skip`, `--limit`, or `--page-size` option in the common `OptionDefinitions`. The only common options are `--subscription`, `--resource-group`, `--tenant`, `--next-cursor`, and retry-related options.

### Can users get "the first few items then request next few"?

**No.** In direct CLI mode:

1. **First call** returns the **entire** result set (all pages consumed from Azure). There is no way to say "give me just the first 10."
2. **There is no "next few"** because there is nothing left — everything was already returned.
3. The `--next-cursor` option exists but is **non-functional** in direct CLI mode (as explained above — cursors are lost when the process exits).
4. For the 3 paginated commands (`acr registry list`, `eventgrid topic list`, `monitor activitylog list`), page 1 is returned with a `nextCursor` value, but passing that cursor to a second invocation always fails.

### Summary of the gap

```
What users want:              What exists today:
─────────────────────         ──────────────────────────
azmcp acr list --top 5        ❌ No --top option
azmcp acr list --skip 5       ❌ No --skip option  
azmcp acr list --page 2       ❌ No --page option
azmcp acr list --limit 10     ❌ Not on most commands
azmcp acr list --next-cursor X ❌ Cursor lost between processes
```

---

## Recommendation

**Do not advertise pagination in direct CLI mode.** The current design correctly targets MCP server mode (stdio and HTTP transports) where the server is long-lived. For direct CLI mode:

1. **Suppress `nextCursor` in responses** when the tool detects it is running as a one-shot process (not inside an MCP server loop). Alternatively, accept that the cursor is informational-only.
2. **Consider `--top` / `--limit` flags** as a simpler mechanism for direct CLI users to control result size.
3. **Document clearly** that pagination (cursor-based) requires the MCP server transport (stdio or HTTP) and does not work in one-shot `azmcp.exe` invocations.

---

## Summary

| Question | Answer |
|---|---|
| Does cursor-based pagination work in direct CLI mode? | **No** — cursors are lost when the process exits. |
| Is this a bug? | **No** — the design targets MCP server mode (long-lived process). |
| Can direct CLI mode page at all? | Only the first page is returned. Subsequent pages always fail. |
| What would fix it? | File-based persistence, stateless `--skip`/`--limit`, or `--top` for truncation. |
| Should it be fixed? | Depends on whether direct CLI mode is a supported user scenario for paginated tools. |
