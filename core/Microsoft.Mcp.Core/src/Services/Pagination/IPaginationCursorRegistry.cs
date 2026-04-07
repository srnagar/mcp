// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Mcp.Core.Models.Pagination;

namespace Microsoft.Mcp.Core.Services.Pagination;

/// <summary>
/// Manages opaque pagination cursors that map to backend-specific continuation state.
/// Cursors are scoped to a session and validated against the originating tool and request parameters.
/// </summary>
public interface IPaginationCursorRegistry
{
    /// <summary>
    /// Creates a new pagination cursor and stores the continuation state.
    /// </summary>
    /// <param name="toolName">The name of the tool creating the cursor.</param>
    /// <param name="sessionId">The session or user identity that owns the cursor.</param>
    /// <param name="requestHash">Hash of the request parameters (excluding nextCursor).</param>
    /// <param name="continuationState">Backend-specific state for fetching the next page.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An opaque cursor ID string.</returns>
    ValueTask<string> CreateAsync(
        string toolName,
        string sessionId,
        string requestHash,
        Dictionary<string, string> continuationState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves and validates a pagination cursor. Returns the cursor entry only if the
    /// tool name, session ID, and request hash all match the stored values.
    /// </summary>
    /// <param name="cursorId">The opaque cursor ID to look up.</param>
    /// <param name="toolName">The expected tool name (must match stored value).</param>
    /// <param name="sessionId">The expected session ID (must match stored value).</param>
    /// <param name="requestHash">The expected request hash (must match stored value).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The cursor entry if valid, or <see langword="null"/> if not found or validation fails.</returns>
    ValueTask<PaginationCursorEntry?> GetAsync(
        string cursorId,
        string toolName,
        string sessionId,
        string requestHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the continuation state of an existing cursor. Used when a page is fetched
    /// and the backend provides new continuation state for the next page.
    /// </summary>
    /// <param name="cursorId">The cursor ID to update.</param>
    /// <param name="continuationState">The new continuation state.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if the cursor was found and updated; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> UpdateAsync(
        string cursorId,
        Dictionary<string, string> continuationState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a pagination cursor. Called when the last page has been reached.
    /// </summary>
    /// <param name="cursorId">The cursor ID to delete.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if the cursor was found and deleted; otherwise <see langword="false"/>.</returns>
    ValueTask<bool> DeleteAsync(
        string cursorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all pagination cursors for a specific session.
    /// </summary>
    /// <param name="sessionId">The session whose cursors should be cleared.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask ClearSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all pagination cursors across all sessions.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask ClearAllAsync(CancellationToken cancellationToken = default);
}
