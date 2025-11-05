#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Deploys the Azure MCP Server to Azure Container Apps
.DESCRIPTION
    This script handles the complete deployment pipeline:
    1. Builds the Docker image
    2. Pushes it to Azure Container Registry
    3. Deploys the infrastructure using Bicep
    4. Validates the deployment
.PARAMETER ResourceGroupName
    The name of the resource group to deploy to
.PARAMETER Location
    The Azure region to deploy to (default: eastus)
.PARAMETER EnvironmentSuffix
    Environment suffix for naming (default: dev)
.PARAMETER SubscriptionId
    Azure subscription ID (optional, uses current subscription)
.PARAMETER ContainerRegistryName
    Name of the Azure Container Registry (will be created if doesn't exist)
.PARAMETER SkipBuild
    Skip building the Docker image (use existing image)
.PARAMETER ValidateOnly
    Only validate the deployment, don't actually deploy
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory = $false)]
    [string]$Location = "westus2",
    
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentSuffix = "dev",
    
    [Parameter(Mandatory = $false)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory = $true)]
    [string]$ContainerRegistryName,
    
    [Parameter(Mandatory = $false)]
    [switch]$SkipBuild,
    
    [Parameter(Mandatory = $false)]
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"

# Script directory and project paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$serverProjectPath = Join-Path $projectRoot "servers\Azure.Mcp.Server\src"
$infraPath = Join-Path $projectRoot "infra"

Write-Host "🚀 Starting Azure MCP Server deployment..." -ForegroundColor Green
Write-Host "   Resource Group: $ResourceGroupName" -ForegroundColor Cyan
Write-Host "   Location: $Location" -ForegroundColor Cyan
Write-Host "   Environment: $EnvironmentSuffix" -ForegroundColor Cyan

# Set subscription if provided
if ($SubscriptionId) {
    Write-Host "🔄 Setting Azure subscription to $SubscriptionId..." -ForegroundColor Yellow
    az account set --subscription $SubscriptionId
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to set subscription"
    }
}

# Get current subscription info
$currentSub = az account show --query "{id:id, name:name}" -o json | ConvertFrom-Json
Write-Host "✅ Using subscription: $($currentSub.name) ($($currentSub.id))" -ForegroundColor Green

# Create resource group if it doesn't exist
Write-Host "🔄 Ensuring resource group exists..." -ForegroundColor Yellow
az group create --name $ResourceGroupName --location $Location --output none
if ($LASTEXITCODE -ne 0) {
    throw "Failed to create resource group"
}

# Create or get Container Registry
Write-Host "🔄 Setting up Azure Container Registry..." -ForegroundColor Yellow
$registryExists = az acr show --name $ContainerRegistryName --query "name" -o tsv 2>$null
if (-not $registryExists) {
    Write-Host "   Creating new Container Registry: $ContainerRegistryName" -ForegroundColor Cyan
    az acr create --resource-group $ResourceGroupName --name $ContainerRegistryName --sku Basic --admin-enabled true --output none
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create Container Registry"
    }
} else {
    Write-Host "   Using existing Container Registry: $ContainerRegistryName" -ForegroundColor Cyan
}

# Get ACR login server
$acrLoginServer = az acr show --name $ContainerRegistryName --query "loginServer" -o tsv
if ($LASTEXITCODE -ne 0) {
    throw "Failed to get ACR login server"
}

$imageName = "azmcp-server"
$imageTag = "latest"
$fullImageName = "$acrLoginServer/${imageName}:${imageTag}"

if (-not $SkipBuild) {
    # Build and publish the application
    Write-Host "🔄 Building and publishing the application..." -ForegroundColor Yellow
    $publishPath = Join-Path $serverProjectPath "bin\Release\net9.0\linux-x64\publish"
    
    # Clean previous builds
    if (Test-Path $publishPath) {
        Remove-Item $publishPath -Recurse -Force
    }
    
    # Publish the application for Linux
    Push-Location $serverProjectPath
    try {
        dotnet publish --configuration Release --runtime linux-x64 --self-contained false --output "bin\Release\net9.0\linux-x64\publish"
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to publish application"
        }
    } finally {
        Pop-Location
    }
    
    # Build Docker image
    Write-Host "🔄 Building Docker image..." -ForegroundColor Yellow
    Push-Location $projectRoot
    try {
        docker build --build-arg PUBLISH_DIR="servers/Azure.Mcp.Server/src/bin/Release/net9.0/linux-x64/publish" -t $fullImageName .
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to build Docker image"
        }
    } finally {
        Pop-Location
    }
    
    # Login to ACR and push image
    Write-Host "🔄 Pushing image to Azure Container Registry..." -ForegroundColor Yellow
    az acr login --name $ContainerRegistryName
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to login to ACR"
    }
    
    docker push $fullImageName
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to push Docker image"
    }
    
    Write-Host "✅ Docker image built and pushed: $fullImageName" -ForegroundColor Green
} else {
    Write-Host "⏭️ Skipping Docker build (using existing image: $fullImageName)" -ForegroundColor Yellow
}

# Update parameters file with the image name
Write-Host "🔄 Updating deployment parameters..." -ForegroundColor Yellow
$parametersFile = Join-Path $infraPath "main.parameters.json"
$parameters = Get-Content $parametersFile | ConvertFrom-Json
$parameters.parameters.containerImage.value = $fullImageName
# Ensure the container registry name in parameters matches the one supplied to this script to avoid stale values
if ($parameters.parameters.containerRegistryName.value -ne $ContainerRegistryName) {
    Write-Host "   Syncing containerRegistryName parameter: '$($parameters.parameters.containerRegistryName.value)' -> '$ContainerRegistryName'" -ForegroundColor Cyan
    $parameters.parameters.containerRegistryName.value = $ContainerRegistryName
}
$parameters | ConvertTo-Json -Depth 10 | Set-Content $parametersFile

if ($ValidateOnly) {
    # Validate deployment
    Write-Host "🔍 Validating deployment..." -ForegroundColor Yellow
    az deployment group what-if `
        --resource-group $ResourceGroupName `
        --template-file (Join-Path $infraPath "main.bicep") `
        --parameters (Join-Path $infraPath "main.parameters.json")
    
    if ($LASTEXITCODE -ne 0) {
        throw "Deployment validation failed"
    }
    
    Write-Host "✅ Deployment validation completed successfully!" -ForegroundColor Green
    return
}

# Deploy infrastructure
Write-Host "🔄 Deploying infrastructure..." -ForegroundColor Yellow
$deploymentName = "azmcp-deployment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

$deploymentResult = az deployment group create `
    --resource-group $ResourceGroupName `
    --name $deploymentName `
    --template-file (Join-Path $infraPath "main.bicep") `
    --parameters (Join-Path $infraPath "main.parameters.json") `
    --query "properties.outputs" -o json | ConvertFrom-Json

if ($LASTEXITCODE -ne 0) {
    throw "Deployment failed"
}

# Extract outputs
$containerAppUrl = $deploymentResult.containerAppUrl.value
$resourceGroupUrl = $deploymentResult.resourceGroupUrl.value
$managedIdentityPrincipalId = $deploymentResult.managedIdentityPrincipalId.value

Write-Host "✅ Deployment completed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "🌐 Application URL: $containerAppUrl" -ForegroundColor Cyan
Write-Host "🔗 Resource Group: $resourceGroupUrl" -ForegroundColor Cyan
Write-Host "🆔 Managed Identity Principal ID: $managedIdentityPrincipalId" -ForegroundColor Cyan

# Test the deployment
Write-Host "🔄 Testing the deployed application..." -ForegroundColor Yellow
try {
    # Wait a moment for the app to be ready
    Start-Sleep -Seconds 30
    
    $healthResponse = Invoke-RestMethod -Uri "$containerAppUrl/health" -Method Get -TimeoutSec 30
    Write-Host "✅ Health check passed!" -ForegroundColor Green
    
    # Test a simple MCP endpoint if available
    try {
        $mcpResponse = Invoke-RestMethod -Uri "$containerAppUrl" -Method Post -ContentType "application/json" -Body '{"jsonrpc":"2.0","method":"tools/list","id":1}' -TimeoutSec 30
        Write-Host "✅ MCP server is responding!" -ForegroundColor Green
    } catch {
        Write-Host "⚠️ MCP endpoint test failed, but this might be expected without proper authentication" -ForegroundColor Yellow
    }
} catch {
    Write-Host "⚠️ Application health check failed. Check the logs in Azure Portal." -ForegroundColor Yellow
    Write-Host "   This might be normal during initial startup." -ForegroundColor Gray
}

Write-Host ""
Write-Host "🎉 Deployment completed! Your Azure MCP Server is running at:" -ForegroundColor Green
Write-Host "   $containerAppUrl" -ForegroundColor White
Write-Host ""
Write-Host "📋 Next Steps:" -ForegroundColor Yellow
Write-Host "   1. Configure authentication and authorization as needed" -ForegroundColor Gray
Write-Host "   2. Set up monitoring and alerting" -ForegroundColor Gray
Write-Host "   3. Configure custom domain if required" -ForegroundColor Gray
Write-Host "   4. Review security settings and network access" -ForegroundColor Gray