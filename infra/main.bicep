param location string = resourceGroup().location
param appServicePlanName string
param backendAppName string
param staticWebAppName string
param appServicePlanSku string = 'F1'
param appServicePlanTier string = 'Free'
param appServicePlanCapacity int = 1
param tags object = {}

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  tags: tags
  sku: {
    name: appServicePlanSku
    tier: appServicePlanTier
    capacity: appServicePlanCapacity
  }
  properties: {
    reserved: true
  }
}

resource backendApp 'Microsoft.Web/sites@2023-12-01' = {
  name: backendAppName
  location: location
  kind: 'app,linux'
  tags: tags
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: false
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
      ]
    }
  }
}

resource staticSite 'Microsoft.Web/staticSites@2023-01-01' = {
  name: staticWebAppName
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

output backendAppName string = backendApp.name
output backendDefaultHostName string = backendApp.properties.defaultHostName
output staticWebAppName string = staticSite.name
output staticDefaultHostName string = staticSite.properties.defaultHostname
