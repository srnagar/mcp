// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.Mcp.Core.Commands.Subscription;
using Azure.Mcp.Tools.Monitor.Models.ActivityLog;
using Azure.Mcp.Tools.Monitor.Options.ActivityLog;
using Azure.Mcp.Tools.Monitor.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Extensions;
using Microsoft.Mcp.Core.Models.Command;
using Microsoft.Mcp.Core.Models.Option;
using Microsoft.Mcp.Core.Models.Pagination;
using Microsoft.Mcp.Core.Services.Pagination;

namespace Azure.Mcp.Tools.Monitor.Commands.ActivityLog;

public sealed class ActivityLogListCommand(
    ILogger<ActivityLogListCommand> logger,
    IPaginationCursorRegistry cursorRegistry,
    IOptions<PaginationOptions> paginationOptions)
    : SubscriptionCommand<ActivityLogListOptions>
{
    private const string CommandTitle = "List Activity Logs";
    internal record ActivityLogListCommandResult(List<ActivityLogEventData> ActivityLogs, PaginationInfo Pagination);

    private readonly IPaginationCursorRegistry _cursorRegistry = cursorRegistry;
    private readonly PaginationOptions _paginationOptions = paginationOptions.Value;

    public override string Id => "ffc0ed72-0622-4a27-bfd8-6df9b83adce8";

    public override string Name => "list";

    public override string Description =>
        $"""
        Always use this tool if user is asking for activity logs for a resource.
        Lists activity logs for the specified Azure resource over the given prior number of hours.
        This command retrieves activity logs to help understand resource deployment history, modification activities, and access patterns.
        Returns activity log events with details including timestamp, operation name, status, and caller information. should be called to help retrieve information about why a resource failed to deploy or may not be working.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false,
    SupportsPagination = true)]
public sealed class ActivityLogListCommand(ILogger<ActivityLogListCommand> logger, IMonitorService monitorService, IPaginationCursorRegistry cursorRegistry, IOptions<PaginationOptions> paginationOptions)
    : SubscriptionCommand<ActivityLogListOptions>
{
    private readonly ILogger<ActivityLogListCommand> _logger = logger;
    private readonly IMonitorService _monitorService = monitorService;
    private readonly IPaginationCursorRegistry _cursorRegistry = cursorRegistry;
    private readonly PaginationOptions _paginationOptions = paginationOptions.Value;

    internal record ActivityLogListCommandResult(
        List<ActivityLogEventData> ActivityLogs,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? NextCursor);

    protected override void RegisterOptions(Command command)
    {
        base.RegisterOptions(command);
        command.Options.Add(OptionDefinitions.Common.ResourceGroup.AsOptional());
        command.Options.Add(ActivityLogOptionDefinitions.ResourceName);
        command.Options.Add(ActivityLogOptionDefinitions.ResourceType);
        command.Options.Add(ActivityLogOptionDefinitions.Hours);
        command.Options.Add(ActivityLogOptionDefinitions.EventLevel);
        PaginationHelper.RegisterPaginationOption(command);
    }

    protected override ActivityLogListOptions BindOptions(ParseResult parseResult)
    {
        var options = base.BindOptions(parseResult);
        options.ResourceGroup = parseResult.GetValueOrDefault<string>(OptionDefinitions.Common.ResourceGroup.Name);
        options.ResourceName = parseResult.GetValueOrDefault<string>(ActivityLogOptionDefinitions.ResourceName.Name);
        options.ResourceType = parseResult.GetValueOrDefault<string>(ActivityLogOptionDefinitions.ResourceType.Name);
        options.Hours = parseResult.GetValueOrDefault<double>(ActivityLogOptionDefinitions.Hours.Name);
        options.EventLevel = parseResult.GetValueOrDefault<ActivityLogEventLevel?>(ActivityLogOptionDefinitions.EventLevel.Name);
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
            var service = context.GetService<IMonitorService>();

            if (_paginationOptions.Enabled)
            {
                var toolName = $"monitor_activitylog_{Name}";
                var requestHash = PaginationHelper.ComputeRequestHash(parseResult, GetCommand());

                string? nextLink = null;
                var cursorEntry = await PaginationHelper.ResolveCursorAsync(
                    _cursorRegistry, options.Cursor, toolName, requestHash, cancellationToken);

            if (cursorEntry is not null)
            {
                cursorEntry.ContinuationState.TryGetValue("nextLink", out nextLink);
            }

            var result = await service.ListActivityLogsPaged(
                options.Subscription!,
                options.ResourceName!,
                options.ResourceGroup,
                options.ResourceType,
                options.Hours ?? 24.0,
                options.EventLevel,
                pageSize,
                nextLink,
                options.Tenant,
                options.RetryPolicy,
                cancellationToken);

            // Manage cursor
            string? nextCursor = null;
            if (!string.IsNullOrEmpty(result.NextLink))
            {
                var continuationState = new Dictionary<string, string>
                {
                    ["nextLink"] = result.NextLink
                };

                if (options.NextCursor is not null)
                {
                    await _cursorRegistry.UpdateAsync(options.NextCursor, continuationState, cancellationToken);
                    nextCursor = options.NextCursor;
                }
                else
                {
                    nextCursor = await _cursorRegistry.CreateAsync(
                        toolName, requestHash, continuationState, cancellationToken);
                }

                context.Response.Results = ResponseResult.Create(
                    new ActivityLogListCommandResult(result.Items, nextCursor),
                    MonitorJsonContext.Default.ActivityLogListCommandResult);
            }
            else
            {
                var activityLogs = await service.ListActivityLogs(
                    options.Subscription!,
                    options.ResourceName!,
                    options.ResourceGroup,
                    options.ResourceType,
                    options.Hours ?? 24.0,
                    options.EventLevel,
                    tenant: options.Tenant,
                    retryPolicy: options.RetryPolicy,
                    cancellationToken: cancellationToken);

                context.Response.Results = ResponseResult.Create(
                    new ActivityLogListCommandResult(activityLogs, null),
                    MonitorJsonContext.Default.ActivityLogListCommandResult);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error listing activity logs. ResourceName: {ResourceName}, ResourceType: {ResourceType}, Hours: {Hours}.",
                options.ResourceName, options.ResourceType, options.Hours);
            HandleException(context, ex);
        }

        return context.Response;
    }

    protected override string GetErrorMessage(Exception ex) => ex switch
    {
        RequestFailedException reqEx when reqEx.Status == 404 =>
            "Resource not found. Verify the resource name and that you have access to it.",
        RequestFailedException reqEx when reqEx.Status == 403 =>
            $"Authorization failed accessing the resource activity logs. Details: {reqEx.Message}",
        HttpRequestException httpEx when httpEx.Message.Contains("404") =>
            "Resource not found. Verify the resource name and that you have access to it.",
        HttpRequestException httpEx when httpEx.Message.Contains("403") =>
            "Authorization failed accessing the resource activity logs. Ensure you have appropriate permissions to view activity logs.",
        Azure.RequestFailedException reqEx => reqEx.Message,
        HttpRequestException httpEx => httpEx.Message,
        _ => base.GetErrorMessage(ex)
    };

    protected override HttpStatusCode GetStatusCode(Exception ex) => ex switch
    {
        Azure.RequestFailedException reqEx => (HttpStatusCode)reqEx.Status,
        HttpRequestException httpEx when httpEx.Message.Contains("404") => (HttpStatusCode)404,
        HttpRequestException httpEx when httpEx.Message.Contains("403") => (HttpStatusCode)403,
        _ => base.GetStatusCode(ex)
    };

}
