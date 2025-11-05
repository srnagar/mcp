#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Local development script for testing Azure MCP Server Docker image
.DESCRIPTION
    Builds and runs the Docker image locally for testing before deployment
.PARAMETER SkipBuild
    Skip building the Docker image (use existing image)
.PARAMETER Port
    Local port to bind (default: 1031)
#>

param(
    [Parameter(Mandatory = $false)]
    [switch]$SkipBuild,
    
    [Parameter(Mandatory = $false)]
    [int]$Port = 1031
)

$ErrorActionPreference = "Stop"

# Script directory and project paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$serverProjectPath = Join-Path $projectRoot "servers\Azure.Mcp.Server"

Write-Host "🧪 Local Azure MCP Server testing..." -ForegroundColor Green

if (-not $SkipBuild) {
    # Build and publish the application
    Write-Host "🔄 Building and publishing the application..." -ForegroundColor Yellow
    $publishPath = Join-Path $serverProjectPath "bin\Release\net9.0\publish"
    
    # Clean previous builds
    if (Test-Path $publishPath) {
        Remove-Item $publishPath -Recurse -Force
    }
    
    # Publish the application
    Push-Location $serverProjectPath
    try {
        dotnet publish --configuration Release --output "bin\Release\net9.0\publish" --self-contained false
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
        docker build --build-arg PUBLISH_DIR="servers/Azure.Mcp.Server/bin/Release/net9.0/publish" -t azmcp-server:local .
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to build Docker image"
        }
    } finally {
        Pop-Location
    }
    
    Write-Host "✅ Docker image built successfully!" -ForegroundColor Green
} else {
    Write-Host "⏭️ Skipping Docker build (using existing image: azmcp-server:local)" -ForegroundColor Yellow
}

# Stop any existing container
Write-Host "🔄 Stopping any existing containers..." -ForegroundColor Yellow
docker stop azmcp-server-local 2>$null
docker rm azmcp-server-local 2>$null

# Run the container
Write-Host "🔄 Starting container on port $Port..." -ForegroundColor Yellow
docker run -d `
    --name azmcp-server-local `
    -p ${Port}:1031 `
    -e ASPNETCORE_ENVIRONMENT=Development `
    -e ASPNETCORE_URLS=http://+:1031 `
    -e "AzureAd__TenantId=70a036f6-8e4d-4615-bad6-149c02e7720d" `
    -e "AzureAd__ClientId=ca1e0302-d50a-47d7-b5e6-7aff49884bce" `
    -e "AzureAd__Instance=https://login.microsoftonline.com/" `
    -v "${env:USERPROFILE}/.azure:/root/.azure:ro" `
    azmcp-server:local

if ($LASTEXITCODE -ne 0) {
    throw "Failed to start container"
}

Write-Host "✅ Container started successfully!" -ForegroundColor Green
Write-Host "🌐 Application URL: http://localhost:$Port" -ForegroundColor Cyan

# Wait for the application to be ready
Write-Host "🔄 Waiting for application to be ready..." -ForegroundColor Yellow
$maxAttempts = 30
$attempt = 0

do {
    Start-Sleep -Seconds 2
    $attempt++
    
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:$Port/health" -Method Get -TimeoutSec 5
        Write-Host "✅ Application is healthy!" -ForegroundColor Green
        break
    } catch {
        if ($attempt -eq $maxAttempts) {
            Write-Host "⚠️ Application didn't become healthy within expected time" -ForegroundColor Yellow
            break
        }
        Write-Host "   Attempt $attempt/$maxAttempts - waiting..." -ForegroundColor Gray
    }
} while ($attempt -lt $maxAttempts)

# Show container logs
Write-Host ""
Write-Host "📋 Recent container logs:" -ForegroundColor Yellow
docker logs azmcp-server-local --tail 20

Write-Host ""
Write-Host "🎉 Local testing environment is ready!" -ForegroundColor Green
Write-Host ""
Write-Host "📋 Available commands:" -ForegroundColor Yellow
Write-Host "   View logs:     docker logs -f azmcp-server-local" -ForegroundColor Gray
Write-Host "   Stop container: docker stop azmcp-server-local" -ForegroundColor Gray
Write-Host "   Remove container: docker rm azmcp-server-local" -ForegroundColor Gray
Write-Host ""
Write-Host "🌐 Test the application at: http://localhost:$Port" -ForegroundColor Cyan

# Test MCP endpoint
Write-Host ""
Write-Host "🔄 Testing MCP endpoint..." -ForegroundColor Yellow
try {
    $mcpTestPayload = @{
        jsonrpc = "2.0"
        method = "tools/list"
        id = 1
    } | ConvertTo-Json

    $mcpResponse = Invoke-RestMethod -Uri "http://localhost:$Port" -Method Post -ContentType "application/json" -Body $mcpTestPayload -TimeoutSec 10
    Write-Host "✅ MCP endpoint is responding!" -ForegroundColor Green
    Write-Host "   Found $($mcpResponse.result.tools.Count) tools available" -ForegroundColor Cyan
} catch {
    Write-Host "⚠️ MCP endpoint test failed: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "   This might be expected without proper authentication" -ForegroundColor Gray
}