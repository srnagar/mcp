# Pagination Design for Azure MCP Tools

## Overview

This document describes the cursor-based pagination framework for Azure MCP tools that return collections. The design introduces an opaque, session-scoped cursor mechanism that works across all backend types (Azure Resource Graph, ARM SDK, REST APIs, data-plane SDKs).

## Request / Response Schema

### Request

Every paginated tool accepts an optional `nextCursor` parameter alongside its existing parameters:

```json
{
  "subscription": "my-sub",
  "resourceGroup": "my-rg",
  "nextCursor": null
}
```

- **First page**: `nextCursor` is `null` or omitted
- **Subsequent pages**: `nextCursor` is the opaque string returned from the previous response

When `nextCursor` is provided, the server validates that the cursor was issued for the same tool, session, and request parameters before using it.

### Response

The tool result includes a `pagination` section:

```json
{
  "status": 200,
  "results": {
    "items": [ ... ],
    "pagination": {
      "nextCursor": "c_abc123def456",
      "pageSize": 50
    }
  },
  "duration": 234
}
```

- `pagination.nextCursor`: Opaque cursor string if more results exist; `null` if this is the last page
- `pagination.pageSize`: Number of items requested per page

## Interaction Sequence

```mermaid
sequenceDiagram
    participant Client as MCP Client
    participant Server as MCP Server<br/>(Tool Command)
    participant Cache as Pagination<br/>Cursor Registry
    participant Azure as Azure Service

    Note over Client,Azure: First Page Request (nextCursor = null)
    Client->>Server: CallTool(args: { subscription, resourceGroup, nextCursor: null })
    Server->>Azure: Fetch first page (pageSize items)
    Azure-->>Server: Page 1 results + continuation state
    Server->>Cache: CreateAsync(toolName, sessionId, requestHash, continuationState)
    Note over Cache: cursorId = hash(requestHash + continuationState)
    Cache-->>Server: cursorId = "c_a1b2c3d4..."
    Server-->>Client: { items: [...], pagination: { nextCursor: "c_a1b2c3d4...", pageSize: 50 } }

    Note over Client,Azure: Page 2 Request
    Client->>Server: CallTool(args: { subscription, resourceGroup, nextCursor: "c_a1b2c3d4..." })
    Server->>Cache: GetAsync(cursorId, toolName, sessionId, requestHash)
    Cache-->>Server: continuationState (nextLink, offset, etc.)
    Server->>Azure: Fetch next page using continuation state
    Azure-->>Server: Page 2 results + new continuation state (or end)
    alt More pages available
        Server->>Cache: CreateAsync(toolName, sessionId, requestHash, newContinuationState)
        Note over Cache: New deterministic cursorId from new state
        Cache-->>Server: cursorId = "c_e5f6g7h8..."
        Server-->>Client: { items: [...], pagination: { nextCursor: "c_e5f6g7h8...", pageSize: 50 } }
    else No more pages
        Server-->>Client: { items: [...], pagination: { nextCursor: null, pageSize: 50 } }
    end

    Note over Client,Azure: Retry of Page 2 (idempotent — same cursor, same result)
    Client->>Server: CallTool(args: { subscription, resourceGroup, nextCursor: "c_a1b2c3d4..." })
    Server->>Cache: GetAsync("c_a1b2c3d4...", ...)
    Cache-->>Server: Same continuationState as before
    Server->>Azure: Fetch next page (same continuation state → same results)
    Azure-->>Server: Page 2 results + same new continuation state
    Server->>Cache: CreateAsync(..., sameContinuationState)
    Note over Cache: Same inputs → same deterministic cursorId "c_e5f6g7h8..."
    Cache-->>Server: cursorId = "c_e5f6g7h8..."
    Server-->>Client: { items: [...], pagination: { nextCursor: "c_e5f6g7h8...", pageSize: 50 } }
```

## Cursor Registry Flow

```mermaid
flowchart TD
    A[Tool ExecuteAsync] -->|Receives request| B{nextCursor<br/>provided?}
    B -->|No| C[Call Azure service<br/>for first page]
    C --> D[Get results +<br/>continuation state]
    D --> E{More results<br/>available?}
    E -->|Yes| F[CursorRegistry.CreateAsync<br/>deterministic ID from<br/>requestHash + continuationState]
    F --> G[Return results +<br/>pagination.nextCursor]
    E -->|No| H[Return results +<br/>pagination.nextCursor = null]

    B -->|Yes| I[CursorRegistry.GetAsync<br/>validates: toolName, sessionId,<br/>requestHash match]
    I -->|Valid| J[Extract continuation state<br/>nextLink / offset / skip / etc.]
    J --> K[Call Azure service<br/>with continuation state]
    K --> L[Get results +<br/>new continuation state]
    L --> M{More results?}
    M -->|Yes| F
    M -->|No| H

    I -->|Invalid/Expired| P[Return 400 error:<br/>invalid or expired cursor]
```

## Cache Architecture

```mermaid
flowchart LR
    subgraph ICacheService
        direction TB
        G1[Group: subscriptions<br/>TTL: 10 min]
        G2[Group: resourceGroups<br/>TTL: 24 hr]
        G3[Group: pagination<br/>TTL: 2 hr configurable]
    end

    R[PaginationCursorRegistry] --> G3
    S[SubscriptionService] --> G1
    RG[ResourceGroupService] --> G2

    G3 -->|ClearGroupAsync| X[Clear all cursors<br/>independently]
```

The pagination cache group is fully isolated from other cache groups. Calling `ClearGroupAsync("pagination")` removes all cursors without affecting subscription or resource group caches.

## Cursor Entry Structure

Each cursor entry in the registry contains:

| Field | Type | Purpose |
|---|---|---|
| `ToolName` | `string` | Tool that created the cursor (e.g., `azmcp_acr_registry_list`) |
| `SessionId` | `string` | User/session scope for multi-user security |
| `RequestHash` | `string` | SHA256 hash of request parameters (excluding `nextCursor`) to validate consistency |
| `ContinuationState` | `Dictionary<string, string>` | Backend-specific state (e.g., `nextLink`, `offset`, `skipToken`) |
| `CreatedAt` | `DateTimeOffset` | Timestamp for diagnostics |

The `ContinuationState` dictionary is intentionally generic to support all backend pagination mechanisms:

- **Resource Graph**: `{ "offset": "50" }` (KQL skip-based)
- **ARM SDK**: `{ "continuationToken": "..." }` (from `AsPages()`)
- **REST API**: `{ "nextLink": "https://..." }` (URL for next page)
- **Marketplace**: `{ "skipToken": "..." }` (OData `$skiptoken`)

## Tool Metadata

Paginated tools set `SupportsPagination = true` in their `ToolMetadata`:

```csharp
public override ToolMetadata Metadata => new()
{
    Destructive = false,
    ReadOnly = true,
    SupportsPagination = true
};
```

This is surfaced to MCP clients as a `PaginationHint` in the tool's `Meta` property.

## Tool Description Convention

Paginated tools append the following guidance to their description:

> Returns up to {pageSize} items per request. If `pagination.nextCursor` is non-null in the response, more results are available. To fetch the next page, call this tool again with the same parameters and the returned `nextCursor` value. Always confirm with the user before fetching additional pages.

## Configuration

Pagination behavior is configured via `PaginationOptions`:

| Setting | Default | Description |
|---|---|---|
| `DefaultPageSize` | 50 | Number of items per page when tool doesn't specify its own |
| `CursorTimeToLive` | 2 hours | How long a cursor remains valid in cache |

## Security Considerations

- **Session scoping**: Cursors are scoped to a session/user identity, preventing cross-user cursor reuse in HTTP mode
- **Request hash validation**: On each cursor use, the server verifies the request parameters match the original request (excluding `nextCursor`), preventing parameter manipulation
- **TTL expiry**: Cursors automatically expire after the configured TTL, preventing stale data access
- **Opaque IDs**: Cursor IDs are opaque GUIDs that reveal no information about internal state
