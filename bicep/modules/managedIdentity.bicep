// =============================================================================
// managedIdentity.bicep — User-assigned Managed Identity
// =============================================================================

@description('Name of the managed identity.')
param name string

@description('Location.')
param location string

@description('Resource tags.')
param tags object

resource mi 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: name
  location: location
  tags: tags
}

output id string = mi.id
output name string = mi.name
output principalId string = mi.properties.principalId
output clientId string = mi.properties.clientId
