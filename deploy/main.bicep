// RepoLens on Azure: Container Apps (API + Worker), Service Bus, Cosmos DB for MongoDB (vCore),
// Azure OpenAI, Key Vault, Container Registry and Application Insights, wired with one managed identity.
//
//   az group create -n rg-repolens -l eastus2
//   az deployment group create -g rg-repolens -f deploy/main.bicep -p deploy/main.bicepparam
//
// The first deployment runs placeholder images. Build and push the real images (see README), then
// deploy again with apiImage/workerImage set.

targetScope = 'resourceGroup'

@description('Short name used in resource names: 3-12 lowercase letters or digits.')
@minLength(3)
@maxLength(12)
param appName string = 'repolens'

param location string = resourceGroup().location

@description('API image, for example myregistry.azurecr.io/repolens-api:1.0.0. The default is a placeholder for the first deployment.')
param apiImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Worker image, for example myregistry.azurecr.io/repolens-worker:1.0.0.')
param workerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('GitHub OAuth App client id. Its callback URL must be https://<api-fqdn>/api/v1/auth/github/callback (see outputs).')
param gitHubClientId string

@secure()
@description('GitHub OAuth App client secret. Stored in Key Vault.')
param gitHubClientSecret string

@description('Frontend page that receives ?code=... after sign-in, for example https://app.example.com/auth/callback.')
param frontendAuthCallbackUrl string

@description('Browser origins allowed to call the API (CORS).')
param frontendOrigins array = []

@description('Administrator user of the Cosmos DB for MongoDB (vCore) cluster.')
param mongoAdminUser string = 'repolensadmin'

@secure()
@minLength(8)
@description('Administrator password of the Cosmos DB for MongoDB (vCore) cluster.')
param mongoAdminPassword string

@description('Cluster tier. M30 or higher is recommended for vector search.')
param mongoTier string = 'M30'

@secure()
@description('JWT signing key (at least 32 characters). Generated when omitted; stored in Key Vault.')
param jwtSigningKey string = '${newGuid()}${newGuid()}'

param chatModelName string = 'gpt-5-mini'
param chatModelVersion string = '2025-08-07'
param chatDeploymentSku string = 'GlobalStandard'
param chatCapacity int = 50

param embeddingModelName string = 'text-embedding-3-small'
param embeddingModelVersion string = '1'
param embeddingDeploymentSku string = 'GlobalStandard'
param embeddingCapacity int = 120

var suffix = uniqueString(resourceGroup().id, appName)

var names = {
  identity: '${appName}-id-${suffix}'
  logs: '${appName}-logs-${suffix}'
  insights: '${appName}-insights-${suffix}'
  registry: take('${appName}acr${suffix}', 50)
  serviceBus: '${appName}-sb-${suffix}'
  keyVault: take('${appName}-kv-${suffix}', 24)
  mongo: '${appName}-mongo-${suffix}'
  openAi: '${appName}-oai-${suffix}'
  environment: '${appName}-env-${suffix}'
  api: '${appName}-api'
  worker: '${appName}-worker'
}

var summaryRequestsTopic = 'summary-requests-topic'
var summaryRequestsSubscription = 'summary-worker'
var summaryEventsTopic = 'summary-events'

// Built-in role definition ids.
var roles = {
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  serviceBusDataSender: '69a216fc-b8fb-44d8-bc22-1f3c2552ae39'
  serviceBusDataReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  keyVaultCryptoUser: '12338af0-0e69-4776-bea7-57ae8d297424'
  cognitiveServicesOpenAiUser: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
}

// ---------------------------------------------------------------------------
// Identity and observability
// ---------------------------------------------------------------------------

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.identity
  location: location
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: names.logs
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: names.insights
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------------------------------------------------------------------------
// Container registry
// ---------------------------------------------------------------------------

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: names.registry
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

resource registryPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, identity.id, roles.acrPull)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Service Bus: command topic/subscription (with dead-lettering) and event topic
// ---------------------------------------------------------------------------

resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: names.serviceBus
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource summaryRequests 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBus
  name: summaryRequestsTopic
  properties: {
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    defaultMessageTimeToLive: 'P7D'
  }
}

resource summaryRequestsWorker 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: summaryRequests
  name: summaryRequestsSubscription
  properties: {
    // Must equal Messaging:MaxDeliveryCount in the app.
    maxDeliveryCount: 5
    lockDuration: 'PT5M'
    deadLetteringOnMessageExpiration: true
    defaultMessageTimeToLive: 'P7D'
  }
}

resource summaryEvents 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBus
  name: summaryEventsTopic
  properties: {
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    defaultMessageTimeToLive: 'P7D'
  }
}

// KEDA reads the topic subscription backlog through this rule (the scaler needs Manage rights).
resource kedaScalerRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2022-10-01-preview' = {
  parent: serviceBus
  name: 'keda-scaler'
  properties: {
    rights: [
      'Manage'
      'Listen'
      'Send'
    ]
  }
}

resource serviceBusSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, identity.id, roles.serviceBusDataSender)
  scope: serviceBus
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusDataSender)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource serviceBusReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBus.id, identity.id, roles.serviceBusDataReceiver)
  scope: serviceBus
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusDataReceiver)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Cosmos DB for MongoDB (vCore): all application data, vectors and PDFs (GridFS)
// ---------------------------------------------------------------------------

resource mongo 'Microsoft.DocumentDB/mongoClusters@2024-07-01' = {
  name: names.mongo
  location: location
  properties: {
    administrator: {
      userName: mongoAdminUser
      password: mongoAdminPassword
    }
    serverVersion: '7.0'
    compute: {
      tier: mongoTier
    }
    storage: {
      sizeGb: 32
    }
    sharding: {
      shardCount: 1
    }
    highAvailability: {
      targetMode: 'Disabled'
    }
    publicNetworkAccess: 'Enabled'
  }
}

// Allows connections from Azure services (the Container Apps). Use private endpoints for stricter isolation.
resource mongoFirewall 'Microsoft.DocumentDB/mongoClusters/firewallRules@2024-07-01' = {
  parent: mongo
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

var mongoConnectionString = replace(
  replace(mongo.properties.connectionString, '<user>', uriComponent(mongoAdminUser)),
  '<password>',
  uriComponent(mongoAdminPassword)
)

// ---------------------------------------------------------------------------
// Azure OpenAI (Entra ID only; no API keys)
// ---------------------------------------------------------------------------

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: names.openAi
  location: location
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: names.openAi
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: true
  }
}

resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: chatModelName
  sku: {
    name: chatDeploymentSku
    capacity: chatCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: chatModelName
      version: chatModelVersion
    }
  }
}

resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: embeddingModelName
  sku: {
    name: embeddingDeploymentSku
    capacity: embeddingCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: embeddingModelName
      version: embeddingModelVersion
    }
  }
  // Deployments on one account must be created one at a time.
  dependsOn: [
    chatDeployment
  ]
}

resource openAiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openAi.id, identity.id, roles.cognitiveServicesOpenAiUser)
  scope: openAi
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.cognitiveServicesOpenAiUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Key Vault: secrets loaded as configuration, and the key protecting the Data Protection key ring
// ---------------------------------------------------------------------------

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: names.keyVault
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
  }
}

// Secret names use "--" for ":" (Mongo--ConnectionString becomes Mongo:ConnectionString).
resource mongoSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Mongo--ConnectionString'
  properties: {
    value: mongoConnectionString
  }
}

resource gitHubSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'GitHub--ClientSecret'
  properties: {
    value: gitHubClientSecret
  }
}

resource jwtSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Auth--Jwt--SigningKey'
  properties: {
    value: jwtSigningKey
  }
}

resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: keyVault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, roles.keyVaultSecretsUser)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource keyVaultCryptoUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, roles.keyVaultCryptoUser)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultCryptoUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// Container Apps
// ---------------------------------------------------------------------------

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: names.environment
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// Settings shared by the API and the Worker. Secrets come from Key Vault through KeyVault__Uri.
var sharedEnv = [
  { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
  { name: 'KeyVault__Uri', value: keyVault.properties.vaultUri }
  { name: 'DataProtection__KeyVaultKeyId', value: dataProtectionKey.properties.keyUri }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
  { name: 'Mongo__DatabaseName', value: 'repolens' }
  { name: 'Messaging__Provider', value: 'ServiceBus' }
  { name: 'Messaging__ServiceBus__FullyQualifiedNamespace', value: '${serviceBus.name}.servicebus.windows.net' }
  { name: 'Messaging__ServiceBus__SummaryRequestsTopic', value: summaryRequestsTopic }
  { name: 'Messaging__ServiceBus__SummaryRequestsSubscription', value: summaryRequestsSubscription }
  { name: 'Messaging__ServiceBus__SummaryEventsTopic', value: summaryEventsTopic }
  { name: 'Ai__Provider', value: 'AzureOpenAI' }
  { name: 'Ai__Endpoint', value: openAi.properties.endpoint }
  { name: 'Ai__ChatModel', value: chatDeployment.name }
  { name: 'Ai__EmbeddingModel', value: embeddingDeployment.name }
  { name: 'VectorSearch__Provider', value: 'CosmosVCore' }
]

var corsEnv = [for (origin, i) in frontendOrigins: {
  name: 'Frontend__AllowedOrigins__${i}'
  value: origin
}]

var registries = [
  {
    server: registry.properties.loginServer
    identity: identity.id
  }
]

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: names.api
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat(sharedEnv, corsEnv, [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'GitHub__ClientId', value: gitHubClientId }
            { name: 'Frontend__AuthCallbackUrl', value: frontendAuthCallbackUrl }
          ])
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
  dependsOn: [
    registryPull
    serviceBusSender
    keyVaultSecretsUser
    keyVaultCryptoUser
    openAiUser
    mongoFirewall
    mongoSecret
    gitHubSecret
    jwtSecret
  ]
}

resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: names.worker
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      registries: registries
      secrets: [
        {
          name: 'servicebus-keda'
          value: kedaScalerRule.listKeys().primaryConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: workerImage
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
          env: concat(sharedEnv, [
            { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
          ])
        }
      ]
      // Scales to zero when idle; one replica per two waiting summary requests, up to 10.
      scale: {
        minReplicas: 0
        maxReplicas: 10
        rules: [
          {
            name: 'summary-requests'
            custom: {
              type: 'azure-servicebus'
              metadata: {
                topicName: summaryRequestsTopic
                subscriptionName: summaryRequestsSubscription
                namespace: serviceBus.name
                messageCount: '2'
              }
              auth: [
                {
                  secretRef: 'servicebus-keda'
                  triggerParameter: 'connection'
                }
              ]
            }
          }
        ]
      }
    }
  }
  dependsOn: [
    registryPull
    serviceBusSender
    serviceBusReceiver
    keyVaultSecretsUser
    keyVaultCryptoUser
    openAiUser
    mongoFirewall
    mongoSecret
    summaryRequestsWorker
    summaryEvents
  ]
}

// ---------------------------------------------------------------------------
// Outputs
// ---------------------------------------------------------------------------

output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output gitHubOAuthCallbackUrl string = 'https://${api.properties.configuration.ingress.fqdn}/api/v1/auth/github/callback'
output registryLoginServer string = registry.properties.loginServer
output registryName string = registry.name
output keyVaultName string = keyVault.name
output serviceBusNamespace string = serviceBus.name
output mongoClusterName string = mongo.name
output openAiEndpoint string = openAi.properties.endpoint
