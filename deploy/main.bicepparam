using './main.bicep'

// Copy this file, fill in your values, and pass secrets through environment variables:
//   $env:GITHUB_CLIENT_SECRET = '...'; $env:MONGO_ADMIN_PASSWORD = '...'
//   az deployment group create -g rg-repolens -f deploy/main.bicep -p deploy/main.bicepparam

param appName = 'repolens'

param gitHubClientId = '<your GitHub OAuth App client id>'
param gitHubClientSecret = readEnvironmentVariable('GITHUB_CLIENT_SECRET')

param mongoAdminPassword = readEnvironmentVariable('MONGO_ADMIN_PASSWORD')

param frontendAuthCallbackUrl = 'https://app.example.com/auth/callback'
param frontendOrigins = [
  'https://app.example.com'
]

// After the first deployment, push images to the registry (see README) and set:
// param apiImage = '<registry>.azurecr.io/repolens-api:1.0.0'
// param workerImage = '<registry>.azurecr.io/repolens-worker:1.0.0'
