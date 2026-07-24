[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$StaticWebAppName,

    [Parameter(Mandatory = $true)]
    [string]$BackendBaseUrl,

    [switch]$SkipPostDeployValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..\..')
$frontendRoot = Join-Path $repoRoot 'frontend\chatbot-ui'

Write-Host 'Building frontend...'
Push-Location $frontendRoot
try {
    npm install | Out-Host
    npm run build -- --configuration production | Out-Host
} finally {
    Pop-Location
}

$distBrowser = Join-Path $frontendRoot 'dist\chatbot-ui\browser'
$distRoot = Join-Path $frontendRoot 'dist\chatbot-ui'
$deployPath = if (Test-Path $distBrowser) { $distBrowser } elseif (Test-Path $distRoot) { $distRoot } else { $null }

if (-not $deployPath) {
    throw 'Unable to find frontend build output. Expected dist\\chatbot-ui\\browser or dist\\chatbot-ui.'
}

$indexPath = Join-Path $deployPath 'index.html'
if (-not (Test-Path $indexPath)) {
    throw "Unable to find built index.html at '$indexPath'."
}

$normalizedBackendBaseUrl = $BackendBaseUrl.TrimEnd('/')
$runtimeApiBaseUrl = "$normalizedBackendBaseUrl/api"
$indexContents = Get-Content -Path $indexPath -Raw
$indexContents = $indexContents.Replace('__CHATBOT_API_BASE_URL_VALUE__', $runtimeApiBaseUrl)
Set-Content -Path $indexPath -Value $indexContents -Encoding UTF8
Write-Host "Injected frontend runtime API base URL: $runtimeApiBaseUrl"

Write-Host 'Fetching Static Web App deployment token...'
$deploymentToken = az staticwebapp secrets list --name $StaticWebAppName --resource-group $ResourceGroupName --query properties.apiKey -o tsv

if ([string]::IsNullOrWhiteSpace($deploymentToken)) {
    throw 'Failed to fetch deployment token for Static Web App.'
}

Write-Host 'Deploying frontend to Static Web Apps...'
Push-Location $frontendRoot
try {
    npx --yes @azure/static-web-apps-cli@2.0.7 deploy $deployPath --deployment-token $deploymentToken --env production | Out-Host
} finally {
    Pop-Location
}

if (-not $SkipPostDeployValidation.IsPresent) {
    $frontendHost = az staticwebapp show --resource-group $ResourceGroupName --name $StaticWebAppName --query defaultHostname -o tsv
    if ([string]::IsNullOrWhiteSpace($frontendHost)) {
        throw 'Post-deploy validation failed: unable to resolve Static Web App hostname.'
    }

    $validationUrl = "https://$frontendHost/?_deployValidation=$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())"
    Write-Host "Validating deployed frontend runtime config: $validationUrl"
    $deployedIndex = (Invoke-WebRequest -Uri $validationUrl -UseBasicParsing).Content

    if ($deployedIndex -match '__CHATBOT_API_BASE_URL_VALUE__') {
        throw 'Post-deploy validation failed: frontend still contains __CHATBOT_API_BASE_URL_VALUE__ placeholder.'
    }

    $expectedRuntimeLine = "window.__CHATBOT_API_BASE_URL__ = '$runtimeApiBaseUrl';"
    if ($deployedIndex -notmatch [regex]::Escape($expectedRuntimeLine)) {
        throw "Post-deploy validation failed: expected runtime API assignment not found. Expected line: $expectedRuntimeLine"
    }

    Write-Host 'Post-deploy frontend runtime config validation succeeded.'
}

Write-Host 'Frontend deployment completed.'
