# Azure MCP Server Remote Deployment

This directory contains the deployment artifacts for running the Azure MCP Server as a remote HTTP service in Azure Container Apps.

## Overview

The deployment includes:
- **Dockerfile.remote**: Docker image configuration for the remote MCP server
- **remote-mcp-deploy.bicep**: Bicep template for Azure infrastructure
- **Deploy-RemoteMcp.ps1**: PowerShell deployment script that orchestrates the entire process

## Architecture

The deployed solution includes:
1. **Azure Container Registry (ACR)**: Stores the Docker image
2. **Azure Container Apps (ACA)**: Hosts the MCP server
3. **User-Assigned Managed Identity**: Provides identity for ACR access and Azure resource operations
4. **Log Analytics Workspace**: Collects logs from the Container App
5. **Container Apps Environment**: Hosting environment for the container app

## Environment Variables

The following environment variables from `launchSettings.json` are configured in the container:

- `ASPNETCORE_ENVIRONMENT`: Set to "Development" or "Production" based on environment parameter
- `ASPNETCORE_URLS`: Set to `http://+:8080` for container networking
- `AzureAd__TenantId`: Azure AD tenant ID for authentication
- `AzureAd__ClientId`: Azure AD client ID for the MCP server application
- `AzureAd__Instance`: Azure AD instance URL (default: https://login.microsoftonline.com/)

## Prerequisites

1. **Azure CLI**: Install from https://docs.microsoft.com/cli/azure/install-azure-cli
2. **Docker**: Install from https://docs.docker.com/get-docker/
3. **PowerShell 7+**: Install from https://docs.microsoft.com/powershell/scripting/install/installing-powershell
4. **.NET SDK 9.0+**: Required for building the application
5. **Azure Subscription**: With permissions to create resources

## Azure AD Application Setup

Before deploying, you need to register an Azure AD application:

1. Go to Azure Portal → Azure Active Directory → App registrations
2. Click "New registration"
3. Set a name (e.g., "Azure MCP Server Remote")
4. Set redirect URIs if needed for your authentication flow
5. After creation, note the:
   - **Application (client) ID** - Use as `AzureAdClientId`
   - **Directory (tenant) ID** - Use as `AzureAdTenantId`
6. Configure API permissions as needed for the MCP server to access Azure resources

## Deployment Steps

### Quick Start

```powershell
# Navigate to the deployment directory
cd deployment

# Run the deployment script
./Deploy-RemoteMcp.ps1 `
    -ResourceGroup "rg-azmcp-dev" `
    -Location "eastus" `
    -Environment "dev" `
    -AzureAdTenantId "your-tenant-id" `
    -AzureAdClientId "your-client-id"
```

### Full Parameter List

```powershell
./Deploy-RemoteMcp.ps1 `
    -ResourceGroup "rg-azmcp-prod" `
    -Location "westus2" `
    -Environment "prod" `
    -BaseName "myapp-mcp" `
    -ImageTag "v1.0.0" `
    -AzureAdTenantId "your-tenant-id" `
    -AzureAdClientId "your-client-id" `
    -AzureAdInstance "https://login.microsoftonline.com/"
```

### Parameters

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| ResourceGroup | Yes | - | Azure resource group name (created if doesn't exist) |
| Location | No | eastus | Azure region for deployment |
| Environment | No | dev | Environment name (dev/test/prod) for resource naming |
| BaseName | No | azmcp-remote | Base name for all resources |
| ImageTag | No | latest | Docker image tag |
| AzureAdTenantId | Yes | - | Azure AD tenant ID |
| AzureAdClientId | Yes | - | Azure AD client ID for MCP server |
| AzureAdInstance | No | https://login.microsoftonline.com/ | Azure AD instance URL |
| SkipBuild | No | false | Skip building the server binaries |
| SkipDockerBuild | No | false | Skip building the Docker image |

## What the Script Does

1. **Builds the server** for Linux x64 using `eng/scripts/Build-Code.ps1`
2. **Creates a Docker image** using `Dockerfile.remote`
3. **Ensures Azure CLI is authenticated** and creates the resource group
4. **Deploys infrastructure** using the Bicep template:
   - Azure Container Registry
   - Log Analytics Workspace
   - Managed Identity with AcrPull role
   - Container Apps Environment
   - Container App with health checks and auto-scaling
5. **Pushes the Docker image** to ACR
6. **Updates the Container App** to use the new image

## Post-Deployment

### Assign RBAC Roles

The Container App uses a managed identity to access Azure resources. After deployment, assign appropriate roles:

```powershell
# Get the managed identity principal ID from deployment output
$ManagedIdentityId = "<principal-id-from-output>"

# Example: Grant Reader access to a subscription
az role assignment create `
    --assignee $ManagedIdentityId `
    --role "Reader" `
    --scope "/subscriptions/<subscription-id>"

# Example: Grant Contributor access to a specific resource group
az role assignment create `
    --assignee $ManagedIdentityId `
    --role "Contributor" `
    --scope "/subscriptions/<subscription-id>/resourceGroups/<resource-group>"
```

### Access the Service

The deployment outputs the Container App URL:

```
Container App URL: https://azmcp-remote-app-dev.nicegrass-12345678.eastus.azurecontainerapps.io
```

Health check endpoint:
```
https://<container-app-url>/health
```

### View Logs

```powershell
# Stream logs from the container app
az containerapp logs show `
    --name azmcp-remote-app-dev `
    --resource-group rg-azmcp-dev `
    --follow

# View logs in Log Analytics
az monitor log-analytics query `
    --workspace <workspace-id> `
    --analytics-query "ContainerAppConsoleLogs_CL | where ContainerAppName_s == 'azmcp-remote-app-dev' | order by TimeGenerated desc | take 50"
```

## Scaling Configuration

The Container App is configured with auto-scaling:
- **Min replicas**: 1
- **Max replicas**: 3
- **Scale rule**: HTTP scaling based on concurrent requests (10 per instance)

Modify these in the Bicep template if needed.

## Resource Cleanup

To delete all deployed resources:

```powershell
az group delete --name rg-azmcp-dev --yes --no-wait
```

## Troubleshooting

### Build Fails
- Ensure .NET SDK 9.0+ is installed
- Run `dotnet --version` to verify
- Check build logs in `.work/build/`

### Docker Build Fails
- Ensure Docker Desktop is running
- Check Docker daemon is accessible
- Verify publish directory exists: `.work/build/Azure.Mcp.Server/linux-x64-untrimmed`

### ACR Push Fails
- Verify ACR login: `az acr login --name <acr-name>`
- Check ACR permissions
- Ensure Docker image was built successfully

### Container App Not Starting
- Check container logs: `az containerapp logs show`
- Verify environment variables are set correctly
- Check health endpoint is responding
- Verify managed identity has AcrPull role on ACR

### Authentication Issues
- Verify Azure AD tenant ID and client ID are correct
- Ensure the managed identity has appropriate RBAC roles
- Check the Container App environment variables

## Local Testing

To test the Docker image locally:

```powershell
# Build the image
docker build -t azure-mcp-server:local -f Dockerfile.remote --build-arg PUBLISH_DIR=.work/build/Azure.Mcp.Server/linux-x64-untrimmed .

# Run locally
docker run -it --rm -p 8080:8080 `
    -e AzureAd__TenantId="your-tenant-id" `
    -e AzureAd__ClientId="your-client-id" `
    -e AzureAd__Instance="https://login.microsoftonline.com/" `
    azure-mcp-server:local

# Test health endpoint
curl http://localhost:8080/health
```

## Security Considerations

1. **Managed Identity**: Uses system-assigned managed identity for Azure resource access
2. **No Admin Credentials**: ACR admin user is disabled, using managed identity for authentication
3. **HTTPS Only**: Container Apps ingress is configured for HTTPS
4. **Environment Variables**: Sensitive configuration is managed through Container App environment variables
5. **RBAC**: Follow principle of least privilege when assigning roles to the managed identity

## Additional Resources

- [Azure Container Apps Documentation](https://docs.microsoft.com/azure/container-apps/)
- [Azure Container Registry Documentation](https://docs.microsoft.com/azure/container-registry/)
- [Managed Identity Documentation](https://docs.microsoft.com/azure/active-directory/managed-identities-azure-resources/)
- [Azure MCP Server Documentation](../README.md)
