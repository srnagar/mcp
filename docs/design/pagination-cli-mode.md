# Pagination in Direct CLI Mode

## Overview

This document analyzes how the cursor-based pagination design (see [`pagination-design-proposal.md`](pagination-design-proposal.md)) behaves in **direct CLI mode** — where the user invokes `azmcp.exe` directly from the command line as a one-shot process (e.g., `azmcp.exe monitor workspace list`).

**Verdict: Pagination is disabled in direct CLI mode.** The `PaginationOptions.Enabled` flag defaults to `false` and is only set to `true` when the MCP server starts (stdio or HTTP transport). In CLI mode, paginated commands fall back to their original non-paginated behavior, returning all available results without any cursor or pagination metadata.

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
│  2. ServiceCollection built (PaginationOptions.Enabled=false)│
│  3. Command parsed via System.CommandLine                    │
│  4. ExecuteAsync() runs: calls non-paged service method      │
│  5. JSON response written to stdout (no pagination object)   │
│  6. Process exits                                            │
└──────────────────────────────────────────────────────────────┘
```

**Key characteristic:** Each `azmcp.exe` invocation is an independent, short-lived process. There is no persistent server, no long-running cache, and no way to preserve state between invocations. The `PaginationOptions.Enabled` flag remains `false` because `AddAzureMcpServer()` (which sets it to `true`) is only called in server mode.

---

## How Pagination Is Disabled

### The `Enabled` flag

`PaginationOptions.Enabled` defaults to `false`. When the MCP server starts via `ServiceStartCommand` (stdio or HTTP transport), `AddAzureMcpServer()` calls `PostConfigure<PaginationOptions>` to set `Enabled = true`. In direct CLI mode, `AddAzureMcpServer()` is never called, so `Enabled` stays `false`.

### Command behavior when disabled

When `PaginationOptions.Enabled` is `false`, paginated commands:

1. **Call the original non-paged service methods** — e.g., `GetTopicsAsync()` instead of `GetTopicsPagedAsync()`, returning all available results.
2. **Skip cursor resolution** — the `--cursor` option is ignored and no cursor lookup occurs.
3. **Omit pagination from the response** — `NextCursor` is set to `null` and excluded from JSON output via `JsonIgnoreCondition.WhenWritingNull`.

### What the user sees

```powershell
PS> azmcp.exe acr registry list --subscription my-sub

{
  "status": 200,
  "results": {
    "registries": [ ... all registries ... ]
  },
  "duration": 1234
}
```

Note: No `pagination` object in the response. The user gets a clean result with all available items.

---

## Why Cursor-Based Pagination Cannot Work in Direct CLI Mode

Even if pagination were not disabled, cursors would fail because:

1. **Cursors are in-memory only.** The `PaginationCursorCache` uses a `ConcurrentDictionary` that is destroyed when the process exits.
2. **Each invocation is a fresh process.** A cursor returned in response 1 references state that no longer exists when invocation 2 starts.
3. **There is no external persistence.** The design intentionally avoids file-based or distributed cursor storage.

This is why the `Enabled` flag exists — to prevent commands from creating cursors that can never be consumed.

---

## Contrast: Direct CLI Mode vs. MCP Server Mode (stdio)

| Aspect | Direct CLI mode (`azmcp.exe <cmd>`) | MCP Server mode (stdio transport) |
|---|---|---|
| **Process lifetime** | Seconds (one-shot) | Hours (persistent child process) |
| **PaginationOptions.Enabled** | `false` (default) | `true` (set by `AddAzureMcpServer`) |
| **Service methods called** | Non-paged (returns all results) | Paged (returns `pageSize` items) |
| **Pagination in response** | Omitted | Included (`nextCursor`) |
| **Cursor persistence** | N/A (no cursors created) | ✅ Works (same process across calls) |
| **Who calls the tool** | Human user in terminal | LLM agent via JSON-RPC |

---

## Can Users Control Result Sets in Direct CLI Mode Today?

**No — there is no general-purpose mechanism for controlling result set size or paging through results in direct CLI mode.** Most list commands fetch the entire result set from Azure and return it in a single JSON response.

A small number of tools have **ad-hoc, tool-specific** mechanisms for limiting results:

| Tool | Mechanism | Option | Notes |
|---|---|---|---|
| **Workbooks** (`workbooks list`) | `--max-results` | User-facing option (default: 50, max: 1000) | Appends `\| limit N` to the Resource Graph KQL query. |
| **Monitor activity log** (`monitor activitylog list`) | `--limit` | User-facing option | Limits the number of activity log entries returned. |
| **Kusto** (`kusto database/table list`) | `--limit` | User-facing option | Limits results from Kusto `.show` commands. |
| **Deploy** (`deploy logs`) | `--limit` | User-facing option (default: 200) | Limits the number of log rows retrieved. |

These are not standardized and are not available on most commands.

---

## Potential Future Enhancements

If finer-grained result control in direct CLI mode is desired:

1. **`--top` / `--limit` flags** as a simpler mechanism for CLI users to cap result sizes.
2. **`--skip` / `--offset` flags** for backends that support stateless offset-based queries (e.g., Resource Graph).
3. **File-based cursor persistence** (e.g., `~/.azmcp/cursors/<id>.json`) — adds complexity but enables cross-invocation pagination.

---

## Summary

| Question | Answer |
|---|---|
| Is pagination enabled in direct CLI mode? | **No** — `PaginationOptions.Enabled` defaults to `false`. |
| What do paginated commands return in CLI mode? | All available results, with no `pagination` object in the response. |
| Is the `--cursor` option functional in CLI mode? | No — it is registered but not processed when `Enabled` is `false`. |
| Is this a bug? | **No** — the design intentionally targets MCP server mode (long-lived process). |
| How is this controlled? | `AddAzureMcpServer()` sets `Enabled = true` via `PostConfigure`. Only called in server mode. |
