[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$BackendAppName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..\..')
$publishDir = Join-Path $repoRoot '.artifacts\backend-publish'
$zipPath = Join-Path $repoRoot '.artifacts\backend.zip'

Write-Host 'Publishing backend...'

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

New-Item -ItemType Directory -Path (Join-Path $repoRoot '.artifacts') -Force | Out-Null

dotnet publish (Join-Path $repoRoot 'backend\src\Chatbot.Api\Chatbot.Api.csproj') -c Release -o $publishDir | Out-Host

Write-Host 'Creating deployment package...'
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -Force

Write-Host 'Deploying backend package to Azure App Service...'
az webapp deployment source config-zip --resource-group $ResourceGroupName --name $BackendAppName --src $zipPath | Out-Host

Write-Host 'Backend deployment completed.'
