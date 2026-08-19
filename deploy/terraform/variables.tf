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
  description = "Approved billing account that always receives the staging budget alert."
  type        = string

  validation {
    condition     = can(regex("^[A-Z0-9]{6}-[A-Z0-9]{6}-[A-Z0-9]{6}$", var.billing_account_id))
    error_message = "billing_account_id must be a valid billing account reference."
  }
}

variable "monthly_budget_usd" {
  description = "Monthly staging budget alert threshold."
  type        = number
  default     = 25
  validation {
    condition     = var.monthly_budget_usd > 0 && var.monthly_budget_usd <= 100 && floor(var.monthly_budget_usd) == var.monthly_budget_usd
    error_message = "monthly_budget_usd must be a positive whole-dollar amount no greater than 100."
  }
}

variable "api_max_instance_count" {
  description = "Hard Cloud Run scale ceiling for the public staging API."
  type        = number
  default     = 2
  validation {
    condition     = var.api_max_instance_count >= 1 && var.api_max_instance_count <= 2 && floor(var.api_max_instance_count) == var.api_max_instance_count
    error_message = "api_max_instance_count must be a whole number from 1 through 2."
  }
}

variable "reference_target_max_instance_count" {
  description = "Hard Cloud Run scale ceiling for the private staging reference target."
  type        = number
  default     = 1
  validation {
    condition     = var.reference_target_max_instance_count >= 1 && var.reference_target_max_instance_count <= 2 && floor(var.reference_target_max_instance_count) == var.reference_target_max_instance_count
    error_message = "reference_target_max_instance_count must be a whole number from 1 through 2."
  }
}

variable "worker_max_instance_count" {
  description = "Reviewed hard worker ceiling; one instance preserves deployment-wide process limiters."
  type        = number
  default     = 1
  validation {
    condition     = var.worker_max_instance_count == 1
    error_message = "worker_max_instance_count must remain exactly 1."
  }
}

variable "deletion_protection" {
  description = "Reviewed staging protection applied to both databases and all Cloud Run services."
  type        = bool
  default     = true
  validation {
    condition     = var.deletion_protection
    error_message = "deletion_protection must remain enabled."
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
