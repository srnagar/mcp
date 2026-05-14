// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.EventGrid.Options.Topic;
using Azure.Mcp.Tools.EventGrid.Services;
using Microsoft.Extensions.Options;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Extensions;
using Microsoft.Mcp.Core.Models.Command;
using Microsoft.Mcp.Core.Models.Option;
using Microsoft.Mcp.Core.Models.Pagination;
using Microsoft.Mcp.Core.Services.Pagination;
using System.Text.Json.Serialization;

namespace Azure.Mcp.Tools.EventGrid.Commands.Topic;

[CommandMetadata(
    Id = "42390294-2856-4980-a057-095c91355650",
    Name = "list",
    Title = "List Event Grid Topics",
    Description = "List or show all Event Grid topics in a subscription, optionally filtered by resource group, returning endpoints, access keys, provisioning state, and subscription details for event publishing and management. A subscription or topic name is required.",
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false,
    SupportsPagination = true)]
public sealed class TopicListCommand(ILogger<TopicListCommand> logger, IEventGridService eventGridService, IPaginationCursorRegistry cursorRegistry, IOptions<PaginationOptions> paginationOptions) : BaseEventGridCommand<TopicListOptions>
{
    private readonly ILogger<TopicListCommand> _logger = logger;
    private readonly IEventGridService _eventGridService = eventGridService;

    private readonly IPaginationCursorRegistry _cursorRegistry = cursorRegistry;
    private readonly PaginationOptions _paginationOptions = paginationOptions.Value;


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
        options.Cursor = PaginationHelper.BindCursor(parseResult);
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
            if (_paginationOptions.Enabled)
            {
                var toolName = $"eventgrid_topic_{Name}";
                var requestHash = PaginationHelper.ComputeRequestHash(parseResult, GetCommand());

                string? armContinuationToken = null;
                var cursorEntry = await PaginationHelper.ResolveCursorAsync(
                    _cursorRegistry, options.Cursor, toolName, requestHash, cancellationToken);

                if (cursorEntry is not null)
                {
                    armContinuationToken = cursorEntry.ContinuationState.ContinuationToken;
                }

                var result = await _eventGridService.GetTopicsPagedAsync(
                    options.Subscription!,
                    options.ResourceGroup,
                    options.Tenant,
                    options.RetryPolicy,
                    pageSize,
                    armContinuationToken,
                    cancellationToken);

                string? nextCursor = null;
                if (!string.IsNullOrEmpty(result.ContinuationToken))
                {
                    var continuationState = new ContinuationState
                    {
                        ContinuationToken = result.ContinuationToken
                    };

                    nextCursor = await _cursorRegistry.CreateAsync(
                        toolName, requestHash, continuationState, cancellationToken);
                }

                context.Response.Results = ResponseResult.Create(
                    new TopicListCommandResult(result.Items, nextCursor),
                    EventGridJsonContext.Default.TopicListCommandResult);
            }
            else
            {
                var topics = await _eventGridService.GetTopicsAsync(
                    options.Subscription!,
                    options.ResourceGroup,
                    options.Tenant,
                    options.RetryPolicy,
                    cancellationToken);

                context.Response.Results = ResponseResult.Create(
                    new TopicListCommandResult(topics, null),
                    EventGridJsonContext.Default.TopicListCommandResult);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error listing Event Grid topics. Subscription: {Subscription}.",
                options.Subscription);
            HandleException(context, ex);
        }

        return context.Response;
    }

    internal record TopicListCommandResult(
        List<EventGridTopicInfo> Topics,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? NextCursor);
}
