#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Simple deployment script for Azure MCP Server using Azure CLI only
.DESCRIPTION
    Alternative deployment approach using only Azure CLI commands
    This is useful when you want more control or have issues with the main deployment script
.PARAMETER ResourceGroupName
    The name of the resource group to deploy to
.PARAMETER ContainerRegistryName
    Name of the Azure Container Registry
.PARAMETER Location
    The Azure region to deploy to (default: eastus)
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory = $true)]
    [string]$ContainerRegistryName,
    
    [Parameter(Mandatory = $false)]
    [string]$Location = "eastus"
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 Simple Azure MCP Server deployment starting..." -ForegroundColor Green

# Create resource group
Write-Host "🔄 Creating resource group..." -ForegroundColor Yellow
az group create --name $ResourceGroupName --location $Location

# Create container registry
Write-Host "🔄 Creating container registry..." -ForegroundColor Yellow
az acr create --resource-group $ResourceGroupName --name $ContainerRegistryName --sku Basic --admin-enabled true

# Build and push image
$imageName = "azmcp-server:latest"
$serverProjectPath = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$serverProjectPath = Join-Path $serverProjectPath "servers\Azure.Mcp.Server"

Write-Host "🔄 Building application..." -ForegroundColor Yellow
Push-Location $serverProjectPath
try {
    dotnet publish --configuration Release --output "bin\Release\net9.0\publish" --self-contained false
} finally {
    Pop-Location
}

Write-Host "🔄 Building and pushing Docker image..." -ForegroundColor Yellow
$rootPath = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Push-Location $rootPath
try {
    az acr build --registry $ContainerRegistryName --image $imageName --build-arg PUBLISH_DIR="servers/Azure.Mcp.Server/bin/Release/net9.0/publish" .
} finally {
    Pop-Location
}

# Get ACR login server
$acrLoginServer = az acr show --name $ContainerRegistryName --query "loginServer" -o tsv
$fullImageName = "$acrLoginServer/$imageName"

Write-Host "🔄 Deploying to Container Apps..." -ForegroundColor Yellow

# Create Container Apps environment
$envName = "env-azmcp-$(Get-Random -Maximum 1000)"
az containerapp env create --name $envName --resource-group $ResourceGroupName --location $Location

# Create Container App
$appName = "app-azmcp-$(Get-Random -Maximum 1000)"
az containerapp create `
    --name $appName `
    --resource-group $ResourceGroupName `
    --environment $envName `
    --image $fullImageName `
    --target-port 1031 `
    --ingress external `
    --cpu 1.0 `
    --memory 2.0Gi `
    --min-replicas 1 `
    --max-replicas 3 `
    --env-vars "ASPNETCORE_ENVIRONMENT=Production" "ASPNETCORE_URLS=http://+:1031" "AzureAd__TenantId=70a036f6-8e4d-4615-bad6-149c02e7720d" "AzureAd__ClientId=ca1e0302-d50a-47d7-b5e6-7aff49884bce" "AzureAd__Instance=https://login.microsoftonline.com/"

# Get the app URL
$appUrl = az containerapp show --name $appName --resource-group $ResourceGroupName --query "properties.configuration.ingress.fqdn" -o tsv

Write-Host "✅ Deployment completed!" -ForegroundColor Green
Write-Host "🌐 Application URL: https://$appUrl" -ForegroundColor Cyan
Write-Host "🔗 Resource Group: https://portal.azure.com/#@/resource/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$ResourceGroupName/overview" -ForegroundColor Cyan