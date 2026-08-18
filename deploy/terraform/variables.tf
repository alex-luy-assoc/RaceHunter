variable "project_id" {
  description = "Google Cloud project dedicated to the RaceHunter staging environment."
  type        = string
}

variable "region" {
  description = "Regional home for Cloud Run, Cloud SQL, Pub/Sub, and Artifact Registry."
  type        = string
  default     = "us-east1"
}

variable "api_image" {
  description = "Immutable RaceHunter API image digest."
  type        = string
}

variable "worker_image" {
  description = "Immutable RaceHunter worker image digest."
  type        = string
}

variable "reference_target_image" {
  description = "Immutable RaceHunter reference-target image digest."
  type        = string
}

variable "billing_account_id" {
  description = "Optional billing account used to create the staging budget alert."
  type        = string
  default     = null
  nullable    = true
}

variable "monthly_budget_usd" {
  description = "Monthly staging budget alert threshold."
  type        = number
  default     = 25
  validation {
    condition     = var.monthly_budget_usd > 0
    error_message = "monthly_budget_usd must be positive."
  }
}

variable "max_instance_count" {
  description = "Hard per-service Cloud Run scale ceiling for staging cost containment."
  type        = number
  default     = 2
  validation {
    condition     = var.max_instance_count >= 1 && var.max_instance_count <= 10
    error_message = "max_instance_count must be between 1 and 10."
  }
}

variable "manual_target_secret_ids" {
  description = "Secret Manager secret resource IDs explicitly authorized for authenticated manual targets. Values are secret IDs, not secret data."
  type        = set(string)
  default     = []
  validation {
    condition     = alltrue([for id in var.manual_target_secret_ids : can(regex("^[A-Za-z0-9_-]{1,255}$", id))])
    error_message = "manual_target_secret_ids may contain only Secret Manager secret IDs."
  }
}
