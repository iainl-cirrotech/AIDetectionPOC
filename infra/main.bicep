targetScope = 'resourceGroup'

@description('Azure region for all resources. Data residency is determined by this value.')
param location string = 'uksouth'

@description('Short lowercase prefix used to name resources.')
@minLength(3)
@maxLength(12)
param namePrefix string = 'aidetect'

@description('Internal-use shared mailbox that the Logic App monitors.')
param sharedMailboxAddress string

@description('Container image for the combined .NET detector API and results site.')
param appImage string

@description('Hugging Face model identifier for the AI-generation classifier.')
param detectorModelId string = 'Thermostatic/community-forensics-frontier-detector-2026-08'

@description('Model version label recorded in every result for auditability.')
param detectorModelVersion string = 'frontier-2026-08'

@description('Optional pinned model revision (commit hash) for reproducibility.')
param detectorModelRevision string = '16db135220b318d811b207db576d90368980b595'

@description('Probability at or above which the band is High.')
param bandHighThreshold string = '0.65'

@description('Probability at or above which the band is Medium.')
param bandLowThreshold string = '0.35'

@description('API key required by the detector endpoint. Generated if left blank.')
@secure()
param detectorApiKey string = ''

@description('Log Analytics retention in days.')
param logsRetentionDays int = 90

@description('Days after which persisted thumbnails are automatically deleted by the storage lifecycle policy.')
param thumbnailRetentionDays int = 7

@description('Days after which result records are deleted by the application. Set to 0 to disable.')
param resultsRetentionDays int = 30

@description('Logic App run history retention in days (minimum 7). Kept low so attachment payloads are not retained.')
param logicAppRetentionDays int = 7

@description('Application minimum replicas. Use 1 for a warm demo, 0 to scale to zero.')
param appMinReplicas int = 1

var prefix = toLower(namePrefix)
var storageName = take('${prefix}st${uniqueString(resourceGroup().id)}', 24)
var acrName = take('${prefix}acr${uniqueString(resourceGroup().id)}', 50)
var apiKey = empty(detectorApiKey) ? uniqueString(resourceGroup().id, 'detector-api-key') : detectorApiKey
var blobEndpoint = 'https://${storageName}.blob.${environment().suffixes.storage}'
var workflowDefinition = union(json(loadTextContent('../logicapp/workflow.json')).definition, {
  runtimeConfiguration: {
    lifetime: {
      unit: 'day'
      count: logicAppRetentionDays
    }
  }
})

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: {
    retentionInDays: logsRetentionDays
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-appi'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource thumbnailContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'thumbnails'
  properties: {
    publicAccess: 'None'
  }
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource resultsTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: 'detections'
}

resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          enabled: true
          name: 'delete-thumbnails'
          type: 'Lifecycle'
          definition: {
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: thumbnailRetentionDays
                }
              }
            }
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                'thumbnails/'
              ]
            }
          }
        }
      ]
    }
  }
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-id'
  location: location
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, identity.id, 'acrpull')
  scope: acr
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, 'blob')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource tableContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, 'table')
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3')
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  // Reuse the original detector name so existing Logic App URLs migrate in place.
  name: '${prefix}-detector'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: identity.id
        }
      ]
      secrets: [
        {
          name: 'detector-api-key'
          value: apiKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'app'
          image: appImage
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
          env: [
            { name: 'AZURE_REGION', value: location }
            { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
            { name: 'DETECTOR_CLASSIFIER', value: 'onnx' }
            { name: 'DETECTOR_MODEL_PATH', value: '/app/models/community_forensics_frontier_fp16.onnx' }
            { name: 'DETECTOR_MODEL_ID', value: detectorModelId }
            { name: 'DETECTOR_MODEL_VERSION', value: detectorModelVersion }
            { name: 'DETECTOR_MODEL_REVISION', value: detectorModelRevision }
            { name: 'BAND_HIGH_THRESHOLD', value: bandHighThreshold }
            { name: 'BAND_LOW_THRESHOLD', value: bandLowThreshold }
            { name: 'STORAGE_ACCOUNT_URL', value: blobEndpoint }
            { name: 'STORE_BACKEND', value: 'azure' }
            { name: 'THUMBNAIL_CONTAINER', value: 'thumbnails' }
            { name: 'RESULTS_TABLE', value: 'detections' }
            { name: 'RESULTS_RETENTION_DAYS', value: string(resultsRetentionDays) }
            { name: 'C2PATOOL_PATH', value: '/usr/local/bin/c2patool' }
            { name: 'C2PA_TRUST_FILE', value: '/app/c2pa_trust/C2PA-TRUST-LIST.pem' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
            { name: 'DETECTOR_API_KEY', secretRef: 'detector-api-key' }
          ]
        }
      ]
      scale: {
        minReplicas: appMinReplicas
        maxReplicas: 3
      }
    }
  }
}

resource office365Connection 'Microsoft.Web/connections@2016-06-01' = {
  name: '${prefix}-office365'
  location: location
  properties: {
    displayName: 'Office 365 Outlook'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'office365')
    }
  }
}

resource logicApp 'Microsoft.Logic/workflows@2019-05-01' = {
  name: '${prefix}-mail-pickup'
  location: location
  properties: {
    definition: workflowDefinition
    parameters: {
      '$connections': {
        value: {
          office365: {
            connectionId: office365Connection.id
            connectionName: office365Connection.name
            id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'office365')
          }
        }
      }
      sharedMailboxAddress: {
        value: sharedMailboxAddress
      }
      detectorUrl: {
        value: 'https://${app.properties.configuration.ingress.fqdn}/analyze'
      }
      detectorApiKey: {
        value: apiKey
      }
    }
  }
}

resource storageDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: blobService
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      { category: 'StorageRead', enabled: true }
      { category: 'StorageWrite', enabled: true }
      { category: 'StorageDelete', enabled: true }
    ]
  }
}

output webUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
output detectorUrl string = 'https://${app.properties.configuration.ingress.fqdn}/analyze'
output storageAccountName string = storage.name
output logicAppName string = logicApp.name
output office365ConnectionName string = office365Connection.name
output managedIdentityPrincipalId string = identity.properties.principalId
