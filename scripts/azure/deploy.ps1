[CmdletBinding()]
param(
    [string]$Location = 'westeurope',
    [string]$EnvironmentName = 'dev',
    [string]$ResourceGroupName = 'rg-chatbot-dev',
    [ValidateSet('dev-demo', 'secure-demo')]
    [string]$CostProfile = 'dev-demo',
    [string]$GeminiApiKey,
    [string]$NameSuffix,
    [switch]$ForceInfrastructureDeployment
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..\..')
$templatePath = Join-Path $repoRoot 'infra\main.bicep'
$deployBackendScript = Join-Path $scriptDir 'deploy-backend.ps1'
$deployFrontendScript = Join-Path $scriptDir 'deploy-frontend.ps1'
$envFilePath = Join-Path $repoRoot '.env'

function Assert-CommandExists {
    param([Parameter(Mandatory = $true)][string]$CommandName)

    if (-not (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$CommandName' was not found in PATH."
    }
}

function Get-DotEnvValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    if (-not (Test-Path $FilePath)) {
        return $null
    }

    foreach ($line in Get-Content -Path $FilePath) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#')) {
            continue
        }

        $separatorIndex = $trimmed.IndexOf('=')
        if ($separatorIndex -le 0) {
            continue
        }

        $currentKey = $trimmed.Substring(0, $separatorIndex).Trim()
        if (-not $currentKey.Equals($Key, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $value = $trimmed.Substring($separatorIndex + 1).Trim()
        return $value.Trim('"')
    }

    return $null
}

function Test-AzureResourceExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupName,

        [Parameter(Mandatory = $true)]
        [string]$ResourceType,

        [Parameter(Mandatory = $true)]
        [string]$ResourceName
    )

    $escapedResourceName = $ResourceName.Replace("'", "''")
    $matchCount = az resource list --resource-group $ResourceGroupName --resource-type $ResourceType --query "[?name=='$escapedResourceName'] | length(@)" -o tsv

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to query Azure resources for type '$ResourceType' in resource group '$ResourceGroupName'."
    }

    return $matchCount -eq '1'
}

function Get-InfrastructureStateHash {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TemplateFilePath,

        [Parameter(Mandatory = $true)]
        [string[]]$StateParts
    )

    $templateHash = (Get-FileHash -Path $TemplateFilePath -Algorithm SHA256).Hash
    $combined = @($templateHash) + $StateParts
    return (($combined -join '|').ToLowerInvariant())
}

function Get-DefaultNameSuffix {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SubscriptionId
    )

    $normalizedSubscriptionId = ($SubscriptionId.ToLowerInvariant() -replace '[^a-z0-9]', '')
    if ($normalizedSubscriptionId.Length -lt 6) {
        throw 'Subscription id is too short to derive a default resource name suffix.'
    }

    return $normalizedSubscriptionId.Substring(0, 6)
}

function Resolve-PlainTextGeminiApiKey {
    param(
        [string]$InputApiKey,
        [string]$DotEnvPath
    )

    if (-not [string]::IsNullOrWhiteSpace($InputApiKey)) {
        return $InputApiKey
    }

    $dotEnvApiKey = Get-DotEnvValue -FilePath $DotEnvPath -Key 'GEMINI_API_KEY'
    if (-not [string]::IsNullOrWhiteSpace($dotEnvApiKey)) {
        Write-Host "Using GEMINI_API_KEY from $DotEnvPath"
        return $dotEnvApiKey
    }

    Write-Host ''
    Write-Host 'Enter your Gemini API key for App Service configuration.'
    $secureKey = Read-Host -AsSecureString -Prompt 'GEMINI_API_KEY'
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Show-DeploymentPlan {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Account,

        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupName,

        [Parameter(Mandatory = $true)]
        [string]$Location,

        [Parameter(Mandatory = $true)]
        [string]$CostProfile,

        [Parameter(Mandatory = $true)]
        [string]$AppServicePlanName,

        [Parameter(Mandatory = $true)]
        [string]$BackendAppName,

        [Parameter(Mandatory = $true)]
        [string]$StaticWebAppName,

        [Parameter(Mandatory = $true)]
        [string]$BackendHost,

        [Parameter(Mandatory = $true)]
        [string]$FrontendHost,

        [Parameter(Mandatory = $true)]
        [string]$CorsAllowedOrigins,

        [Parameter(Mandatory = $true)]
        [string]$InfrastructureStateHash
    )

    $planLines = @(
        'Deployment summary:',
        "- Azure subscription: $($Account.name) [$($Account.id)]",
        "- Azure tenant id: $($Account.tenantId)",
        "- Resource group: $ResourceGroupName",
        "- Location: $Location",
        "- Cost profile: $CostProfile",
        "- App Service plan: $AppServicePlanName (Free F1)",
        "- Backend App Service: $BackendAppName",
        "- Static Web App: $StaticWebAppName (Free)",
        "- Backend base URL injected into frontend: https://$BackendHost/api",
        "- Frontend host allowed in backend CORS: https://$FrontendHost",
        "- Local CORS origin allowed: http://localhost:4200",
        "- Backend app settings to be applied: LLMProvider=gemini, EnabledProviders=gemini, GEMINI_API_KEY, ASPNETCORE_ENVIRONMENT=Production, CORS_ALLOWED_ORIGINS=$CorsAllowedOrigins",
        "- Infrastructure state hash: $InfrastructureStateHash",
        '- Will create the resource group if it does not exist.',
        '- Will deploy infrastructure only if needed or when forced.',
        '- Will deploy backend code and frontend code after configuration.',
        ''
    )

    foreach ($line in $planLines) {
        Write-Host $line
    }
}

function Confirm-Deployment {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Account,

        [Parameter(Mandatory = $true)]
        [string]$ResourceGroupName,

        [Parameter(Mandatory = $true)]
        [string]$Location,

        [Parameter(Mandatory = $true)]
        [string]$CostProfile,

        [Parameter(Mandatory = $true)]
        [string]$AppServicePlanName,

        [Parameter(Mandatory = $true)]
        [string]$BackendAppName,

        [Parameter(Mandatory = $true)]
        [string]$StaticWebAppName,

        [Parameter(Mandatory = $true)]
        [string]$BackendHost,

        [Parameter(Mandatory = $true)]
        [string]$FrontendHost,

        [Parameter(Mandatory = $true)]
        [string]$CorsAllowedOrigins,

        [Parameter(Mandatory = $true)]
        [string]$InfrastructureStateHash
    )

    Show-DeploymentPlan `
        -Account $Account `
        -ResourceGroupName $ResourceGroupName `
        -Location $Location `
        -CostProfile $CostProfile `
        -AppServicePlanName $AppServicePlanName `
        -BackendAppName $BackendAppName `
        -StaticWebAppName $StaticWebAppName `
        -BackendHost $BackendHost `
        -FrontendHost $FrontendHost `
        -CorsAllowedOrigins $CorsAllowedOrigins `
        -InfrastructureStateHash $InfrastructureStateHash

    $confirmation = Read-Host 'Type CONTINUE to start deployment or anything else to cancel'
    if ($confirmation -ne 'CONTINUE') {
        throw 'Deployment cancelled by user before any Azure changes were made.'
    }
}

Assert-CommandExists -CommandName 'az'
Assert-CommandExists -CommandName 'dotnet'
Assert-CommandExists -CommandName 'npm'

if ($CostProfile -eq 'secure-demo') {
    Write-Warning 'secure-demo currently reuses the same infra as dev-demo. Key Vault integration can be added in the next increment.'
}

$accountJson = az account show -o json | ConvertFrom-Json
if (-not $accountJson) {
    throw 'Azure CLI is not logged in. Run az login and retry.'
}

$resolvedSubscriptionId = $accountJson.id
if ([string]::IsNullOrWhiteSpace($resolvedSubscriptionId)) {
    throw 'No active subscription id could be resolved. Run az account set to choose a subscription and retry.'
}

Write-Host "Using subscription: $resolvedSubscriptionId"
az account set --subscription $resolvedSubscriptionId | Out-Host

Write-Host "Azure account name: $($accountJson.name)"
Write-Host "Azure tenant id: $($accountJson.tenantId)"

$effectiveSuffix = if (-not [string]::IsNullOrWhiteSpace($NameSuffix)) {
    $NameSuffix.ToLowerInvariant()
} else {
    Get-DefaultNameSuffix -SubscriptionId $resolvedSubscriptionId
}

$normalizedEnv = ($EnvironmentName.ToLowerInvariant() -replace '[^a-z0-9-]', '')
if ([string]::IsNullOrWhiteSpace($normalizedEnv)) {
    throw 'EnvironmentName must contain at least one alphanumeric character.'
}

$nameSuffixFragment = if ([string]::IsNullOrWhiteSpace($effectiveSuffix)) { '' } else { "-$effectiveSuffix" }
$resourceGroupName = $ResourceGroupName
$appServicePlanName = "asp-chatbot-$normalizedEnv$nameSuffixFragment"
$backendAppName = "app-chatbot-api-$normalizedEnv$nameSuffixFragment"
$staticWebAppName = "swa-chatbot-ui-$normalizedEnv$nameSuffixFragment"
$infrastructureStateHash = Get-InfrastructureStateHash -TemplateFilePath $templatePath -StateParts @($Location, $CostProfile, $appServicePlanName, $backendAppName, $staticWebAppName)

Write-Host ''
Write-Host 'Planned resources:'
Write-Host "- Resource Group: $resourceGroupName"
Write-Host "- App Service Plan (Free F1): $appServicePlanName"
Write-Host "- Backend App: $backendAppName"
Write-Host "- Static Web App (Free): $staticWebAppName"
Write-Host "- Cost Profile: $CostProfile"
Write-Host "- Force Infrastructure Deployment: $ForceInfrastructureDeployment"

Write-Host ''
$backendHost = "$backendAppName.azurewebsites.net"
$frontendHost = "$staticWebAppName.azurestaticapps.net"
$corsAllowedOrigins = "http://localhost:4200,https://$frontendHost"

Confirm-Deployment `
    -Account $accountJson `
    -ResourceGroupName $resourceGroupName `
    -Location $Location `
    -CostProfile $CostProfile `
    -AppServicePlanName $appServicePlanName `
    -BackendAppName $backendAppName `
    -StaticWebAppName $staticWebAppName `
    -BackendHost $backendHost `
    -FrontendHost $frontendHost `
    -CorsAllowedOrigins $corsAllowedOrigins `
    -InfrastructureStateHash $infrastructureStateHash

$resourceGroupExists = (az group exists --name $resourceGroupName) -eq 'true'
if (-not $resourceGroupExists) {
    Write-Host 'Creating resource group...'
    az group create --name $resourceGroupName --location $Location | Out-Host
} else {
    Write-Host "Reusing existing resource group: $resourceGroupName"
}

$existingInfrastructureHash = if ($resourceGroupExists) {
    az group show --name $resourceGroupName --query tags.deploymentInfraHash -o tsv 2>$null
} else {
    ''
}

$appServicePlanExists = Test-AzureResourceExists -ResourceGroupName $resourceGroupName -ResourceType 'Microsoft.Web/serverfarms' -ResourceName $appServicePlanName
$backendAppExists = Test-AzureResourceExists -ResourceGroupName $resourceGroupName -ResourceType 'Microsoft.Web/sites' -ResourceName $backendAppName
$staticWebAppExists = Test-AzureResourceExists -ResourceGroupName $resourceGroupName -ResourceType 'Microsoft.Web/staticSites' -ResourceName $staticWebAppName
$allInfrastructureResourcesExist = $appServicePlanExists -and $backendAppExists -and $staticWebAppExists
$shouldDeployInfrastructure = $ForceInfrastructureDeployment.IsPresent -or -not $allInfrastructureResourcesExist -or ($existingInfrastructureHash -ne $infrastructureStateHash)

if ($shouldDeployInfrastructure) {
    Write-Host 'Deploying infrastructure...'
    $deploymentRaw = az deployment group create `
        --resource-group $resourceGroupName `
        --template-file $templatePath `
        --parameters location=$Location appServicePlanName=$appServicePlanName backendAppName=$backendAppName staticWebAppName=$staticWebAppName `
        --only-show-errors `
        -o json 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Infrastructure deployment failed. Azure CLI output:`n$($deploymentRaw -join [Environment]::NewLine)"
    }

    $deployment = $deploymentRaw | ConvertFrom-Json

    if (-not $deployment) {
        throw 'Infrastructure deployment did not return a valid response.'
    }

    az group update --name $resourceGroupName --set tags.deploymentInfraHash=$infrastructureStateHash tags.environment=$normalizedEnv tags.costProfile=$CostProfile | Out-Host
} else {
    Write-Host 'Infrastructure unchanged and required resources already exist. Skipping infrastructure deployment and deploying code only.'
}

$geminiKey = Resolve-PlainTextGeminiApiKey -InputApiKey $GeminiApiKey -DotEnvPath $envFilePath
if ([string]::IsNullOrWhiteSpace($geminiKey)) {
    throw 'GEMINI_API_KEY cannot be empty.'
}

$backendHost = az webapp show --resource-group $resourceGroupName --name $backendAppName --query defaultHostName -o tsv
$frontendHost = az staticwebapp show --resource-group $resourceGroupName --name $staticWebAppName --query defaultHostname -o tsv

if ([string]::IsNullOrWhiteSpace($backendHost) -or [string]::IsNullOrWhiteSpace($frontendHost)) {
    throw 'Unable to resolve backend/frontend host names from Azure resources.'
}

$corsAllowedOrigins = "http://localhost:4200,https://$frontendHost"

Write-Host 'Applying backend app settings for Gemini-first Azure mode...'
az webapp config appsettings set `
    --resource-group $resourceGroupName `
    --name $backendAppName `
    --settings "LLMProvider=gemini" "LLM_PROVIDER=gemini" "EnabledProviders=gemini" "ENABLED_PROVIDERS=gemini" "GEMINI_API_KEY=$geminiKey" "ASPNETCORE_ENVIRONMENT=Production" "CORS_ALLOWED_ORIGINS=$corsAllowedOrigins" | Out-Host

Write-Host 'Deploying backend...'
& $deployBackendScript -ResourceGroupName $resourceGroupName -BackendAppName $backendAppName

Write-Host 'Deploying frontend...'
& $deployFrontendScript -ResourceGroupName $resourceGroupName -StaticWebAppName $staticWebAppName -BackendBaseUrl "https://$backendHost"

Write-Host ''
Write-Host 'Deployment completed successfully.'
Write-Host "Backend URL: https://$backendHost"
Write-Host "Backend Swagger: https://$backendHost/swagger"
Write-Host "Frontend URL: https://$frontendHost"
Write-Host ''
Write-Host 'Note: This is a dev-demo deployment profile with free-tier reliability limits.'
