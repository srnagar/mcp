# Azure MCP Server Deployment Guide

This directory contains Infrastructure as Code (IaC) files and deployment scripts for deploying the Azure MCP Server (azmcp.exe) to Azure Container Apps.

## Files Overview

- **`main.bicep`** - Main Bicep template that creates all necessary Azure resources
- **`main.parameters.json`** - Parameters file for the Bicep template
- **`deploy.ps1`** - PowerShell deployment script that handles the complete deployment pipeline
- **`README.md`** - This documentation file

## Prerequisites

Before deploying, ensure you have:

1. **Azure CLI** installed and authenticated (`az login`)
2. **Docker** installed for building container images
3. **PowerShell 7.0+** for running the deployment script
4. **.NET 9.0 SDK** for building the application
5. **Azure subscription** with appropriate permissions

## Quick Start

1. **Clone and navigate to the repository:**
   ```powershell
   git clone https://github.com/microsoft/mcp.git
   cd mcp
   ```

2. **Run the deployment script:**
   ```powershell
   ./infra/deploy.ps1 -ResourceGroupName "rg-azmcp-dev" -ContainerRegistryName "acrazmcpdev123"
   ```

## Deployment Options

### Basic Deployment
```powershell
./infra/deploy.ps1 -ResourceGroupName "rg-azmcp-dev" -ContainerRegistryName "acrazmcpdev123"
```

### Custom Environment and Location
```powershell
./infra/deploy.ps1 `
  -ResourceGroupName "rg-azmcp-prod" `
  -ContainerRegistryName "acrazmcpprod123" `
  -Location "westus2" `
  -EnvironmentSuffix "prod"
```

### Validation Only (What-If)
```powershell
./infra/deploy.ps1 `
  -ResourceGroupName "rg-azmcp-dev" `
  -ContainerRegistryName "acrazmcpdev123" `
  -ValidateOnly
```

### Skip Docker Build (Use Existing Image)
```powershell
./infra/deploy.ps1 `
  -ResourceGroupName "rg-azmcp-dev" `
  -ContainerRegistryName "acrazmcpdev123" `
  -SkipBuild
```

## Deployment Script Parameters

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `ResourceGroupName` | ✅ | - | Name of the Azure resource group |
| `ContainerRegistryName` | ✅ | - | Name of the Azure Container Registry |
| `Location` | ❌ | `eastus` | Azure region for deployment |
| `EnvironmentSuffix` | ❌ | `dev` | Environment suffix for naming |
| `SubscriptionId` | ❌ | Current | Azure subscription ID |
| `SkipBuild` | ❌ | `false` | Skip Docker image build |
| `ValidateOnly` | ❌ | `false` | Only validate, don't deploy |

## What Gets Deployed

The deployment creates the following Azure resources:

1. **Log Analytics Workspace** - For monitoring and logging
2. **Application Insights** - For application telemetry
3. **Container Apps Environment** - Managed environment for container apps
4. **Container App** - The main application hosting the Azure MCP Server
5. **Azure Container Registry** - For storing the Docker image (if it doesn't exist)

## Configuration

### Environment Variables

The deployed container app includes these environment variables:

- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://+:1031`
- `AzureAd__TenantId` - Azure AD tenant ID for authentication
- `AzureAd__ClientId` - Azure AD client ID for authentication
- `AzureAd__Instance=https://login.microsoftonline.com/`
- `APPLICATIONINSIGHTS_CONNECTION_STRING` - Application Insights connection string

### Health Checks

The container app is configured with health checks:

- **Liveness Probe**: `GET /health` - Checks if the app is running
- **Readiness Probe**: `GET /health/ready` - Checks if the app is ready to serve traffic

### Scaling

Default scaling configuration:
- **Min Replicas**: 1
- **Max Replicas**: 3
- **Scale Rule**: HTTP-based with 10 concurrent requests threshold

### Resources

Default resource allocation per container:
- **CPU**: 1.0 cores
- **Memory**: 2.0 Gi

## Customization

### Modifying Parameters

Edit `main.parameters.json` to customize:

```json
{
  "parameters": {
    "appName": { "value": "my-custom-app" },
    "cpuCores": { "value": "2.0" },
    "memorySize": { "value": "4.0Gi" },
    "maxReplicas": { "value": 5 }
  }
}
```

### Advanced Configuration

For advanced scenarios, modify `main.bicep`:

- Add custom domains
- Configure private networking
- Add additional environment variables
- Modify scaling rules
- Add additional Azure services

## Authentication and Security

The deployment includes:

1. **System-Assigned Managed Identity** - For secure access to Azure resources
2. **HTTPS Only** - All traffic is encrypted
3. **Azure AD Integration** - Pre-configured for authentication
4. **Private Container Registry** - Images stored securely

### Granting Permissions

After deployment, you may need to grant the managed identity permissions to Azure resources:

```powershell
# Get the managed identity principal ID (output from deployment)
$principalId = "your-managed-identity-principal-id"

# Grant Contributor access to a resource group
az role assignment create `
  --assignee $principalId `
  --role "Contributor" `
  --scope "/subscriptions/{subscription-id}/resourceGroups/{resource-group}"
```

## Monitoring and Troubleshooting

### View Logs

```powershell
# View container app logs
az containerapp logs show `
  --name "your-container-app-name" `
  --resource-group "your-resource-group"
```

### Monitor with Application Insights

The deployment includes Application Insights for comprehensive monitoring:

- Request tracing
- Dependency tracking
- Performance metrics
- Custom telemetry

### Common Issues

1. **Container fails to start**
   - Check Docker image exists in ACR
   - Verify port configuration (1031)
   - Check environment variables

2. **Authentication issues**
   - Verify Azure AD tenant ID and client ID
   - Check managed identity permissions

3. **Health check failures**
   - Ensure application implements `/health` endpoints
   - Check resource allocation (CPU/memory)

## Development Workflow

For development deployments:

1. **Build and test locally:**
   ```powershell
   cd servers/Azure.Mcp.Server
   dotnet run --launch-profile debug-remotemcp
   ```

2. **Deploy to development environment:**
   ```powershell
   ./infra/deploy.ps1 -ResourceGroupName "rg-azmcp-dev" -ContainerRegistryName "acrazmcpdev123"
   ```

3. **Test the deployment:**
   ```powershell
   # The deployment script automatically tests the health endpoints
   # Additional testing can be done via the provided URL
   ```

## Cost Optimization

To minimize costs:

1. Use smaller resource allocations for development
2. Set appropriate min/max replica counts
3. Consider using Azure Container Instances for testing
4. Monitor usage with Azure Cost Management

## Support and Troubleshooting

For issues:

1. Check the deployment logs during execution
2. Review Azure Portal for resource status
3. Use Application Insights for application-level issues
4. Consult the main repository documentation

## Next Steps

After successful deployment:

1. Configure custom authentication if needed
2. Set up monitoring and alerting
3. Configure custom domains and SSL certificates
4. Implement CI/CD pipelines for automated deployments
5. Review and enhance security settings