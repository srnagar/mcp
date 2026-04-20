# Azure MCP Tools That Return Collections

This document catalogs all Azure MCP tools that return a list of items, including explicit `list` commands and `get` commands that return collections when an item identifier is omitted.

**80 commands total** — 45 explicit list commands and 35 get commands that can return collections.

## Service Backend Summary

Each command retrieves data through one of four backend patterns. The backend determines pagination behavior and whether the MCP tool can impose item limits.

| Backend | Commands | Pagination Model | MCP Item Limit |
|---|---|---|---|
| **Azure Resource Graph** | 12 | KQL `limit` clause; `ResultTruncated` flag but no continuation token | 50 (default via `ExecuteResourceQueryAsync`) |
| **Azure Resource Manager SDK** | 44 | `AsyncPageable<T>` / `GetAllAsync()` — SDK handles page iteration automatically | None — all pages consumed |
| **Data-plane SDK** | 12 | SDK-specific (`AsyncPageable`, stream iterators, `await foreach`) | None (except Cosmos stream iterators) |
| **REST API (direct HTTP)** | 8 | Varies — `nextLink`, `$skiptoken`, OData, or none | Varies per command |
| **Service-specific protocol** | 3 | Direct query; returns full result set | MySQL: 10,000 hardcoded |
| **Static / computed** | 1 | N/A | N/A |

### Azure Resource Graph commands (12)

These use `BaseAzureResourceService.ExecuteResourceQueryAsync()` with a KQL `| limit 50` clause. Results include an `AreResultsTruncated` flag but no continuation token for fetching additional pages.

`acr registry list`, `advisor recommendation list`, `appconfig account list`, `containerapps list`, `deviceregistry namespace list`, `grafana list`, `kusto cluster list`, `role assignment list`, `sql elastic-pool list`, `storage account get`, `sql db get`, `workbooks list`

### Azure Resource Manager SDK commands (44)

These use ARM SDK methods (`GetAllAsync()`, `GetXxxAsync()`) that return `AsyncPageable<T>` or `IAsyncEnumerable<T>`. The SDK handles pagination transparently — all pages are consumed. No MCP-layer limits are applied.

Includes: subscription/group management, AKS, App Service, Compute, Cosmos (accounts), Datadog, DesktopVirtualization, EventGrid, Event Hubs, File Sync, Foundry Extensions (OpenAI models), Load Testing, Managed Lustre, Monitor (workspaces/tables), MySQL/Postgres (servers), Policy, Redis, Search services, Service Fabric, SignalR, SQL (entra-admin/firewall-rules)

### Data-plane SDK commands (12)

These call Azure service data-plane APIs directly through typed SDKs, bypassing ARM. Pagination is handled by each SDK internally.

ACR repositories (`ContainerRegistryClient`), App Configuration key-values, Azure AI Search (indexes, knowledge bases/sources), Blob Storage (blobs, containers), Foundry Extensions (knowledge indexes), Key Vault (secrets, keys, certificates), Storage tables (`TableServiceClient`)

### REST API commands (8)

These make direct HTTP calls to Azure REST APIs. Pagination varies by endpoint.

Activity Log (`nextLink`), App Service diagnostics (direct HTTP, planned SDK migration), Marketplace products (`$skiptoken`), Pricing (API pagination), Quota availability (computed, no pagination), Resource Health events/status (OData)

### Service-specific protocol commands (3)

These connect to service endpoints using native protocols rather than ARM or REST.

Kusto database/table list (`.show` control commands — no pagination), MySQL database/table list (SQL queries — hardcoded 10,000 limit), Postgres database/table list (SQL queries — no explicit limit)

---

## Commands by Service Area

Commands marked with **(get)** are get commands that return a collection when the item identifier is omitted.

### Core Platform

| Command | Backend | Limits Items | Service Supports Pagination |
|---|---|---|---|
| `azmcp subscription list` | ARM SDK (`armClient.GetSubscriptions()`) | Yes — 10,000 hardcoded | Yes — `AsyncPageable` |
| `azmcp group list` | ARM SDK (`GetResourceGroups().GetAllAsync()`) | No | Yes — `GetAllAsync` |
| `azmcp group resource list` | ARM SDK (`GetGenericResourcesAsync()`) | No | Yes — `IAsyncEnumerable` |
| `azmcp policy assignment list` | ARM SDK (`GetAllAsync()` on policy assignments) | No | Yes — `AsyncPageable` |
| `azmcp role assignment list` | Resource Graph (`authorizationresources`) | Yes — 50 | Yes — `ResultTruncated` flag |
| `azmcp pricing get` **(get)** | REST API (Azure Retail Prices) | No | Yes — API pagination |

### Compute, Containers & App Hosting

| Command | Backend | Limits Items | Service Supports Pagination |
|---|---|---|---|
| `azmcp aks cluster get` **(get)** | ARM SDK (AKS) | No | Yes — ARM SDK enumeration |
| `azmcp aks nodepool get` **(get)** | ARM SDK (AKS) | No | Yes — ARM SDK enumeration |
| `azmcp appservice webapp get` **(get)** | ARM SDK (App Service) | No | Yes — ARM SDK enumeration |
| `azmcp appservice webapp diagnostic list` | REST API (direct HTTP) | No | Unclear — planned SDK migration |
| `azmcp compute disk get` **(get)** | ARM SDK (Compute) | No | Yes — ARM SDK enumeration |
| `azmcp compute vm get` **(get)** | ARM SDK (Compute) | No | Yes — ARM SDK enumeration |
| `azmcp compute vmss get` **(get)** | ARM SDK (Compute) | No | Yes — ARM SDK enumeration |
| `azmcp containerapps list` | Resource Graph (`Microsoft.App/containerApps`) | Yes — 50 | Yes — `ResultTruncated` flag |
| `azmcp functionapp get` **(get)** | ARM SDK (App Service) | No | Yes — ARM SDK enumeration |
| `azmcp functions language list` | Static manifest | N/A | No — static data |
| `azmcp servicefabric managedcluster node get` **(get)** | ARM SDK (Service Fabric) | No | Yes — ARM SDK enumeration |
| `azmcp virtualdesktop hostpool list` | ARM SDK (`GetHostPoolsAsync()`) | No | Yes — `AsyncPageable` |
| `azmcp virtualdesktop hostpool host list` | ARM SDK (`GetSessionHosts().GetAllAsync()`) | No | Yes — `GetAllAsync` |
| `azmcp virtualdesktop hostpool host user-list` | ARM SDK (`GetUserSessions().GetAllAsync()`) | No | Yes — `GetAllAsync` |

### Storage & File Systems

| Command | Backend | Limits Items | Service Supports Pagination |
|---|---|---|---|
| `azmcp storage account get` **(get)** | Resource Graph | Yes — 50 | Yes — `ResultTruncated` flag |
| `azmcp storage blob get` **(get)** | Data-plane SDK (Blob Storage) | No | Yes — SDK enumeration |
| `azmcp storage blob container get` **(get)** | Data-plane SDK (Blob Storage) | No | Yes — SDK enumeration |
| `azmcp storage table list` | Data-plane SDK (`TableServiceClient.QueryAsync()`) | No | Yes — `AsyncPageable` |
| `azmcp fileshares fileshare get` **(get)** | ARM SDK (Storage) | No | Yes — ARM SDK enumeration |
| `azmcp fileshares snapshot get` **(get)** | ARM SDK (Storage) | No | Yes — ARM SDK enumeration |
| `azmcp fileshares privateendpointconnection get` **(get)** | ARM SDK (Storage) | No | Yes — ARM SDK enumeration |
| `azmcp storagesync service get` **(get)** | ARM SDK (File Sync) | No | Yes — ARM SDK enumeration |
| `azmcp storagesync syncgroup get` **(get)** | ARM SDK (File Sync) | No | Yes — ARM SDK enumeration |
| `azmcp storagesync serverendpoint get` **(get)** | ARM SDK (File Sync) | No | Yes — ARM SDK enumeration |
| `azmcp storagesync registeredserver get` **(get)** | ARM SDK (File Sync) | No | Yes — ARM SDK enumeration |
| `azmcp storagesync cloudendpoint get` **(get)** | ARM SDK (File Sync) | No | Yes — ARM SDK enumeration |
| `azmcp managedlustre fs list` | ARM SDK (`GetAmlFileSystems()`) | No | Yes — ARM SDK enumeration |
| `azmcp managedlustre fs importjob get` **(get)** | ARM SDK (StorageCache) | No | Yes — ARM SDK enumeration |
| `azmcp managedlustre fs autoexportjob get` **(get)** | ARM SDK (StorageCache) | No | Yes — ARM SDK enumeration |
| `azmcp managedlustre fs autoimportjob get` **(get)** | ARM SDK (StorageCache) | No | Yes — ARM SDK enumeration |
| `azmcp managedlustre fs sku get` **(get)** | ARM SDK (StorageCache) | No | Yes — ARM SDK enumeration |

### Databases

| Command | Backend | Limits Items | Service Supports Pagination |
|---|---|---|---|
| `azmcp cosmos list` | ARM SDK (accounts) / Data-plane SDK (databases, containers) | No | Yes — `AsyncPageable` / stream iterators |
| `azmcp kusto cluster list` | Resource Graph (`Microsoft.Kusto/clusters`) | Yes — 50 | Yes — `ResultTruncated` flag |
| `azmcp kusto database list` | Kusto control command (`.show databases`) | No | No — full result set |
| `azmcp kusto table list` | Kusto control command (`.show tables`) | No | No — full result set |
| `azmcp mysql list` | ARM SDK (servers) / MySQL protocol (databases, tables) | Yes — 10,000 for DB/table queries | Yes — `GetAllAsync` (servers); no pagination for DB queries |
| `azmcp postgres list` | ARM SDK (servers) / PostgreSQL protocol (databases, tables) | No | Yes — `AsyncPageable` (servers); no pagination for DB queries |
| `azmcp redis list` | ARM SDK (Redis + Redis Enterprise) | No | Yes — `AsyncPageable` |
| `azmcp sql server get` **(get)** | ARM SDK (SQL) | No | Yes — ARM SDK enumeration |
| `azmcp sql db get` **(get)** | Resource Graph | Yes — `AreResultsTruncated` flag | Yes — Resource Graph |
| `azmcp sql server entra-admin list` | ARM SDK (`GetAllAsync()`) | No | Yes — `AsyncPageable` |
| `azmcp sql elastic-pool list` | Resource Graph (`Microsoft.Sql/servers/elasticPools`) | Yes — 50 | Yes — `ResultTruncated` flag |
| `azmcp sql server firewall-rule list` | ARM SDK (`GetSqlFirewallRules().GetAllAsync()`) | No | Yes — `AsyncPageable` |

### Monitoring, Diagnostics & Compliance

| Command | Backend | Limits Items | Service Supports Pagination |
|---|---|---|---|
| `azmcp advisor recommendation list` | Resource Graph (`advisorresources`) | Yes — 50 | Yes — `ResultTruncated` flag |
| `azmcp applicationinsights recommendation list` | ARM SDK (`GetApplicationInsightsComponentsAsync()`) | Yes — 20 hardcoded | Yes — `GetAllAsync` |
| `azmcp loadtesting testresource list` | ARM SDK (`GetLoadTestingResources()`) | No | Yes — ARM SDK enumeration |
| `azmcp loadtesting testrun get` **(get)** | Data-plane SDK (Load Testing) | No | Yes — SDK enumeration |
| `azmcp monitor activitylog list` | REST API (Activity Log `2017-03-01-preview`) | Yes — `--top` (default 10) | Yes — `nextLink` pagination |
| `azmcp monitor table list` | ARM SDK (`GetOperationalInsightsTables().GetAllAsync()`) | No | Yes — `GetAllAsync` |
| `azmcp monitor table type list` | ARM SDK (`GetOperationalInsightsTables().GetAllAsync()`) | No | Yes — `GetAllAsync` |
| `azmcp monitor webtest get` **(get)** | ARM SDK (Application Insights) | No | Yes — ARM SDK enumeration |
| `azmcp monitor workspace list` | ARM SDK (`GetOperationalInsightsWorkspacesAsync()`) | No | Yes — `AsyncPageable` |
| `azmcp resourcehealth availability-status get` **(get)** | REST API (Resource Health) | No | Yes — REST API pagination |
| `azmcp resourcehealth health-events list` | REST API (`Microsoft.ResourceHealth/events`) | No | Yes — OData query support |
| `azmcp workbooks list` | Resource Graph (KQL query) | Yes — 50 default, 1000 max (`--max-results`) | No — single KQL query |
| `azmcp quota region availability list` | REST API (region/resource type) | No | No — computed result set |

### Security & Key Management

| Command | Backend | Limits Items | Service Supports Pagination |
|---|---|---|---|
| `azmcp keyvault certificate get` **(get)** | Data-plane SDK (Key Vault) | No | Yes — Key Vault SDK pagination |
| `azmcp keyvault key get` **(get)** | Data-plane SDK (Key Vault) | No | Yes — Key Vault SDK pagination |
| `azmcp keyvault secret get` **(get)** | Data-plane SDK (Key Vault) | No | Yes — Key Vault SDK pagination |

### Messaging & Events

| Command | Backend | Limits Items | Service Supports Pagination |
|---|---|---|---|
| `azmcp eventgrid subscription list` | ARM SDK (`GetEventGridSubscriptions()`) | No | Yes — `AsyncPageable` |
| `azmcp eventgrid topic list` | ARM SDK (`GetEventGridTopics().GetAllAsync()`) | No | Yes — `AsyncPageable` |
| `azmcp eventhubs consumergroup get` **(get)** | ARM SDK (Event Hubs) | No | Yes — ARM SDK enumeration |
| `azmcp eventhubs eventhub get` **(get)** | ARM SDK (Event Hubs) | No | Yes — ARM SDK enumeration |
| `azmcp eventhubs namespace get` **(get)** | ARM SDK (Event Hubs) | No | Yes — ARM SDK enumeration |
| `azmcp signalr runtime get` **(get)** | ARM SDK (SignalR) | No | Yes — ARM SDK enumeration |

### Configuration & Container Registries

| Command | Backend | Limits Items | Service Supports Pagination |
|---|---|---|---|
| `azmcp acr registry list` | Resource Graph (`Microsoft.ContainerRegistry/registries`) | Yes — 50 | Yes — `ResultTruncated` flag |
| `azmcp acr registry repository list` | Data-plane SDK (`GetRepositoryNamesAsync()`) | No | Yes — `AsyncPageable` |
| `azmcp appconfig account list` | Resource Graph (`Microsoft.AppConfiguration/configurationStores`) | Yes — 50 | Yes — `ResultTruncated` flag |
| `azmcp appconfig keyvalue get` **(get)** | Data-plane SDK (App Configuration) | No | Yes — SDK enumeration |

### AI & Search

| Command | Backend | Limits Items | Service Supports Pagination |
|---|---|---|---|
| `azmcp foundryextensions knowledge index list` | Data-plane SDK (`GetIndicesAsync()`) | No | Yes — `AsyncPageable` |
| `azmcp foundryextensions openai models-list` | ARM SDK (`GetAllAsync()` on deployments) | No | Yes — `AsyncPageable` |
| `azmcp search index get` **(get)** | Data-plane SDK (AI Search) | No | Yes — SDK enumeration |
| `azmcp search knowledgebase get` **(get)** | Data-plane SDK (AI Search) | No | Yes — SDK enumeration |
| `azmcp search knowledgesource get` **(get)** | Data-plane SDK (AI Search) | No | Yes — SDK enumeration |
| `azmcp search service list` | ARM SDK (`GetSearchServicesAsync()`) | No | Yes — `AsyncPageable` |

### Other Azure Services

| Command | Backend | Limits Items | Service Supports Pagination |
|---|---|---|---|
| `azmcp datadog monitoredresources list` | ARM SDK (Datadog provider) | No | Limited — direct enumeration |
| `azmcp deviceregistry namespace list` | Resource Graph (`Microsoft.DeviceRegistry/namespaces`) | Yes — 50 | Yes — `ResultTruncated` flag |
| `azmcp grafana list` | Resource Graph (`Microsoft.Dashboard/grafana`) | Yes — 50 | Yes — `ResultTruncated` flag |
| `azmcp marketplace product list` | REST API (Marketplace) | No | Yes — `$skiptoken` cursor pagination |

---

## Summary of Item Limits

Commands that **do** limit the number of items returned:

| Command | Limit | Mechanism |
|---|---|---|
| `azmcp subscription list` | 10,000 | Hardcoded `MaxSubscriptions` constant |
| `azmcp monitor activitylog list` | 10 (default) | `--top` parameter; user-configurable |
| `azmcp applicationinsights recommendation list` | 20 | Hardcoded `MaxRecommendations` constant |
| `azmcp workbooks list` | 50 (default), 1000 (max) | `--max-results` parameter; KQL `limit` clause |
| `azmcp mysql list` | 10,000 | Hardcoded `MaxResultLimit` for database/table queries |
| Azure Resource Graph commands (12) | 50 | Default `limit` parameter in `ExecuteResourceQueryAsync`; returns `AreResultsTruncated` flag |

All other commands return **all** items from the underlying service without an MCP-layer limit. The underlying Azure services themselves generally support pagination via the Azure SDK's `AsyncPageable<T>` / `GetAllAsync()` patterns, which the MCP tools consume fully.
