// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Mcp.Core.Models.Pagination;

namespace Microsoft.Mcp.Core.Services.Pagination;

/// <summary>
/// In-memory implementation of <see cref="IPaginationCursorRegistry"/> backed by <see cref="PaginationCursorCache"/>.
/// Cursor IDs are deterministic: computed from the request hash and continuation state,
/// making paginated requests naturally idempotent.
/// </summary>
public sealed class PaginationCursorRegistry(
    PaginationCursorCache cache,
    IOptions<PaginationOptions> options,
    ILogger<PaginationCursorRegistry> logger) : IPaginationCursorRegistry
{
    private const string CursorPrefix = "c_";

    private readonly PaginationCursorCache _cache = cache;
    private readonly PaginationOptions _options = options.Value;
    private readonly ILogger<PaginationCursorRegistry> _logger = logger;

    public ValueTask<string> CreateAsync(
        string toolName,
        string sessionId,
        string requestHash,
        Dictionary<string, string> continuationState,
        CancellationToken cancellationToken = default)
    {
        var cursorId = GenerateCursorId(requestHash, continuationState);

        var entry = new PaginationCursorEntry
        {
            ToolName = toolName,
            SessionId = sessionId,
            RequestHash = requestHash,
            ContinuationState = continuationState,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _cache.Set(cursorId, entry, _options.CursorTimeToLive);

        _logger.LogDebug(
            "Created pagination cursor {CursorId} for tool {ToolName}, session {SessionId}.",
            cursorId, toolName, sessionId);

        return new ValueTask<string>(cursorId);
    }

    public ValueTask<PaginationCursorEntry?> GetAsync(
        string cursorId,
        string toolName,
        string sessionId,
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

    public ValueTask ClearSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var entries = _cache.GetAll();

        foreach (var kvp in entries)
        {
            if (string.Equals(kvp.Value.SessionId, sessionId, StringComparison.Ordinal))
            {
                _cache.Remove(kvp.Key);
            }
        }

        _logger.LogDebug("Cleared all pagination cursors for session {SessionId}.", sessionId);
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAllAsync(CancellationToken cancellationToken = default)
    {
        _cache.Clear();
        _logger.LogDebug("Cleared all pagination cursors.");
        return ValueTask.CompletedTask;
    }

    internal static string GenerateCursorId(string requestHash, Dictionary<string, string> continuationState)
    {
        var sb = new StringBuilder();
        sb.Append(requestHash);
        sb.Append('|');

        foreach (var kvp in continuationState.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.Append(kvp.Key);
            sb.Append('=');
            sb.Append(kvp.Value);
            sb.Append('&');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return $"{CursorPrefix}{Convert.ToHexStringLower(bytes)[..32]}";
    }
}
