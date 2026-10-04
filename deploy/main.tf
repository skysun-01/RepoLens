// Terraform reference equivalent of deploy/main.bicep.
// This file targets Azure and is intentionally kept beside the Bicep deployment
// so the two approaches can be compared while learning Terraform.

terraform {
  required_version = ">= 1.9.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    azapi = {
      source  = "Azure/azapi"
      version = "~> 2.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}

provider "azurerm" {
  features {}
}

provider "azapi" {}

provider "random" {}

data "azurerm_client_config" "current" {}

resource "azurerm_resource_group" "this" {
  name     = var.resource_group_name
  location = var.location
}

resource "random_string" "suffix" {
  length  = 8
  lower   = true
  numeric = true
  special = false
  upper   = false
}

locals {
  suffix = random_string.suffix.result

  names = {
    identity    = "${var.app_name}-id-${local.suffix}"
    logs        = "${var.app_name}-logs-${local.suffix}"
    insights    = "${var.app_name}-insights-${local.suffix}"
    registry    = substr("${var.app_name}acr${local.suffix}", 0, 50)
    service_bus = "${var.app_name}-sb-${local.suffix}"
    key_vault   = substr("${var.app_name}-kv-${local.suffix}", 0, 24)
    mongo       = "${var.app_name}-mongo-${local.suffix}"
    openai      = "${var.app_name}-oai-${local.suffix}"
    environment = "${var.app_name}-env-${local.suffix}"
  }

  summary_requests_topic        = "summary-requests-topic"
  summary_requests_subscription = "summary-worker"
  summary_events_topic   = "summary-events"
  jwt_signing_key        = coalesce(var.jwt_signing_key, "${uuid()}${uuid()}")
}

resource "azurerm_user_assigned_identity" "app" {
  name                = local.names.identity
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
}

resource "azurerm_log_analytics_workspace" "logs" {
  name                = local.names.logs
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

resource "azurerm_application_insights" "this" {
  name                = local.names.insights
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  workspace_id        = azurerm_log_analytics_workspace.logs.id
  application_type    = "web"
}

resource "azurerm_container_registry" "this" {
  name                = local.names.registry
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  sku                 = "Basic"
  admin_enabled       = false
}

resource "azurerm_role_assignment" "registry_pull" {
  scope                = azurerm_container_registry.this.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

resource "azurerm_servicebus_namespace" "this" {
  name                = local.names.service_bus
  location            = var.location
  resource_group_name = azurerm_resource_group.this.name
  sku                 = "Standard"
  minimum_tls_version = "1.2"
}

resource "azurerm_servicebus_topic" "summary_requests" {
  name         = local.summary_requests_topic
  namespace_id = azurerm_servicebus_namespace.this.id

  requires_duplicate_detection             = true
  duplicate_detection_history_time_window  = "PT10M"
  default_message_ttl                      = "P7D"
}

resource "azurerm_servicebus_subscription" "summary_requests_worker" {
  name               = local.summary_requests_subscription
  topic_id           = azurerm_servicebus_topic.summary_requests.id
  max_delivery_count = 5
  lock_duration      = "PT5M"

  dead_lettering_on_message_expiration = true
  default_message_ttl                   = "P7D"
}

resource "azurerm_servicebus_topic" "summary_events" {
  name         = local.summary_events_topic
  namespace_id = azurerm_servicebus_namespace.this.id

  requires_duplicate_detection            = true
  duplicate_detection_history_time_window = "PT10M"
  default_message_ttl                     = "P7D"
}

resource "azurerm_servicebus_namespace_authorization_rule" "keda_scaler" {
  name         = "keda-scaler"
  namespace_id = azurerm_servicebus_namespace.this.id
  listen       = true
  send         = true
  manage       = true
}

resource "azurerm_role_assignment" "service_bus_sender" {
  scope                = azurerm_servicebus_namespace.this.id
  role_definition_name = "Azure Service Bus Data Sender"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

resource "azurerm_role_assignment" "service_bus_receiver" {
  scope                = azurerm_servicebus_namespace.this.id
  role_definition_name = "Azure Service Bus Data Receiver"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

// AzAPI is used here because the AzureRM provider does not expose every
// mongoClusters API property used by the Bicep deployment.
resource "azapi_resource" "mongo" {
  type      = "Microsoft.DocumentDB/mongoClusters@2024-07-01"
  name      = local.names.mongo
  parent_id = azurerm_resource_group.this.id
  location  = var.location

  response_export_values = ["properties.connectionString"]

  body = {
    properties = {
      administrator = {
        userName = var.mongo_admin_user
        password = var.mongo_admin_password
      }
      serverVersion = "7.0"
      compute       = { tier = var.mongo_tier }
      storage       = { sizeGb = 32 }
      sharding      = { shardCount = 1 }
      highAvailability = {
        targetMode = "Disabled"
      }
      publicNetworkAccess = "Enabled"
    }
  }
}

resource "azapi_resource" "mongo_firewall" {
  type      = "Microsoft.DocumentDB/mongoClusters/firewallRules@2024-07-01"
  name      = "AllowAllAzureServicesAndResourcesWithinAzureIps"
  parent_id = azapi_resource.mongo.id

  body = {
    properties = {
      startIpAddress = "0.0.0.0"
      endIpAddress   = "0.0.0.0"
    }
  }
}

resource "azurerm_cognitive_account" "openai" {
  name                  = local.names.openai
  location              = var.location
  resource_group_name   = azurerm_resource_group.this.name
  kind                  = "OpenAI"
  sku_name              = "S0"
  custom_subdomain_name = local.names.openai

  public_network_access_enabled = true
  local_auth_enabled             = false
}

resource "azurerm_cognitive_deployment" "chat" {
  name                 = var.chat_model_name
  cognitive_account_id = azurerm_cognitive_account.openai.id

  model {
    format  = "OpenAI"
    name    = var.chat_model_name
    version = var.chat_model_version
  }

  scale {
    type     = "GlobalStandard"
    capacity = 50
  }
}

resource "azurerm_cognitive_deployment" "embedding" {
  name                 = var.embedding_model_name
  cognitive_account_id = azurerm_cognitive_account.openai.id

  model {
    format  = "OpenAI"
    name    = var.embedding_model_name
    version = var.embedding_model_version
  }

  scale {
    type     = "GlobalStandard"
    capacity = 120
  }

  depends_on = [azurerm_cognitive_deployment.chat]
}

resource "azurerm_role_assignment" "openai_user" {
  scope                = azurerm_cognitive_account.openai.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

resource "azurerm_key_vault" "this" {
  name                       = local.names.key_vault
  location                   = var.location
  resource_group_name        = azurerm_resource_group.this.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  soft_delete_retention_days = 90
  purge_protection_enabled   = true
  enable_rbac_authorization  = true
}

resource "azurerm_key_vault_secret" "mongo_connection_string" {
  name         = "Mongo--ConnectionString"
  value        = replace(replace(azapi_resource.mongo.output.properties.connectionString, "<user>", var.mongo_admin_user), "<password>", var.mongo_admin_password)
  key_vault_id = azurerm_key_vault.this.id
}

resource "azurerm_key_vault_secret" "github_client_secret" {
  name         = "GitHub--ClientSecret"
  value        = var.github_client_secret
  key_vault_id = azurerm_key_vault.this.id
}

resource "azurerm_key_vault_secret" "jwt_signing_key" {
  name         = "Auth--Jwt--SigningKey"
  value        = local.jwt_signing_key
  key_vault_id = azurerm_key_vault.this.id
}

resource "azurerm_key_vault_key" "data_protection" {
  name         = "dataprotection"
  key_vault_id = azurerm_key_vault.this.id
  key_type     = "RSA"
  key_size     = 2048
  key_opts     = ["wrapKey", "unwrapKey"]
}

resource "azurerm_role_assignment" "key_vault_secrets_user" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

resource "azurerm_role_assignment" "key_vault_crypto_user" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Crypto User"
  principal_id         = azurerm_user_assigned_identity.app.principal_id
}

resource "azurerm_container_app_environment" "this" {
  name                       = local.names.environment
  location                   = var.location
  resource_group_name        = azurerm_resource_group.this.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.logs.id
}

locals {
  shared_environment_variables = [
    { name = "AZURE_CLIENT_ID", value = azurerm_user_assigned_identity.app.client_id },
    { name = "KeyVault__Uri", value = azurerm_key_vault.this.vault_uri },
    { name = "DataProtection__KeyVaultKeyId", value = azurerm_key_vault_key.data_protection.id },
    { name = "APPLICATIONINSIGHTS_CONNECTION_STRING", value = azurerm_application_insights.this.connection_string },
    { name = "Mongo__DatabaseName", value = "repolens" },
    { name = "Messaging__Provider", value = "ServiceBus" },
    { name = "Messaging__ServiceBus__FullyQualifiedNamespace", value = "${azurerm_servicebus_namespace.this.name}.servicebus.windows.net" },
    { name = "Messaging__ServiceBus__SummaryRequestsTopic", value = local.summary_requests_topic },
    { name = "Messaging__ServiceBus__SummaryRequestsSubscription", value = local.summary_requests_subscription },
    { name = "Messaging__ServiceBus__SummaryEventsTopic", value = local.summary_events_topic },
    { name = "Ai__Provider", value = "AzureOpenAI" },
    { name = "Ai__Endpoint", value = azurerm_cognitive_account.openai.endpoint },
    { name = "Ai__ChatModel", value = azurerm_cognitive_deployment.chat.name },
    { name = "Ai__EmbeddingModel", value = azurerm_cognitive_deployment.embedding.name },
    { name = "VectorSearch__Provider", value = "CosmosVCore" }
  ]

  cors_environment_variables = [
    for index, origin in var.frontend_origins : {
      name  = "Frontend__AllowedOrigins__${index}"
      value = origin
    }
  ]
}

resource "azurerm_container_app" "api" {
  name                         = "${var.app_name}-api"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.this.name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.app.id]
  }

  registry {
    server   = azurerm_container_registry.this.login_server
    identity = azurerm_user_assigned_identity.app.id
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  template {
    min_replicas = 1
    max_replicas = 5

    http_scale_rule {
      name                = "http"
      concurrent_requests = 50
    }

    container {
      name   = "api"
      image  = var.api_image
      cpu    = 0.5
      memory = "1Gi"

      dynamic "env" {
        for_each = concat(local.shared_environment_variables, local.cors_environment_variables, [
          { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
          { name = "GitHub__ClientId", value = var.github_client_id },
          { name = "Frontend__AuthCallbackUrl", value = var.frontend_auth_callback_url }
        ])
        content {
          name  = env.value.name
          value = env.value.value
        }
      }

      liveness_probe {
        transport        = "HTTP"
        port             = 8080
        path             = "/health/live"
        interval_seconds = 30
      }

      readiness_probe {
        transport        = "HTTP"
        port             = 8080
        path             = "/health/ready"
        interval_seconds = 15
      }
    }
  }

  depends_on = [
    azurerm_role_assignment.registry_pull,
    azurerm_role_assignment.service_bus_sender,
    azurerm_role_assignment.key_vault_secrets_user,
    azurerm_role_assignment.key_vault_crypto_user,
    azurerm_role_assignment.openai_user,
    azapi_resource.mongo_firewall
  ]
}

resource "azurerm_container_app" "worker" {
  name                         = "${var.app_name}-worker"
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = azurerm_resource_group.this.name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.app.id]
  }

  registry {
    server   = azurerm_container_registry.this.login_server
    identity = azurerm_user_assigned_identity.app.id
  }

  template {
    min_replicas = 0
    max_replicas = 10

    custom_scale_rule {
      name             = "summary-requests"
      custom_rule_type = "azure-servicebus"
      metadata = {
        topicName        = local.summary_requests_topic
        subscriptionName = local.summary_requests_subscription
        namespace        = azurerm_servicebus_namespace.this.name
        messageCount     = "2"
      }
      authentication {
        secret_name       = "servicebus-keda"
        trigger_parameter = "connection"
      }
    }

    container {
      name   = "worker"
      image  = var.worker_image
      cpu    = 1.0
      memory = "2Gi"

      dynamic "env" {
        for_each = local.shared_environment_variables
        content {
          name  = env.value.name
          value = env.value.value
        }
      }

      env {
        name  = "DOTNET_ENVIRONMENT"
        value = "Production"
      }
    }
  }

  secret {
    name  = "servicebus-keda"
    value = azurerm_servicebus_namespace_authorization_rule.keda_scaler.primary_connection_string
  }

  depends_on = [
    azurerm_role_assignment.registry_pull,
    azurerm_role_assignment.service_bus_sender,
    azurerm_role_assignment.service_bus_receiver,
    azurerm_role_assignment.key_vault_secrets_user,
    azurerm_role_assignment.key_vault_crypto_user,
    azurerm_role_assignment.openai_user,
    azapi_resource.mongo_firewall,
    azurerm_servicebus_subscription.summary_requests_worker,
    azurerm_servicebus_topic.summary_events
  ]
}

output "api_url" {
  value = "https://${azurerm_container_app.api.ingress[0].fqdn}"
}

output "github_oauth_callback_url" {
  value = "https://${azurerm_container_app.api.ingress[0].fqdn}/api/v1/auth/github/callback"
}

output "registry_login_server" {
  value = azurerm_container_registry.this.login_server
}

output "registry_name" {
  value = azurerm_container_registry.this.name
}

output "key_vault_name" {
  value = azurerm_key_vault.this.name
}

output "service_bus_namespace" {
  value = azurerm_servicebus_namespace.this.name
}

output "mongo_cluster_name" {
  value = azapi_resource.mongo.name
}

output "openai_endpoint" {
  value = azurerm_cognitive_account.openai.endpoint
}