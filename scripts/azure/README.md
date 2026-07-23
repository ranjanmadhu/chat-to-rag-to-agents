# Azure Deployment Scripts

This folder contains script-first deployment for the repository.

## Default Profile

The default profile is ultra-low-cost dev-demo:

1. Static Web Apps Free (frontend)
2. App Service Free F1 (backend)
3. Gemini default provider in Azure app settings
4. Ollama disabled through EnabledProviders=gemini
5. Resource group defaults to `rg-chatbot-dev`
6. Resource names use a stable subscription-derived suffix by default
7. Infrastructure is skipped when the current Azure resources already match the expected infra state

## Prerequisites

1. Azure CLI (`az`) and active login (`az login`) with the target subscription selected
2. .NET SDK 10
3. Node.js and npm
4. PowerShell

## One-Command Deploy

From repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\azure\deploy.ps1 -EnvironmentName dev -Location westeurope
```

## Teardown for Fresh Deploy

Delete the deployment resource group and all resources:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\azure\teardown.ps1 -ResourceGroupName rg-chatbot-dev -Force
```

If you want a confirmation prompt, omit `-Force`.

## Parameters

1. `-Location` (default: `westeurope`)
2. `-EnvironmentName` (default: `dev`)
3. `-ResourceGroupName` (default: `rg-chatbot-dev`)
4. `-CostProfile` (`dev-demo` default; `secure-demo` reserved)
5. `-GeminiApiKey` (optional; overrides `.env` when provided)
6. `-NameSuffix` (optional custom suffix for resource names; overrides subscription-derived default)
7. `-ForceInfrastructureDeployment` (optional; re-runs infra deployment even when unchanged)

Frontend deploy script parameters:

1. `-ResourceGroupName` (required)
2. `-StaticWebAppName` (required)
3. `-BackendBaseUrl` (required)
4. `-SkipPostDeployValidation` (optional; skips deployed `index.html` runtime config verification)

Teardown script parameters:

1. `-ResourceGroupName` (default: `rg-chatbot-dev`)
2. `-NoWait` (optional; submits deletion and returns immediately)
3. `-Force` (optional; skips confirmation prompt)

## Gemini API Key Resolution

The deployment script resolves `GEMINI_API_KEY` in this order:

1. `-GeminiApiKey` parameter
2. `GEMINI_API_KEY` in the repository `.env` file
3. Interactive secure prompt

## What The Script Does

1. Uses the currently selected Azure account subscription and logs the subscription name, id, and tenant id.
2. Prints a deployment summary showing the resource group, resource names, backend/frontend URLs, and the settings that will be applied.
3. Prompts for confirmation before any Azure changes are made. Type `CONTINUE` to proceed.
4. Creates `rg-chatbot-dev` if it does not already exist, otherwise reuses it.
5. Derives stable resource names from the active subscription id unless `-NameSuffix` is provided.
6. Checks whether the expected Azure resources already exist and whether the infra template state hash has changed.
7. Deploys infra from `infra/main.bicep` only when needed, or when `-ForceInfrastructureDeployment` is passed.
8. Applies backend app settings for Gemini-first mode and CORS allowed origins.
9. Publishes and deploys backend (`dotnet publish` + zip deploy).
10. Builds frontend and injects backend API base URL into built `index.html`.
11. Deploys frontend to Static Web Apps.
12. Validates deployed frontend `index.html` contains the expected runtime API assignment.
13. Prints backend/frontend URLs.

## Notes

1. This profile is intended for demos and learning, not production reliability.
2. Free tiers can throttle and may have cold starts.
3. For production hardening, add Key Vault and non-free SKUs.
