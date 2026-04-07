// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Mcp.Core.Models.Pagination;

/// <summary>
/// Represents a cached pagination cursor entry containing the state needed to fetch the next page.
/// Stored in the pagination cache registry and validated against incoming requests.
/// </summary>
public sealed class PaginationCursorEntry
{
    /// <summary>
    /// Gets the name of the tool that created this cursor.
    /// Used to validate that cursor reuse matches the originating tool.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Gets the session or user identity that owns this cursor.
    /// Prevents cross-user cursor reuse in multi-user HTTP mode.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the hash of the original request parameters (excluding nextCursor).
    /// Used to validate that subsequent requests match the original query.
    /// </summary>
    public required string RequestHash { get; init; }

    /// <summary>
    /// Gets or sets the backend-specific continuation state.
    /// Keys and values depend on the pagination backend:
    /// Resource Graph: { "offset": "50" },
    /// ARM SDK: { "continuationToken": "..." },
    /// REST API: { "nextLink": "https://..." },
    /// Marketplace: { "skipToken": "..." }.
    /// </summary>
    public required Dictionary<string, string> ContinuationState { get; set; }

    /// <summary>
    /// Gets the timestamp when this cursor was created. Used for diagnostics.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
