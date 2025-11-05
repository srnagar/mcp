// Main Bicep template for deploying Azure MCP Server to Azure Container Apps
// This template creates all necessary resources for hosting the azmcp.exe as a container app

@description('The name of the application')
param appName string = 'azmcp-server'

@description('The location for all resources')
param location string = resourceGroup().location

@description('The environment suffix (e.g., dev, test, prod)')
param environmentSuffix string = 'dev'

@description('The container image to deploy')
param containerImage string

@description('The Azure Container Registry name (if using ACR)')
param containerRegistryName string = ''

@description('The target port the container listens on')
param targetPort int = 1031

@description('Minimum number of replicas')
@minValue(0)
@maxValue(10)
param minReplicas int = 1

@description('Maximum number of replicas')
@minValue(1)
@maxValue(30)
param maxReplicas int = 3

@description('CPU allocation for the container (in cores)')
param cpuCores string = '0.5'

@description('Memory allocation for the container (in Gi)')
param memorySize string = '1.0Gi'

@description('Azure AD Tenant ID for authentication')
param azureAdTenantId string

@description('Azure AD Client ID for authentication')
param azureAdClientId string

@description('Tags to apply to all resources')
param tags object = {
  environment: environmentSuffix
  application: 'azure-mcp-server'
  deployedBy: 'bicep'
}

// Generate unique names based on app name and environment
var uniqueSuffix = substring(uniqueString(resourceGroup().id), 0, 6)
var containerAppName = '${appName}-${environmentSuffix}-${uniqueSuffix}'
var containerAppEnvName = '${appName}-env-${environmentSuffix}-${uniqueSuffix}'
var logAnalyticsName = '${appName}-logs-${environmentSuffix}-${uniqueSuffix}'
var appInsightsName = '${appName}-ai-${environmentSuffix}-${uniqueSuffix}'

// Reference existing ACR if provided
resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = if (!empty(containerRegistryName)) {
  name: containerRegistryName
}

// Log Analytics Workspace for monitoring
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 90
    features: {
      searchVersion: 1
      legacy: 0
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

// Application Insights for telemetry
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    Flow_Type: 'Bluefield'
    Request_Source: 'rest'
  }
}

// Container Apps Environment
resource containerAppEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppEnvName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    zoneRedundant: false
  }
}

// Container App
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    environmentId: containerAppEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: targetPort
        transport: 'http'
        allowInsecure: false
        traffic: [
          {
            weight: 100
            latestRevision: true
          }
        ]
      }
      // Use ACR admin credentials for image pull to avoid timing issues with role assignments
      secrets: !empty(containerRegistryName) ? [
        {
          name: 'acr-password'
          value: acr.listCredentials().passwords[0].value
        }
      ] : []
      registries: !empty(containerRegistryName) ? [
        {
          server: acr.properties.loginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ] : []
    }
    template: {
      containers: [
        {
          name: containerAppName
          image: containerImage
          // Override default ENTRYPOINT to ensure HTTP mode is enabled so that
          // the health probes and ingress succeed. Without the
          // --run-as-remote-http-service flag the server defaults to STDIO
          // transport and does not expose an HTTP listener, causing probe
          // failures in Azure Container Apps.
          command: [
            './azmcp'
            'server'
            'start'
            '--run-as-remote-http-service'
            '--outgoing-auth-strategy'
            'UseOnBehalfOf'
          ]
          resources: {
            cpu: json(cpuCores)
            memory: memorySize
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Development'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:${targetPort}'
            }
            {
              name: 'AzureAd__TenantId'
              value: azureAdTenantId
            }
            {
              name: 'AzureAd__ClientId'
              value: '${azureAdClientId}'
            }
            {
              name: 'AzureAd__Instance'
              value: environment().authentication.loginEndpoint
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: azureAdClientId
            }
            {
              name: 'AZURE_TENANT_ID'
              value: azureAdTenantId
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsights.properties.ConnectionString
            }
            {
              name: 'MSI_ENDPOINT'
              value: 'http://169.254.169.254/metadata/identity/oauth2/token'
            }
            {
              name: 'MSI_SECRET'
              value: ''
            }
            {
              name: 'AZURE_MCP_INCLUDE_PRODUCTION_CREDENTIALS'
              value: 'true'
            }
          ]
          probes: []
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-rule'
            http: {
              metadata: {
                concurrentRequests: '10'
              }
            }
          }
        ]
      }
    }
  }
}

// Grant AcrPull role to the Container App's managed identity
// TODO: Role assignment management - currently commented out due to existing assignments
// Consider using a deployment script or separate ARM template for idempotent role management
/*
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(containerRegistryName)) {
  name: guid('${containerApp.name}-acrpull-${uniqueString(resourceGroup().id, containerApp.name)}')
  scope: acr
  properties: {
    principalId: containerApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d') // AcrPull role
    principalType: 'ServicePrincipal'
  }
}
*/

// Output important information
@description('The FQDN of the deployed container app')
output containerAppFQDN string = containerApp.properties.configuration.ingress.fqdn

@description('The URL of the deployed container app')
output containerAppUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'

@description('The name of the container app')
output containerAppName string = containerApp.name

@description('The resource group URL in Azure Portal')
output resourceGroupUrl string = 'https://portal.azure.com/#@${tenant().tenantId}/resource/subscriptions/${subscription().subscriptionId}/resourceGroups/${resourceGroup().name}/overview'

@description('The system-assigned managed identity principal ID')
output managedIdentityPrincipalId string = containerApp.identity.principalId

@description('The Application Insights connection string')
output applicationInsightsConnectionString string = appInsights.properties.ConnectionString