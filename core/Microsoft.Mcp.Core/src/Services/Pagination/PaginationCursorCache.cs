// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Microsoft.Mcp.Core.Models.Pagination;

namespace Microsoft.Mcp.Core.Services.Pagination;

/// <summary>
/// A dedicated in-memory cache for pagination cursor entries.
/// Uses a <see cref="ConcurrentDictionary{TKey, TValue}"/> with absolute expiration
/// to store cursor entries independently of any general-purpose cache service.
/// </summary>
public sealed class PaginationCursorCache
{
    private readonly ConcurrentDictionary<string, CacheItem> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets a cursor entry by its ID. Returns <see langword="null"/> if the entry
    /// does not exist or has expired.
    /// </summary>
    public PaginationCursorEntry? Get(string cursorId)
    {
        if (!_entries.TryGetValue(cursorId, out var item))
        {
            return null;
        }

        if (DateTimeOffset.UtcNow >= item.ExpiresAt)
        {
            _entries.TryRemove(cursorId, out _);
            return null;
        }

        return item.Entry;
    }

    /// <summary>
    /// Stores a cursor entry with the specified time-to-live. If an entry with the
    /// same ID already exists, it is overwritten.
    /// </summary>
    public void Set(string cursorId, PaginationCursorEntry entry, TimeSpan timeToLive)
    {
        var item = new CacheItem(entry, DateTimeOffset.UtcNow + timeToLive);
        _entries[cursorId] = item;
    }

    /// <summary>
    /// Removes a cursor entry by its ID.
    /// </summary>
    /// <returns><see langword="true"/> if the entry was found and removed; otherwise <see langword="false"/>.</returns>
    public bool Remove(string cursorId)
    {
        return _entries.TryRemove(cursorId, out _);
    }

    /// <summary>
    /// Returns a snapshot of all non-expired cursor IDs and their entries.
    /// Expired entries encountered during enumeration are removed.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, PaginationCursorEntry>> GetAll()
    {
        var now = DateTimeOffset.UtcNow;
        var results = new List<KeyValuePair<string, PaginationCursorEntry>>();

        foreach (var kvp in _entries)
        {
            if (now >= kvp.Value.ExpiresAt)
            {
                _entries.TryRemove(kvp.Key, out _);
                continue;
            }

            results.Add(new KeyValuePair<string, PaginationCursorEntry>(kvp.Key, kvp.Value.Entry));
        }

        return results;
    }

    /// <summary>
    /// Removes all cursor entries.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
    }

    private sealed record CacheItem(PaginationCursorEntry Entry, DateTimeOffset ExpiresAt);
}
