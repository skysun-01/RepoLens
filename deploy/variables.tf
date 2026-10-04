variable "app_name" {
  type        = string
  description = "Short name used in Azure resource names."
  default     = "repolens"

  validation {
    condition     = can(regex("^[a-z0-9]{3,12}$", var.app_name))
    error_message = "app_name must contain 3-12 lowercase letters or digits."
  }
}

variable "location" {
  type    = string
  default = "eastus2"
}

variable "resource_group_name" {
  type    = string
  default = "rg-repolens"
}

variable "api_image" {
  type    = string
  default = "mcr.microsoft.com/k8se/quickstart:latest"
}

variable "worker_image" {
  type    = string
  default = "mcr.microsoft.com/k8se/quickstart:latest"
}

variable "github_client_id" {
  type = string
}

variable "github_client_secret" {
  type      = string
  sensitive = true
}

variable "frontend_auth_callback_url" {
  type = string
}

variable "frontend_origins" {
  type    = list(string)
  default = []
}

variable "mongo_admin_user" {
  type    = string
  default = "repolensadmin"
}

variable "mongo_admin_password" {
  type      = string
  sensitive = true
}

variable "mongo_tier" {
  type    = string
  default = "M30"
}

variable "jwt_signing_key" {
  type      = string
  sensitive = true
  default   = null
}

variable "chat_model_name" {
  type    = string
  default = "gpt-5-mini"
}

variable "chat_model_version" {
  type    = string
  default = "2025-08-07"
}

variable "embedding_model_name" {
  type    = string
  default = "text-embedding-3-small"
}

variable "embedding_model_version" {
  type    = string
  default = "1"
}