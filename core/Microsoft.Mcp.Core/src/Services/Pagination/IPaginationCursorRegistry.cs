// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Mcp.Core.Models.Pagination;

namespace Microsoft.Mcp.Core.Services.Pagination;

/// <summary>
/// Manages opaque pagination cursors that map to backend-specific continuation state.
/// Cursors are validated against the originating tool and request parameters.
/// Cursor IDs are opaque GUIDs — on retrieval, the registry validates that the incoming request
/// matches the stored tool name and request hash.
/// </summary>
public interface IPaginationCursorRegistry
{
    /// <summary>
    /// Creates a new pagination cursor and stores the continuation state.
    /// Each call generates a new opaque GUID cursor ID.
    /// </summary>
    /// <param name="toolName">The name of the tool creating the cursor.</param>
    /// <param name="requestHash">Hash of the request parameters (excluding cursor).</param>
    /// <param name="continuationState">Backend-specific state for fetching the next page.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An opaque cursor ID string.</returns>
    ValueTask<string> CreateAsync(
        string toolName,
        string requestHash,
        ContinuationState continuationState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves and validates a pagination cursor. Returns the cursor entry only if the
    /// tool name and request hash match the stored values.
    /// </summary>
    /// <param name="cursorId">The opaque cursor ID to look up.</param>
    /// <param name="toolName">The expected tool name (must match stored value).</param>
    /// <param name="requestHash">The expected request hash (must match stored value).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The cursor entry if valid, or <see langword="null"/> if not found or validation fails.</returns>
    ValueTask<PaginationCursorEntry?> GetAsync(
        string cursorId,
        string toolName,
        string requestHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a pagination cursor.
    /// </summary>
    /// <param name="cursorId">The cursor ID to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if the cursor was found and deleted; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> DeleteAsync(
        string cursorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all pagination cursors across all sessions.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask ClearAllAsync(CancellationToken cancellationToken = default);
}
