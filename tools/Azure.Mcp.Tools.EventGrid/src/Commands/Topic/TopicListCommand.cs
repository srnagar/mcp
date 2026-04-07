// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Core.Extensions;
using Azure.Mcp.Core.Models.Option;
using Azure.Mcp.Tools.EventGrid.Options.Topic;
using Azure.Mcp.Tools.EventGrid.Services;
using Microsoft.Extensions.Options;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;
using Microsoft.Mcp.Core.Models.Pagination;
using Microsoft.Mcp.Core.Services.Pagination;

namespace Azure.Mcp.Tools.EventGrid.Commands.Topic;

public sealed class TopicListCommand(
    ILogger<TopicListCommand> logger,
    IEventGridService eventGridService,
    IPaginationCursorRegistry cursorRegistry,
    IOptions<PaginationOptions> paginationOptions) : BaseEventGridCommand<TopicListOptions>
{
    private const string CommandTitle = "List Event Grid Topics";
    private readonly ILogger<TopicListCommand> _logger = logger;
    private readonly IEventGridService _eventGridService = eventGridService;
    private readonly IPaginationCursorRegistry _cursorRegistry = cursorRegistry;
    private readonly PaginationOptions _paginationOptions = paginationOptions.Value;

    public override string Id => "42390294-2856-4980-a057-095c91355650";

    public override string Name => "list";

    public override string Description =>
        $"""
        List or show all Event Grid topics in a subscription, optionally filtered by resource group, returning endpoints, access keys, provisioning state, and subscription details for event publishing and management. A subscription or topic name is required.
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
        command.Options.Add(OptionDefinitions.Common.ResourceGroup);
        PaginationHelper.RegisterPaginationOption(command);
    }

    protected override TopicListOptions BindOptions(ParseResult parseResult)
    {
        var options = base.BindOptions(parseResult);
        options.ResourceGroup ??= parseResult.GetValueOrDefault<string>(OptionDefinitions.Common.ResourceGroup.Name);
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
            var toolName = $"eventgrid_topic_{Name}";
            var sessionId = context.Activity?.Id ?? "default";
            var requestHash = PaginationHelper.ComputeRequestHash(parseResult, GetCommand());

            // Resolve cursor for continuation token
            string? armContinuationToken = null;
            var cursorEntry = await PaginationHelper.ResolveCursorAsync(
                _cursorRegistry, options.NextCursor, toolName, sessionId, requestHash, cancellationToken);

            if (cursorEntry is not null)
            {
                cursorEntry.ContinuationState.TryGetValue("continuationToken", out armContinuationToken);
            }

            var result = await _eventGridService.GetTopicsPagedAsync(
                options.Subscription!,
                options.ResourceGroup,
                options.Tenant,
                options.RetryPolicy,
                pageSize,
                armContinuationToken,
                cancellationToken);

            // Manage cursor based on whether more results exist
            string? nextCursor = null;
            if (!string.IsNullOrEmpty(result.ContinuationToken))
            {
                var continuationState = new Dictionary<string, string>
                {
                    ["continuationToken"] = result.ContinuationToken
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
                new TopicListCommandResult(result.Items, pagination),
                EventGridJsonContext.Default.TopicListCommandResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error listing Event Grid topics. Subscription: {Subscription}, Options: {@Options}",
                options.Subscription, options);
            HandleException(context, ex);
        }

        return context.Response;
    }

    internal record TopicListCommandResult(List<EventGridTopicInfo> Topics, PaginationInfo Pagination);
}
