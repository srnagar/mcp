// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Services.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Mcp.Core.Models.Pagination;

namespace Microsoft.Mcp.Core.Services.Pagination;

/// <summary>
/// In-memory implementation of <see cref="IPaginationCursorRegistry"/> backed by <see cref="ICacheService"/>.
/// Uses the "pagination" cache group to isolate cursor entries from other cached data.
/// </summary>
public sealed class PaginationCursorRegistry(
    ICacheService cacheService,
    IOptions<PaginationOptions> options,
    ILogger<PaginationCursorRegistry> logger) : IPaginationCursorRegistry
{
    internal const string CacheGroup = "pagination";
    private const string CursorPrefix = "c_";

    private readonly ICacheService _cacheService = cacheService;
    private readonly PaginationOptions _options = options.Value;
    private readonly ILogger<PaginationCursorRegistry> _logger = logger;

    public async ValueTask<string> CreateAsync(
        string toolName,
        string sessionId,
        string requestHash,
        Dictionary<string, string> continuationState,
        CancellationToken cancellationToken = default)
    {
        var cursorId = GenerateCursorId();

        var entry = new PaginationCursorEntry
        {
            ToolName = toolName,
            SessionId = sessionId,
            RequestHash = requestHash,
            ContinuationState = continuationState,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _cacheService.SetAsync(
            CacheGroup,
            cursorId,
            entry,
            _options.CursorTimeToLive,
            cancellationToken);

        _logger.LogDebug(
            "Created pagination cursor {CursorId} for tool {ToolName}, session {SessionId}.",
            cursorId, toolName, sessionId);

        return cursorId;
    }

    public async ValueTask<PaginationCursorEntry?> GetAsync(
        string cursorId,
        string toolName,
        string sessionId,
        string requestHash,
        CancellationToken cancellationToken = default)
    {
        var entry = await _cacheService.GetAsync<PaginationCursorEntry>(
            CacheGroup,
            cursorId,
            _options.CursorTimeToLive,
            cancellationToken);

        if (entry is null)
        {
            _logger.LogDebug("Pagination cursor {CursorId} not found or expired.", cursorId);
            return null;
        }

        if (!string.Equals(entry.ToolName, toolName, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Pagination cursor {CursorId} tool mismatch: expected {ExpectedTool}, got {ActualTool}.",
                cursorId, toolName, entry.ToolName);
            return null;
        }

        // if (!string.Equals(entry.SessionId, sessionId, StringComparison.Ordinal))
        // {
        //     _logger.LogWarning(
        //         "Pagination cursor {CursorId} session mismatch for tool {ToolName}.",
        //         cursorId, toolName);
        //     return null;
        // }

        if (!string.Equals(entry.RequestHash, requestHash, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Pagination cursor {CursorId} request hash mismatch for tool {ToolName}. " +
                "The request parameters may have changed between pages.",
                cursorId, toolName);
            return null;
        }

        return entry;
    }

    public async ValueTask<bool> UpdateAsync(
        string cursorId,
        Dictionary<string, string> continuationState,
        CancellationToken cancellationToken = default)
    {
        var entry = await _cacheService.GetAsync<PaginationCursorEntry>(
            CacheGroup,
            cursorId,
            _options.CursorTimeToLive,
            cancellationToken);

        if (entry is null)
        {
            _logger.LogDebug("Cannot update pagination cursor {CursorId}: not found or expired.", cursorId);
            return false;
        }

        entry.ContinuationState = continuationState;

        await _cacheService.SetAsync(
            CacheGroup,
            cursorId,
            entry,
            _options.CursorTimeToLive,
            cancellationToken);

        _logger.LogDebug("Updated pagination cursor {CursorId} with new continuation state.", cursorId);
        return true;
    }

    public async ValueTask<bool> DeleteAsync(
        string cursorId,
        CancellationToken cancellationToken = default)
    {
        var entry = await _cacheService.GetAsync<PaginationCursorEntry>(
            CacheGroup,
            cursorId,
            cancellationToken: cancellationToken);

        if (entry is null)
        {
            return false;
        }

        await _cacheService.DeleteAsync(CacheGroup, cursorId, cancellationToken);
        _logger.LogDebug("Deleted pagination cursor {CursorId}.", cursorId);
        return true;
    }

    public async ValueTask ClearSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var keys = await _cacheService.GetGroupKeysAsync(CacheGroup, cancellationToken);

        foreach (var key in keys)
        {
            var entry = await _cacheService.GetAsync<PaginationCursorEntry>(
                CacheGroup,
                key,
                cancellationToken: cancellationToken);

            if (entry is not null && string.Equals(entry.SessionId, sessionId, StringComparison.Ordinal))
            {
                await _cacheService.DeleteAsync(CacheGroup, key, cancellationToken);
            }
        }

        _logger.LogDebug("Cleared all pagination cursors for session {SessionId}.", sessionId);
    }

    public async ValueTask ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await _cacheService.ClearGroupAsync(CacheGroup, cancellationToken);
        _logger.LogDebug("Cleared all pagination cursors.");
    }

    private static string GenerateCursorId() =>
        $"{CursorPrefix}{Guid.NewGuid():N}";
}
