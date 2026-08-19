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

output "worker_audience" {
  description = "Exact Cloud Run audience accepted for API and Pub/Sub worker identity tokens."
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

output "reference_target_audience" {
  description = "Exact Cloud Run audience accepted for worker-to-target identity tokens."
  value       = google_cloud_run_v2_service.reference_target.uri
}

output "service_account_emails" {
  description = "Keyless workload identities used by topology verification."
  value = {
    api         = google_service_account.api.email
    worker      = google_service_account.worker.email
    target      = google_service_account.target.email
    pubsub_push = google_service_account.pubsub_push.email
  }
}

output "pubsub_topic" {
  description = "Authenticated asynchronous work topic."
  value       = google_pubsub_topic.work.id
}

output "cloud_sql_connection_name" {
  description = "Primary application Cloud SQL instance connection identifier."
  value       = google_sql_database_instance.main.connection_name
}

output "target_cloud_sql_connection_name" {
  description = "Credential-isolated reference-target Cloud SQL instance identifier."
  value       = google_sql_database_instance.target.connection_name
}
