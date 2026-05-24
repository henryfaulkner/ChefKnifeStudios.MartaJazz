// =============================================================================
// dnsApexA.bicep — Apex A-alias record in an existing DNS zone
// Separated from dnsZone.bicep so it can depend on the SWA existing
// without blocking the DNS zone deployment.
// =============================================================================

@description('DNS zone name (e.g., martajazz.com).')
param zoneName string

@description('Static Web App resource ID to alias the apex A record to.')
param staticWebAppResourceId string

@description('TTL for the record set (seconds).')
param recordTtl int = 12960000

resource zone 'Microsoft.Network/dnsZones@2023-07-01-preview' existing = {
  name: zoneName
}

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
