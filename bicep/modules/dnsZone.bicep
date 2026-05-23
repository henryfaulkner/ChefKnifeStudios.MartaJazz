// =============================================================================
// dnsZone.bicep — Public DNS Zone + apex A-alias + www CNAME
// =============================================================================

@description('DNS zone name (e.g., martajazz.com). Note: this resource intentionally breaks the project naming convention because the zone name IS the domain.')
param zoneName string

@description('Resource tags.')
param tags object

@description('Static Web App name to alias the apex A record to.')
param staticWebAppName string

@description('Static Web App resource ID (used as the alias target).')
param staticWebAppResourceId string

@description('Static Web App default hostname (e.g., foo.azurestaticapps.net or foo.eastus2.azurestaticapps.net).')
param staticWebAppDefaultHostname string

@description('TTL for record sets (seconds). Spec called out 12,960,000.')
param recordTtl int = 12960000

// DNS Zones are global
resource zone 'Microsoft.Network/dnsZones@2023-07-01-preview' = {
  name: zoneName
  location: 'global'
  tags: tags
  properties: {
    zoneType: 'Public'
  }
}

// Apex A record — alias to the Static Web App
resource apexA 'Microsoft.Network/dnsZones/A@2023-07-01-preview' = {
  parent: zone
  name: '@'
  properties: {
    TTL: recordTtl
    targetResource: {
      id: staticWebAppResourceId
    }
  }
}

// www CNAME — points to the SWA default hostname
// Using a CNAME for www is the conventional companion to an apex A-alias.
resource wwwCname 'Microsoft.Network/dnsZones/CNAME@2023-07-01-preview' = {
  parent: zone
  name: 'www'
  properties: {
    TTL: recordTtl
    CNAMERecord: {
      cname: staticWebAppDefaultHostname
    }
  }
}

output id string = zone.id
output nameServers array = zone.properties.nameServers
