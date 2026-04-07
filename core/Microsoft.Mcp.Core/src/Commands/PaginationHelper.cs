// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;
using Azure.Mcp.Core.Models.Option;
using Microsoft.Mcp.Core.Models.Pagination;
using Microsoft.Mcp.Core.Services.Pagination;

namespace Microsoft.Mcp.Core.Commands;

/// <summary>
/// Provides helper methods for implementing pagination in commands.
/// </summary>
public static class PaginationHelper
{
    /// <summary>
    /// Registers the <c>nextCursor</c> option on a command that supports pagination.
    /// </summary>
    /// <param name="command">The command to add the option to.</param>
    public static void RegisterPaginationOption(Command command)
    {
        command.Options.Add(OptionDefinitions.Common.NextCursor);
    }

    /// <summary>
    /// Binds the <c>nextCursor</c> value from the parsed command line arguments.
    /// </summary>
    /// <param name="parseResult">The parsed command line arguments.</param>
    /// <returns>The cursor value, or <see langword="null"/> if not provided.</returns>
    public static string? BindNextCursor(ParseResult parseResult)
    {
        return parseResult.GetValueOrDefault<string>(OptionDefinitions.Common.NextCursor.Name);
    }

    /// <summary>
    /// Computes a deterministic hash of the request parameters (excluding <c>nextCursor</c>)
    /// to validate that subsequent page requests match the original query.
    /// </summary>
    /// <param name="parseResult">The parsed command line arguments.</param>
    /// <param name="command">The command definition containing the options to hash.</param>
    /// <returns>A hex-encoded SHA256 hash string.</returns>
    public static string ComputeRequestHash(ParseResult parseResult, Command command)
    {
        var sb = new StringBuilder();

        foreach (var option in command.Options.OrderBy(o => o.Name, StringComparer.Ordinal))
        {
            // Skip the nextCursor option itself — it's not part of the request identity
            if (string.Equals(
                Helpers.NameNormalization.NormalizeOptionName(option.Name),
                OptionDefinitions.Common.NextCursorName,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = parseResult.GetValueOrDefault<object>(
                Helpers.NameNormalization.NormalizeOptionName(option.Name));

            if (value is not null)
            {
                sb.Append(option.Name);
                sb.Append('=');
                sb.Append(value);
                sb.Append('|');
            }
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Creates a <see cref="PaginationInfo"/> for inclusion in the command response.
    /// </summary>
    /// <param name="nextCursor">The cursor for the next page, or <see langword="null"/> if this is the last page.</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <returns>A pagination info object.</returns>
    public static PaginationInfo CreatePaginationInfo(string? nextCursor, int pageSize)
    {
        return new PaginationInfo
        {
            NextCursor = nextCursor,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Retrieves the continuation state for a cursor, creating a new request context for
    /// the first page or validating and retrieving an existing cursor for subsequent pages.
    /// </summary>
    /// <param name="registry">The pagination cursor registry.</param>
    /// <param name="nextCursor">The cursor from the client request, or <see langword="null"/> for the first page.</param>
    /// <param name="toolName">The name of the tool making the request.</param>
    /// <param name="sessionId">The session/user identity.</param>
    /// <param name="requestHash">The hash of the request parameters.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The cursor entry with continuation state, or <see langword="null"/> for the first page.</returns>
    /// <exception cref="ArgumentException">Thrown when the cursor is invalid or expired.</exception>
    public static async ValueTask<PaginationCursorEntry?> ResolveCursorAsync(
        IPaginationCursorRegistry registry,
        string? nextCursor,
        string toolName,
        string sessionId,
        string requestHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(nextCursor))
        {
            return null;
        }

        var entry = await registry.GetAsync(nextCursor, toolName, sessionId, requestHash, cancellationToken);

        if (entry is null)
        {
            throw new ArgumentException(
                "The pagination cursor is invalid, expired, or does not match the current request parameters. " +
                "Please retry the request without a cursor to start from the first page.");
        }

        return entry;
    }
}
