output "state_bucket_name" {
  description = "Private versioned bucket supplied to the application GCS backend configuration."
  value       = google_storage_bucket.terraform_state.name
}

output "state_bucket_location" {
  description = "Location of the protected Terraform state bucket."
  value       = google_storage_bucket.terraform_state.location
}

output "artifact_registry_repository" {
  description = "Artifact Registry repository ID used for the three RaceHunter images."
  value       = google_artifact_registry_repository.images.repository_id
}

output "artifact_registry_location" {
  description = "Artifact Registry location used to construct publication references."
  value       = google_artifact_registry_repository.images.location
}

output "artifact_registry_hostname" {
  description = "Non-secret Docker registry hostname used by the release workflow."
  value       = "${google_artifact_registry_repository.images.location}-docker.pkg.dev"
}

output "enabled_services" {
  description = "Declared foundation APIs reconciled before application planning."
  value       = sort(tolist(local.required_services))
}
