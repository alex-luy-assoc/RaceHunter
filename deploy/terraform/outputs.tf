output "api_url" {
  description = "Public judge-facing API and React URL."
  value       = google_cloud_run_v2_service.api.uri
}

output "worker_service" {
  description = "Private worker service name used as cloud execution proof."
  value       = google_cloud_run_v2_service.worker.name
}

output "worker_url" {
  description = "Private worker audience used for API and Pub/Sub identity tokens."
  value       = google_cloud_run_v2_service.worker.uri
}

output "reference_target_service" {
  description = "Private reference target service name."
  value       = google_cloud_run_v2_service.reference_target.name
}

output "reference_target_url" {
  description = "Private reference target audience used by worker identity tokens."
  value       = google_cloud_run_v2_service.reference_target.uri
}

output "pubsub_topic" {
  description = "Authenticated asynchronous work topic."
  value       = google_pubsub_topic.work.id
}

output "cloud_sql_connection_name" {
  description = "Cloud SQL instance connection identifier."
  value       = google_sql_database_instance.main.connection_name
}
