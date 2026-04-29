# Pagination Design for Azure MCP Tools

## Overview

This document describes the cursor-based pagination framework for Azure MCP tools that return collections. The design introduces an opaque, session-scoped cursor mechanism that works across all backend types (Azure Resource Graph, ARM SDK, REST APIs, data-plane SDKs).

## Request / Response Schema

### Request

Every paginated tool accepts an optional `cursor` parameter alongside its existing parameters:

```json
{
  "subscription": "my-sub",
  "resourceGroup": "my-rg",
  "cursor": null
}
```

- **First page**: `cursor` is `null` or omitted
- **Subsequent pages**: `cursor` is the opaque string from the previous response's `pagination.nextCursor`

When `cursor` is provided, the server validates that the cursor was issued for the same tool, session, and request parameters before using it.

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

    Note over Client,Azure: First Page Request (cursor = null)
    Client->>Server: CallTool(args: { subscription, resourceGroup, cursor: null })
    Server->>Server: ComputeRequestHash(args)
    Server->>Server: ResolveCursorAsync(null) → no cursor entry
    Server->>Azure: Fetch page (pageSize items, skip=0)
    Azure-->>Server: Page 1 results (truncated flag or continuation token)
    alt More pages available
        Server->>Server: Construct continuationState from response
        Server->>Cache: CreateAsync(toolName, sessionId, requestHash, continuationState)
        Note over Cache: cursorId = new GUID
        Cache-->>Server: cursorId = "a1b2c3d4..."
        Server-->>Client: { items: [...], pagination: { nextCursor: "a1b2c3d4...", pageSize: 50 } }
    else No more pages
        Server-->>Client: { items: [...], pagination: { nextCursor: null, pageSize: 50 } }
    end

    Note over Client,Azure: Subsequent Page Request
    Client->>Server: CallTool(args: { subscription, resourceGroup, cursor: "a1b2c3d4..." })
    Server->>Server: ComputeRequestHash(args)
    Server->>Cache: ResolveCursorAsync → GetAsync("a1b2c3d4...", toolName, sessionId, requestHash)
    Note over Cache: Validates toolName + requestHash match stored entry
    Cache-->>Server: PaginationCursorEntry (validated)
    Server->>Server: Extract continuation state (offset, token, nextLink)
    Server->>Azure: Fetch page using continuation state
    Azure-->>Server: Page N results (truncated flag or continuation token)
    alt More pages available
        Server->>Server: Construct new continuationState from response
        Server->>Cache: CreateAsync(toolName, sessionId, requestHash, newContinuationState)
        Note over Cache: cursorId = new GUID
        Cache-->>Server: cursorId = "e5f6g7h8..."
        Server-->>Client: { items: [...], pagination: { nextCursor: "e5f6g7h8...", pageSize: 50 } }
    else No more pages
        Server-->>Client: { items: [...], pagination: { nextCursor: null, pageSize: 50 } }
    end
```

## Cursor Registry Flow

```mermaid
flowchart TD
    A[Tool ExecuteAsync] -->|Receives request| B[ComputeRequestHash from args]
    B --> C{cursor<br/>provided?}
    C -->|No| D[ResolveCursorAsync returns null]
    D --> E[Call Azure service<br/>skip=0, limit=pageSize]

    C -->|Yes| F[ResolveCursorAsync calls<br/>CursorRegistry.GetAsync<br/>validates: toolName, sessionId,<br/>requestHash match]
    F -->|Valid| G[Extract continuation state<br/>from cursor entry]
    G --> H[Call Azure service<br/>using continuation state]

    F -->|Invalid/Expired| P[Throw ArgumentException:<br/>invalid or expired cursor]

    E --> I[Get results from Azure]
    H --> I

    I --> J{More results<br/>available?}
    J -->|Yes| K[Construct continuationState<br/>from Azure response]
    K --> L[CursorRegistry.CreateAsync<br/>generates opaque GUID cursor]
    L --> M[Return results +<br/>pagination.nextCursor]

    J -->|No| N[Return results +<br/>pagination.nextCursor = null]
```

## Cache Architecture

```mermaid
flowchart LR
    subgraph GeneralCache["ICacheService (general)"]
        direction TB
        G1[subscriptions<br/>TTL: 10 min]
        G2[resourceGroups<br/>TTL: 24 hr]
    end

    subgraph PaginationStore["PaginationCursorCache (dedicated)"]
        direction TB
        G3[cursor entries<br/>TTL: 1 hr configurable]
    end

    R[PaginationCursorRegistry] --> G3
    S[SubscriptionService] --> G1
    RG[ResourceGroupService] --> G2
```

Pagination cursors are stored in a dedicated `PaginationCursorCache` backed by `ConcurrentDictionary` with absolute TTL expiration. This cache is fully independent of the general `ICacheService` used by subscription and resource group services.

## Cursor Entry Structure

Each cursor entry in the registry contains:

| Field | Type | Purpose |
|---|---|---|
| `ToolName` | `string` | Tool that created the cursor (e.g., `azmcp_acr_registry_list`) |
| `SessionId` | `string` | User/session scope for multi-user security |
| `RequestHash` | `string` | SHA256 hash of request parameters (excluding `cursor`); validated on retrieval to ensure consistency |
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

> Returns up to {pageSize} items per request. If `pagination.nextCursor` is non-null in the response, more results are available. To fetch the next page, call this tool again with the same parameters and pass the returned `nextCursor` value as the `cursor` parameter. Always confirm with the user before fetching additional pages.

## Configuration

Pagination behavior is configured via `PaginationOptions`:

| Setting | Default | Description |
|---|---|---|
| `DefaultPageSize` | 50 | Number of items per page when tool doesn't specify its own |
| `CursorTimeToLive` | 1 hour | How long a cursor remains valid in cache |

## Security Considerations

- **Session scoping**: Cursors are scoped to a session/user identity, preventing cross-user cursor reuse in HTTP mode
- **Request hash validation**: On each cursor use, the server verifies the request parameters match the original request (excluding `cursor`), preventing parameter manipulation
- **TTL expiry**: Cursors automatically expire after the configured TTL, preventing stale data access
- **Opaque IDs**: Cursor IDs are opaque GUIDs that reveal no information about internal state
