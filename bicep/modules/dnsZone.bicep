// =============================================================================
// dnsZone.bicep — Public DNS Zone + apex A-alias + www CNAME
// =============================================================================

@description('DNS zone name (e.g., martajazz.com). Note: this resource intentionally breaks the project naming convention because the zone name IS the domain.')
param zoneName string

@description('Resource tags.')
param tags object

// DNS Zones are global
resource zone 'Microsoft.Network/dnsZones@2023-07-01-preview' = {
  name: zoneName
  location: 'global'
  tags: tags
  properties: {
    zoneType: 'Public'
  }
}


output id string = zone.id
output nameServers array = zone.properties.nameServers
