// =============================================================================
// dnsWwwCname.bicep — www CNAME record in an existing DNS zone
// Separated from dnsZone.bicep so it can depend on the SWA output
// without blocking the DNS zone deployment.
// =============================================================================

@description('DNS zone name (e.g., martajazz.com).')
param zoneName string

@description('SWA default hostname to point www at (e.g., foo.azurestaticapps.net).')
param swaDefaultHostname string

@description('TTL for the record set (seconds).')
param recordTtl int = 12960000

resource zone 'Microsoft.Network/dnsZones@2023-07-01-preview' existing = {
  name: zoneName
}

resource wwwCname 'Microsoft.Network/dnsZones/CNAME@2023-07-01-preview' = {
  parent: zone
  name: 'www'
  properties: {
    TTL: recordTtl
    CNAMERecord: {
      cname: swaDefaultHostname
    }
  }
}
