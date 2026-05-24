// =============================================================================
// containerAppsEnvironment.bicep — Managed environment for Container Apps
// =============================================================================

@description('Name of the Container Apps Environment.')
param name string

@description('Location.')
param location string

@description('Resource tags.')
param tags object

@description('Optional: Log Analytics workspace customer ID for diagnostics.')
param logAnalyticsCustomerId string = ''

@description('Optional: Log Analytics shared key.')
@secure()
param logAnalyticsSharedKey string = ''

resource env 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: empty(logAnalyticsCustomerId) ? null : {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsSharedKey
      }
    }
    zoneRedundant: false
  }
}

output id string = env.id
output name string = env.name
output defaultDomain string = env.properties.defaultDomain
