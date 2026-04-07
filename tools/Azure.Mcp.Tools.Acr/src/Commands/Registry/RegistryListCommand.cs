// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.Acr.Options.Registry;
using Azure.Mcp.Tools.Acr.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;
using Microsoft.Mcp.Core.Models.Pagination;
using Microsoft.Mcp.Core.Services.Pagination;

namespace Azure.Mcp.Tools.Acr.Commands.Registry;

public sealed class RegistryListCommand(
    ILogger<RegistryListCommand> logger,
    IAcrService acrService,
    IPaginationCursorRegistry cursorRegistry,
    IOptions<PaginationOptions> paginationOptions) : BaseAcrCommand<RegistryListOptions>
{
    private const string CommandTitle = "List Container Registries";
    private readonly ILogger<RegistryListCommand> _logger = logger;
    private readonly IAcrService _acrService = acrService;
    private readonly IPaginationCursorRegistry _cursorRegistry = cursorRegistry;
    private readonly PaginationOptions _paginationOptions = paginationOptions.Value;

    public override string Id => "796f8778-2fa7-4343-87ad-06bdcf6b296c";

    public override string Name => "list";

    public override string Description =>
        $"""
        List Azure Container Registries in a subscription. Optionally filter by resource group. Each registry result
        includes: name, location, loginServer, skuName, skuTier. If no registries are found the tool returns null results
        (consistent with other list commands).
        Returns up to {_paginationOptions.DefaultPageSize} items per request. If pagination.nextCursor is non-null in the response,
        more results are available. To fetch the next page, call this tool again with the same parameters and the returned
        nextCursor value. Always confirm with the user before fetching additional pages.
        """;

    public override string Title => CommandTitle;

    public override ToolMetadata Metadata => new()
    {
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        ReadOnly = true,
        LocalRequired = false,
        Secret = false,
        SupportsPagination = true
    };

    protected override void RegisterOptions(Command command)
    {
        base.RegisterOptions(command);
        PaginationHelper.RegisterPaginationOption(command);
    }

    protected override RegistryListOptions BindOptions(ParseResult parseResult)
    {
        var options = base.BindOptions(parseResult);
        options.NextCursor = PaginationHelper.BindNextCursor(parseResult);
        return options;
    }

    public override async Task<CommandResponse> ExecuteAsync(CommandContext context, ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (!Validate(parseResult.CommandResult, context.Response).IsValid)
        {
            return context.Response;
        }

        var options = BindOptions(parseResult);
        var pageSize = _paginationOptions.DefaultPageSize;

        try
        {
            var toolName = $"acr_registry_{Name}";
            var sessionId = context.Activity?.Id ?? "default";
            var requestHash = PaginationHelper.ComputeRequestHash(parseResult, GetCommand());

            // Resolve cursor — returns null for first page, entry with state for subsequent pages
            int skip = 0;
            var cursorEntry = await PaginationHelper.ResolveCursorAsync(
                _cursorRegistry, options.NextCursor, toolName, sessionId, requestHash, cancellationToken);

            if (cursorEntry is not null &&
                cursorEntry.ContinuationState.TryGetValue("offset", out var offsetStr) &&
                int.TryParse(offsetStr, out var offset))
            {
                skip = offset;
            }

            _logger.LogInformation("Listing container registries. Subscription: {Subscription}, ResourceGroup: {ResourceGroup}, Skip: {Skip}, Limit: {Limit}", options.Subscription, options.ResourceGroup, skip, pageSize);

            var registries = await _acrService.ListRegistries(
                options.Subscription!,
                options.ResourceGroup,
                options.Tenant,
                options.RetryPolicy,
                cancellationToken,
                limit: pageSize,
                skip: skip);

            // Determine if there are more results and manage cursor
            string? nextCursor = null;
            if (registries?.AreResultsTruncated == true)
            {
                var continuationState = new Dictionary<string, string>
                {
                    ["offset"] = registries.NextOffset.ToString()
                };

                if (options.NextCursor is not null)
                {
                    await _cursorRegistry.UpdateAsync(options.NextCursor, continuationState, cancellationToken);
                    nextCursor = options.NextCursor;
                }
                else
                {
                    nextCursor = await _cursorRegistry.CreateAsync(
                        toolName, sessionId, requestHash, continuationState, cancellationToken);
                }
            }
            else if (options.NextCursor is not null)
            {
                await _cursorRegistry.DeleteAsync(options.NextCursor, cancellationToken);
            }

            var pagination = PaginationHelper.CreatePaginationInfo(nextCursor, pageSize);
            context.Response.Results = ResponseResult.Create(
                new RegistryListCommandResult(registries?.Results ?? [], pagination),
                AcrJsonContext.Default.RegistryListCommandResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error listing container registries. Subscription: {Subscription}, ResourceGroup: {ResourceGroup}, Options: {@Options}",
                options.Subscription, options.ResourceGroup, options);
            HandleException(context, ex);
        }

        return context.Response;
    }

    internal record RegistryListCommandResult(List<Models.AcrRegistryInfo> Registries, PaginationInfo Pagination);
}
