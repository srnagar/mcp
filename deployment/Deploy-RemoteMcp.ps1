#!/usr/bin/env pwsh
#Requires -Version 7

<#
.SYNOPSIS
    Deploys the Azure MCP Server to Azure Container Apps via Azure Container Registry

.DESCRIPTION
    This script performs the following steps:
    1. Builds the Azure MCP Server for Linux x64
    2. Creates a Docker image with the server
    3. Pushes the image to Azure Container Registry
    4. Deploys the Container App using Bicep template

.PARAMETER ResourceGroup
    The Azure resource group name (will be created if it doesn't exist)

.PARAMETER Location
    The Azure region for deployment (default: eastus)

.PARAMETER Environment
    Environment name (dev, test, prod) for resource naming (default: dev)

.PARAMETER BaseName
    Base name for all resources (default: azmcp-remote)

.PARAMETER ImageTag
    Docker image tag (default: git commit SHA)

.PARAMETER AzureAdTenantId
    Azure AD Tenant ID for authentication

.PARAMETER AzureAdClientId
    Azure AD Client ID for the MCP Server

.PARAMETER SkipBuild
    Skip the build step if the Linux binaries already exist

.PARAMETER SkipDockerBuild
    Skip the Docker build step if the image already exists

.PARAMETER BuildConfiguration
    Build configuration (Debug or Release). Default is Debug for easier diagnostics.

.PARAMETER OperatingSystem
    Target operating system (linux, windows, osx). Default is linux.

.PARAMETER Architecture
    Target architecture (x64, arm64). Default is x64.

.EXAMPLE
    .\Deploy-RemoteMcp.ps1 -ResourceGroup "rg-azmcp-dev" -AzureAdTenantId "your-tenant-id" -AzureAdClientId "your-client-id"

.EXAMPLE
    .\Deploy-RemoteMcp.ps1 -ResourceGroup "rg-azmcp-prod" -Environment "prod" -Location "westus2" -BuildConfiguration "Release" -AzureAdTenantId "your-tenant-id" -AzureAdClientId "your-client-id"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory = $false)]
    [string]$Location = "eastus",

    [Parameter(Mandatory = $false)]
    [string]$Environment = "dev",

    [Parameter(Mandatory = $false)]
    [string]$BaseName = "azmcp-remote",

    [Parameter(Mandatory = $false)]
    [string]$ImageTag,

    [Parameter(Mandatory = $true)]
    [string]$AzureAdTenantId,

    [Parameter(Mandatory = $true)]
    [string]$AzureAdClientId,

    [Parameter(Mandatory = $false)]
    [string]$AzureAdInstance = "https://login.microsoftonline.com/",

    [Parameter(Mandatory = $false)]
    [ValidateSet('Debug', 'Release')]
    [string]$BuildConfiguration = 'Debug',

    [Parameter(Mandatory = $false)]
    [ValidateSet('linux', 'windows', 'osx')]
    [string]$OperatingSystem = 'linux',

    [Parameter(Mandatory = $false)]
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',

    [Parameter(Mandatory = $false)]
    [switch]$SkipBuild,

    [Parameter(Mandatory = $false)]
    [switch]$SkipDockerBuild
)

$ErrorActionPreference = 'Stop'

# Get script directory and repo root
$ScriptDir = Split-Path -Parent $PSCommandPath
$RepoRoot = Split-Path -Parent $ScriptDir
$RepoRoot = $RepoRoot.Replace('\', '/')

# Import common functions
. "$RepoRoot/eng/common/scripts/common.ps1"

# Get git commit SHA if ImageTag not specified
if (-not $ImageTag) {
    Push-Location $RepoRoot
    try {
        $GitCommit = git rev-parse --short=8 HEAD 2>$null
        if ($GitCommit -and $LASTEXITCODE -eq 0) {
            $ImageTag = $GitCommit
            Write-Host "Using git commit SHA as image tag: $ImageTag" -ForegroundColor Cyan
        } else {
            $ImageTag = "latest"
            Write-Host "Git commit not found, using 'latest' as image tag" -ForegroundColor Yellow
        }
    }
    finally {
        Pop-Location
    }
}

# Resource names (ACR doesn't allow hyphens)
$AcrName = $BaseName.Replace('-', '') + "acr" + $Environment
$ImageName = "azure-mcp-server"
$FullImageName = "${AcrName}.azurecr.io/${ImageName}:${ImageTag}"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Azure MCP Server Remote Deployment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Resource Group: $ResourceGroup" -ForegroundColor Yellow
Write-Host "Location: $Location" -ForegroundColor Yellow
Write-Host "Environment: $Environment" -ForegroundColor Yellow
Write-Host "ACR Name: $AcrName" -ForegroundColor Yellow
Write-Host "Image: $FullImageName" -ForegroundColor Yellow
Write-Host "========================================`n" -ForegroundColor Cyan

# Step 1: Build the server for Linux
if (-not $SkipBuild) {
    Write-Host "`n[Step 1/6] Building Azure MCP Server ($BuildConfiguration configuration for $OperatingSystem-$Architecture)..." -ForegroundColor Green
    $BuildScript = "$RepoRoot/eng/scripts/Build-Code.ps1"
    
    $BuildArgs = @{
        ServerName = "Azure.Mcp.Server"
        OperatingSystem = $OperatingSystem
        Architecture = $Architecture
    }
    
    # Add ReleaseBuild flag only if BuildConfiguration is Release
    if ($BuildConfiguration -eq 'Release') {
        $BuildArgs['ReleaseBuild'] = $true
    }
    
    & $BuildScript @BuildArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
    Write-Host "✓ Build completed successfully" -ForegroundColor Green
} else {
    Write-Host "`n[Step 1/6] Skipping build (SkipBuild flag set)" -ForegroundColor Yellow
}

# Verify build output exists
$PublishDir = "$RepoRoot/.work/build/Azure.Mcp.Server/$OperatingSystem-$Architecture-untrimmed"
if (-not (Test-Path $PublishDir)) {
    Write-Error "Build output not found at: $PublishDir"
    exit 1
}

# Step 2: Build Docker image
if (-not $SkipDockerBuild) {
    Write-Host "`n[Step 2/6] Building Docker image..." -ForegroundColor Green
    
    Push-Location $RepoRoot
    try {
        # Map architecture to Docker platform
        $DockerPlatform = switch ($Architecture) {
            'x64' { 'linux/amd64' }
            'arm64' { 'linux/arm64' }
            default { 'linux/amd64' }
        }
        
        $DockerBuildArgs = @(
            "build"
            "--platform", $DockerPlatform
            "--build-arg", "PUBLISH_DIR=.work/build/Azure.Mcp.Server/$OperatingSystem-$Architecture-untrimmed"
            "--file", "Dockerfile.remote"
            "--tag", $FullImageName
            "--progress", "plain"
            "."
        )
        
        & docker $DockerBuildArgs
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Docker build failed with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
        }
        Write-Host "✓ Docker image built successfully" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "`n[Step 2/6] Skipping Docker build (SkipDockerBuild flag set)" -ForegroundColor Yellow
}

# Step 3: Ensure Azure CLI is logged in and create resource group
Write-Host "`n[Step 3/6] Setting up Azure resources..." -ForegroundColor Green

# Check if logged in to Azure CLI
$AzAccount = az account show 2>$null | ConvertFrom-Json
if (-not $AzAccount) {
    Write-Host "Not logged in to Azure CLI. Please log in..." -ForegroundColor Yellow
    az login
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Azure login failed"
        exit 1
    }
}

Write-Host "Using Azure subscription: $($AzAccount.name) ($($AzAccount.id))" -ForegroundColor Cyan

# Create resource group if it doesn't exist
$RgExists = az group exists --name $ResourceGroup
if ($RgExists -eq 'false') {
    Write-Host "Creating resource group: $ResourceGroup" -ForegroundColor Yellow
    az group create --name $ResourceGroup --location $Location
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create resource group"
        exit 1
    }
} else {
    Write-Host "Resource group $ResourceGroup already exists" -ForegroundColor Cyan
}

# Step 4: Deploy infrastructure (ACR, Log Analytics, Managed Identity, ACA Environment)
Write-Host "`n[Step 4/6] Deploying Azure infrastructure (without Container App)..." -ForegroundColor Green

# Deploy infrastructure using Bicep template
$InfraBicepFile = "$RepoRoot/deployment/remote-mcp-infrastructure.bicep"
$InfraDeploymentName = "azmcp-infra-$(Get-Date -Format 'yyyyMMddHHmmss')"

Write-Host "Deploying infrastructure (ACR, Log Analytics, Managed Identity, ACA Environment)..." -ForegroundColor Yellow

$InfraOutput = az deployment group create `
    --name $InfraDeploymentName `
    --resource-group $ResourceGroup `
    --template-file $InfraBicepFile `
    --parameters `
        baseName=$BaseName `
        environment=$Environment `
        location=$Location `
    --query 'properties.outputs' `
    --output json | ConvertFrom-Json

if ($LASTEXITCODE -ne 0) {
    Write-Error "Infrastructure deployment failed"
    exit 1
}

$AcrLoginServer = $InfraOutput.acrLoginServer.value
$ManagedIdentityPrincipalId = $InfraOutput.managedIdentityPrincipalId.value
$AcaEnvironmentId = $InfraOutput.acaEnvironmentId.value

Write-Host "✓ Infrastructure deployed successfully" -ForegroundColor Green
Write-Host "  ACR Login Server: $AcrLoginServer" -ForegroundColor Cyan
Write-Host "  Managed Identity Principal ID: $ManagedIdentityPrincipalId" -ForegroundColor Cyan

# Wait for RBAC role assignments to propagate
Write-Host "`nWaiting for Azure RBAC role assignments to propagate (30 seconds)..." -ForegroundColor Yellow
Start-Sleep -Seconds 30

# Step 5: Push Docker image to ACR
Write-Host "`n[Step 5/6] Pushing Docker image to ACR..." -ForegroundColor Green

# Login to ACR
az acr login --name $AcrName
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to login to ACR"
    exit 1
}

# Push image
docker push $FullImageName
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to push Docker image to ACR"
    exit 1
}

Write-Host "✓ Docker image pushed successfully" -ForegroundColor Green

# Step 6: Deploy Container App
Write-Host "`n[Step 6/6] Deploying Container App..." -ForegroundColor Green

$ContainerAppName = "$BaseName-app-$Environment"

# Create or update the container app
$ExistingApp = az containerapp show --name $ContainerAppName --resource-group $ResourceGroup 2>$null
if ($ExistingApp) {
    Write-Host "Updating existing Container App..." -ForegroundColor Yellow
    
    az containerapp update `
        --name $ContainerAppName `
        --resource-group $ResourceGroup `
        --image $FullImageName `
        --set-env-vars `
            "ASPNETCORE_ENVIRONMENT=$($Environment -eq 'prod' ? 'Production' : 'Development')" `
            "ASPNETCORE_URLS=http://+:8080" `
            "AzureAd__TenantId=$AzureAdTenantId" `
            "AzureAd__ClientId=$AzureAdClientId" `
            "AzureAd__Instance=$AzureAdInstance"
} else {
    Write-Host "Creating new Container App..." -ForegroundColor Yellow
    
    # Get the managed identity ID
    $ManagedIdentityId = "/subscriptions/$((az account show --query id -o tsv))/resourceGroups/$ResourceGroup/providers/Microsoft.ManagedIdentity/userAssignedIdentities/$BaseName-identity-$Environment"
    
    az containerapp create `
        --name $ContainerAppName `
        --resource-group $ResourceGroup `
        --environment "$BaseName-env-$Environment" `
        --image $FullImageName `
        --target-port 8080 `
        --ingress external `
        --registry-server $AcrLoginServer `
        --registry-identity $ManagedIdentityId `
        --user-assigned $ManagedIdentityId `
        --cpu 0.5 `
        --memory 1.0Gi `
        --min-replicas 1 `
        --max-replicas 3 `
        --env-vars `
            "ASPNETCORE_ENVIRONMENT=$($Environment -eq 'prod' ? 'Production' : 'Development')" `
            "ASPNETCORE_URLS=http://+:8080" `
            "AzureAd__TenantId=$AzureAdTenantId" `
            "AzureAd__ClientId=$AzureAdClientId" `
            "AzureAd__Instance=$AzureAdInstance"
}

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create/update Container App"
    exit 1
}

Write-Host "✓ Container App deployed successfully" -ForegroundColor Green

# Get the Container App URL
$ContainerAppUrl = az containerapp show `
    --name $ContainerAppName `
    --resource-group $ResourceGroup `
    --query 'properties.configuration.ingress.fqdn' `
    --output tsv

if (-not $ManagedIdentityPrincipalId) {
    $ManagedIdentityPrincipalId = az identity show `
        --name "$BaseName-identity-$Environment" `
        --resource-group $ResourceGroup `
        --query 'principalId' `
        --output tsv
}

# Final summary
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Deployment Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Container App URL: https://$ContainerAppUrl" -ForegroundColor Yellow
Write-Host "ACR: $AcrLoginServer" -ForegroundColor Yellow
Write-Host "Image: $FullImageName" -ForegroundColor Yellow
Write-Host "`nManaged Identity Principal ID: $ManagedIdentityPrincipalId" -ForegroundColor Cyan
Write-Host "Assign appropriate Azure RBAC roles to this principal ID for the resources the MCP server needs to access." -ForegroundColor Cyan
Write-Host "`nHealth Check: https://$ContainerAppUrl/health" -ForegroundColor Yellow
Write-Host "========================================`n" -ForegroundColor Cyan

# Output structured data for automation
$OutputData = @{
    containerAppUrl = "https://$ContainerAppUrl"
    acrLoginServer = $AcrLoginServer
    imageName = $FullImageName
    managedIdentityPrincipalId = $ManagedIdentityPrincipalId
    resourceGroup = $ResourceGroup
    location = $Location
}

$OutputData | ConvertTo-Json | Out-File "$RepoRoot/deployment/.last-deployment.json" -Force
Write-Host "Deployment details saved to: deployment/.last-deployment.json" -ForegroundColor Gray
