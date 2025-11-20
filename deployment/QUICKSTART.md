# Quick Start: Deploy Azure MCP Server Remotely

## Prerequisites Checklist
- [ ] Azure CLI installed and logged in (`az login`)
- [ ] Docker installed and running
- [ ] PowerShell 7+ installed
- [ ] .NET SDK 9.0+ installed
- [ ] Azure AD app registration created (note Tenant ID and Client ID)

## One-Command Deployment

```powershell
cd deployment

./Deploy-RemoteMcp.ps1 `
    -ResourceGroup "rg-azmcp-dev" `
    -AzureAdTenantId "YOUR_TENANT_ID" `
    -AzureAdClientId "YOUR_CLIENT_ID"
```

## What Gets Created

| Resource | Name Pattern | Purpose |
|----------|-------------|---------|
| Container Registry | `azmcpremoteacr{env}` | Stores Docker images |
| Container App | `azmcp-remote-app-{env}` | Runs the MCP server |
| ACA Environment | `azmcp-remote-env-{env}` | Hosting environment |
| Managed Identity | `azmcp-remote-identity-{env}` | Service authentication |
| Log Analytics | `azmcp-remote-logs-{env}` | Logging and monitoring |

## After Deployment

### 1. Grant RBAC Permissions
The deployment outputs a managed identity principal ID. Assign roles:

```powershell
# Example: Grant Reader on subscription
az role assignment create `
    --assignee <PRINCIPAL_ID> `
    --role "Reader" `
    --scope "/subscriptions/<SUBSCRIPTION_ID>"
```

### 2. Test the Endpoint
```powershell
# Health check
curl https://<your-container-app-url>/health
```

### 3. View Logs
```powershell
az containerapp logs show `
    --name azmcp-remote-app-dev `
    --resource-group rg-azmcp-dev `
    --follow
```

## Environment Variables from launchSettings.json

These are automatically configured in the container:

- `ASPNETCORE_ENVIRONMENT`: Development/Production
- `ASPNETCORE_URLS`: http://+:8080
- `AzureAd__TenantId`: Your tenant ID
- `AzureAd__ClientId`: Your client ID
- `AzureAd__Instance`: https://login.microsoftonline.com/

## Command Line Arguments

The server runs with these args (from launchSettings.json):
```
server start --transport http --outgoing-auth-strategy UseHostingEnvironmentIdentity
```

## Common Issues

**Build fails**: Run `dotnet --version` (need 9.0+)  
**Docker fails**: Ensure Docker Desktop is running  
**ACR push fails**: Run `az acr login --name <acr-name>`  
**App won't start**: Check logs with `az containerapp logs show`

## Clean Up

```powershell
az group delete --name rg-azmcp-dev --yes
```

For detailed documentation, see [README.md](README.md)
