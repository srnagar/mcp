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
    /// Gets the hash of the original request parameters (excluding cursor).
    /// Used to validate that subsequent requests match the original query.
    /// </summary>
    public required string RequestHash { get; init; }

    /// <summary>
    /// Gets or sets the backend-specific continuation state for fetching the next page.
    /// Restricted to known continuation types (ContinuationToken, Offset, SkipToken, NextLink)
    /// to prevent arbitrary data from being stored in cursor entries.
    /// </summary>
    public required ContinuationState ContinuationState { get; set; }

    /// <summary>
    /// Gets the timestamp when this cursor was created. Used for diagnostics.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
