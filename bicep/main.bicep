// =============================================================================
// main.bicep — Marta Jazz Infrastructure
// Subscription: ChefKnifeStudios
// =============================================================================

targetScope = 'subscription'

// -----------------------------------------------------------------------------
// Parameters
// -----------------------------------------------------------------------------

@description('Project name used as prefix in all resource names.')
param projectName string = 'marta-jazz'

@description('Deployment environment.')
@allowed([
  'dev'
  'prod'
])
param environment string

@description('Primary Azure region for regional resources.')
param location string = 'eastus2'

@description('Custom apex domain for the site (e.g., martajazz.com).')
param apexDomain string = 'martajazz.com'

@description('Existing Container Registry name (shared across environments).')
param containerRegistryName string = 'chefknife'

@description('Resource group of the existing Container Registry.')
param containerRegistryResourceGroup string = 'general'

@description('Container image tag to deploy for the server container app.')
param serverImageTag string = 'latest'

@description('GitHub repository URL for the Static Web App source.')
param repositoryUrl string = 'https://github.com/henryfaulkner/ChefKnifeStudios.MartaJazz'

@description('GitHub Personal Access Token for SWA deployment.')
@secure()
param repositoryToken string

// -----------------------------------------------------------------------------
// Variables
// -----------------------------------------------------------------------------

var namePrefix = '${projectName}-${environment}'
var resourceGroupName = '${namePrefix}-rg'

var tags = {
  env: environment
  project: projectName
}

// -----------------------------------------------------------------------------
// Resource Group
// -----------------------------------------------------------------------------

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// -----------------------------------------------------------------------------
// DNS Zone (deployed first; SWA validates custom domains against it)
// -----------------------------------------------------------------------------

module dnsZone 'modules/dnsZone.bicep' = {
  name: 'dnsZone-deploy'
  scope: rg
  params: {
    zoneName: apexDomain
    tags: tags
    staticWebAppName: swa.outputs.name
    staticWebAppResourceId: swa.outputs.id
    staticWebAppDefaultHostname: swa.outputs.defaultHostname
  }
}

// -----------------------------------------------------------------------------
// Static Web App (client-side host)
// -----------------------------------------------------------------------------

module swa 'modules/staticWebApp.bicep' = {
  name: 'swa-deploy'
  scope: rg
  params: {
    name: '${namePrefix}-swa'
    location: 'eastus2'
    tags: tags
    repositoryUrl: repositoryUrl
    repositoryToken: repositoryToken
    branch: 'main'
    customDomains: [
      apexDomain
      'www.${apexDomain}'
    ]
  }
}

// -----------------------------------------------------------------------------
// User-assigned Managed Identity (server)
// -----------------------------------------------------------------------------

module serverIdentity 'modules/managedIdentity.bicep' = {
  name: 'server-mi-deploy'
  scope: rg
  params: {
    name: '${namePrefix}-ca-server-mi'
    location: location
    tags: tags
  }
}

// -----------------------------------------------------------------------------
// AcrPull role assignment on the existing ACR for the server MI
// -----------------------------------------------------------------------------

module acrRoleAssignment 'modules/acrRoleAssignment.bicep' = {
  name: 'acr-role-deploy'
  scope: resourceGroup(containerRegistryResourceGroup)
  params: {
    containerRegistryName: containerRegistryName
    principalId: serverIdentity.outputs.principalId
  }
}

// -----------------------------------------------------------------------------
// Container Apps Environment
// -----------------------------------------------------------------------------

module cae 'modules/containerAppsEnvironment.bicep' = {
  name: 'cae-deploy'
  scope: rg
  params: {
    name: '${namePrefix}-cae'
    location: location
    tags: tags
  }
}

// -----------------------------------------------------------------------------
// Container App — server (API + SignalR Hub + Worker)
// -----------------------------------------------------------------------------

module serverApp 'modules/containerApp.bicep' = {
  name: 'server-ca-deploy'
  scope: rg
  params: {
    name: '${namePrefix}-ca-server'
    location: location
    tags: tags
    environmentId: cae.outputs.id
    managedIdentityId: serverIdentity.outputs.id
    containerRegistryLoginServer: '${containerRegistryName}.azurecr.io'
    image: '${containerRegistryName}.azurecr.io/chefknifestudios.martajazz.server.webapi:${serverImageTag}'
    cpu: json('0.5')
    memory: '1Gi'
    minReplicas: 1
    maxReplicas: 1
    targetPort: 8080 // confirmed: Dockerfile EXPOSE 8080
    corsAllowedOrigins: [
      'https://${apexDomain}'
      'https://www.${apexDomain}'
    ]
    envVars: [
      {
        name: 'WebApi__BaseUrl'
        value: 'http://localhost:8080'
      }
    ]
  }
  dependsOn: [
    acrRoleAssignment
  ]
}

// -----------------------------------------------------------------------------
// Outputs
// -----------------------------------------------------------------------------

output resourceGroupName string = rg.name
output staticWebAppDefaultHostname string = swa.outputs.defaultHostname
output dnsZoneNameServers array = dnsZone.outputs.nameServers
output serverContainerAppFqdn string = serverApp.outputs.fqdn
output serverManagedIdentityPrincipalId string = serverIdentity.outputs.principalId
