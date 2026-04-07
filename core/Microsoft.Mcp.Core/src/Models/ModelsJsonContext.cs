// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Microsoft.Mcp.Core.Areas.Tools.Commands;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;
using Microsoft.Mcp.Core.Models.Pagination;

namespace Microsoft.Mcp.Core.Models;

[JsonSerializable(typeof(List<CommandInfo>))]
[JsonSerializable(typeof(CommandResponse))]
[JsonSerializable(typeof(ETag), TypeInfoPropertyName = "McpETag")]
[JsonSerializable(typeof(ToolMetadata))]
[JsonSerializable(typeof(ToolsListCommand.ToolNamesResult))]
[JsonSerializable(typeof(PaginationInfo))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class ModelsJsonContext : JsonSerializerContext
{
    // This class is intentionally left empty. It is used for source generation of JSON serialization.
}
