// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Mcp.Core.Models.Pagination;

/// <summary>
/// Represents the backend-specific continuation state for fetching the next page of results.
/// Exactly one property should be set, corresponding to the pagination mechanism used by the
/// underlying Azure service. This type restricts the allowed continuation keys to prevent
/// arbitrary data from being stored in pagination cursors.
/// </summary>
public sealed class ContinuationState
{
    /// <summary>
    /// Gets or sets an ARM SDK continuation token (used by <c>AsyncPageable</c>-based services).
    /// </summary>
    public string? ContinuationToken { get; init; }

    /// <summary>
    /// Gets or sets an offset value for Resource Graph query pagination.
    /// </summary>
    public string? Offset { get; init; }

    /// <summary>
    /// Gets or sets a skip token for OData/Marketplace-style pagination.
    /// </summary>
    public string? SkipToken { get; init; }

    /// <summary>
    /// Gets or sets a next link URL for REST API pagination.
    /// </summary>
    public string? NextLink { get; init; }

    /// <summary>
    /// Returns <see langword="true"/> if at least one continuation value is set.
    /// </summary>
    public bool HasValue =>
        ContinuationToken is not null ||
        Offset is not null ||
        SkipToken is not null ||
        NextLink is not null;
}
