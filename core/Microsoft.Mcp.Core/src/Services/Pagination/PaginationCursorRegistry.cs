// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Mcp.Core.Models.Pagination;

namespace Microsoft.Mcp.Core.Services.Pagination;

/// <summary>
/// In-memory implementation of <see cref="IPaginationCursorRegistry"/> backed by <see cref="PaginationCursorCache"/>.
/// Cursor IDs are opaque GUIDs. On retrieval, the registry validates that the tool name
/// and request hash match the stored entry to prevent cross-tool or cross-query reuse.
/// </summary>
public sealed class PaginationCursorRegistry(
    PaginationCursorCache cache,
    IOptions<PaginationOptions> options,
    ILogger<PaginationCursorRegistry> logger) : IPaginationCursorRegistry
{
    private readonly PaginationCursorCache _cache = cache;
    private readonly PaginationOptions _options = options.Value;
    private readonly ILogger<PaginationCursorRegistry> _logger = logger;

    public ValueTask<string> CreateAsync(
        string toolName,
        string requestHash,
        ContinuationState continuationState,
        CancellationToken cancellationToken = default)
    {
        var cursorId = Guid.NewGuid().ToString("N");

        var entry = new PaginationCursorEntry
        {
            ToolName = toolName,
            RequestHash = requestHash,
            ContinuationState = continuationState,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _cache.Set(cursorId, entry, _options.CursorTimeToLive);

        _logger.LogDebug(
            "Created pagination cursor {CursorId} for tool {ToolName}.",
            cursorId, toolName);

        return new ValueTask<string>(cursorId);
    }

    public ValueTask<PaginationCursorEntry?> GetAsync(
        string cursorId,
        string toolName,
        string requestHash,
        CancellationToken cancellationToken = default)
    {
        var entry = _cache.Get(cursorId);

        if (entry is null)
        {
            _logger.LogDebug("Pagination cursor {CursorId} not found or expired.", cursorId);
            return new ValueTask<PaginationCursorEntry?>(result: null);
        }

        if (!string.Equals(entry.ToolName, toolName, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Pagination cursor {CursorId} tool mismatch: expected {ExpectedTool}, got {ActualTool}.",
                cursorId, toolName, entry.ToolName);
            return new ValueTask<PaginationCursorEntry?>(result: null);
        }

        if (!string.Equals(entry.RequestHash, requestHash, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Pagination cursor {CursorId} request hash mismatch for tool {ToolName}. " +
                "The request parameters may have changed between pages.",
                cursorId, toolName);
            return new ValueTask<PaginationCursorEntry?>(result: null);
        }

        return new ValueTask<PaginationCursorEntry?>(entry);
    }

    public ValueTask<bool> DeleteAsync(
        string cursorId,
        CancellationToken cancellationToken = default)
    {
        var removed = _cache.Remove(cursorId);

        if (removed)
        {
            _logger.LogDebug("Deleted pagination cursor {CursorId}.", cursorId);
        }

        return new ValueTask<bool>(removed);
    }

    public ValueTask ClearAllAsync(CancellationToken cancellationToken = default)
    {
        _cache.Clear();
        _logger.LogDebug("Cleared all pagination cursors.");
        return ValueTask.CompletedTask;
    }
}
