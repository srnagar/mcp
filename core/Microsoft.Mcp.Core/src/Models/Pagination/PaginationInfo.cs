// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Microsoft.Mcp.Core.Models.Pagination;

/// <summary>
/// Represents pagination metadata included in tool responses that support paginated results.
/// </summary>
public sealed class PaginationInfo
{
    /// <summary>
    /// Gets or sets the opaque cursor for fetching the next page of results.
    /// When <see langword="null"/>, there are no more pages available.
    /// </summary>
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }

    /// <summary>
    /// Gets or sets the number of items requested per page.
    /// </summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }
}
