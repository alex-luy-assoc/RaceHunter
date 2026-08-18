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
