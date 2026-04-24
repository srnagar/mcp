// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Mcp.Core.Models.Pagination;

/// <summary>
/// Configuration options for the pagination framework.
/// </summary>
public sealed class PaginationOptions
{
    /// <summary>
    /// The configuration section name for binding.
    /// </summary>
    public const string SectionName = "Pagination";

    /// <summary>
    /// Gets or sets the default number of items returned per page.
    /// Individual tools may override this with their own page size.
    /// </summary>
    public int DefaultPageSize { get; set; } = 5;

    /// <summary>
    /// Gets or sets the time-to-live for pagination cursor entries in the cache.
    /// Cursors that are not accessed within this duration are automatically evicted.
    /// </summary>
    public TimeSpan CursorTimeToLive { get; set; } = TimeSpan.FromHours(1);
}
