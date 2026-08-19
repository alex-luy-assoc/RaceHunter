variable "project_id" {
  description = "Google Cloud project dedicated to the RaceHunter staging environment."
  type        = string

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{4,28}[a-z0-9]$", var.project_id))
    error_message = "project_id must be a valid Google Cloud project ID."
  }
}

variable "region" {
  description = "Regional home for the staging Artifact Registry repository and state bucket."
  type        = string
  default     = "us-east1"

  validation {
    condition     = can(regex("^[a-z]+-[a-z]+[0-9]$", var.region))
    error_message = "region must be a valid Google Cloud region."
  }
}

variable "state_bucket_name" {
  description = "Globally unique private bucket name for protected Terraform state."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9._-]{1,61}[a-z0-9]$", var.state_bucket_name))
    error_message = "state_bucket_name must be a valid Google Cloud Storage bucket name."
  }
}

variable "state_retention_days" {
  description = "Minimum retention window for every protected Terraform state object version."
  type        = number
  default     = 30

  validation {
    condition     = var.state_retention_days >= 7 && var.state_retention_days <= 365 && floor(var.state_retention_days) == var.state_retention_days
    error_message = "state_retention_days must be a whole number from 7 through 365."
  }
}

variable "artifact_registry_repository" {
  description = "Docker repository that stores immutable RaceHunter release images."
  type        = string
  default     = "racehunter"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{2,62}$", var.artifact_registry_repository))
    error_message = "artifact_registry_repository must be a valid Artifact Registry repository ID."
  }
}
