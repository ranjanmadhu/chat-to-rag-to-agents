[CmdletBinding()]
param(
    [string]$ResourceGroupName = 'rg-chatbot-dev',
    [switch]$NoWait,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-CommandExists {
    param([Parameter(Mandatory = $true)][string]$CommandName)

    if (-not (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$CommandName' was not found in PATH."
    }
}

Assert-CommandExists -CommandName 'az'

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

$resourceGroupExists = (az group exists --name $ResourceGroupName) -eq 'true'
if (-not $resourceGroupExists) {
    Write-Host "Resource group '$ResourceGroupName' does not exist. Nothing to delete."
    exit 0
}

if (-not $Force.IsPresent) {
    Write-Host "You are about to delete resource group '$ResourceGroupName' and all resources inside it."
    $confirmation = Read-Host "Type DELETE to continue"
    if ($confirmation -ne 'DELETE') {
        Write-Host 'Teardown cancelled.'
        exit 0
    }
}

Write-Host "Deleting resource group '$ResourceGroupName'..."
$deleteArgs = @('group', 'delete', '--name', $ResourceGroupName, '--yes')
if ($NoWait.IsPresent) {
    $deleteArgs += '--no-wait'
}

az @deleteArgs | Out-Host

if ($NoWait.IsPresent) {
    Write-Host 'Delete request submitted (no-wait mode).'
} else {
    Write-Host 'Resource group deleted successfully.'
}
