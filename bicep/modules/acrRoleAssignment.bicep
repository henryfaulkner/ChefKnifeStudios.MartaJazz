// =============================================================================
// acrRoleAssignment.bicep — Grant AcrPull on existing ACR to a principal
// Scoped to the resource group containing the registry.
// =============================================================================

@description('Name of the existing Azure Container Registry.')
param containerRegistryName string

@description('Principal ID (object ID) of the identity to grant AcrPull.')
param principalId string

// AcrPull built-in role
var acrPullRoleDefinitionId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource acr 'Microsoft.ContainerRegistry/registries@2025-04-01' existing = {
  name: containerRegistryName
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, principalId, acrPullRoleDefinitionId)
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleDefinitionId)
  }
}
