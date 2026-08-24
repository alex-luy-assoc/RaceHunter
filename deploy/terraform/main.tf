resource "random_password" "primary_database" {
  length  = 32
  special = false
}

resource "random_password" "target_database" {
  length  = 32
  special = false
}

resource "random_password" "demo_control" {
  length  = 48
  special = false
}

resource "google_sql_database_instance" "main" {
  name                = "racehunter-staging"
  region              = var.region
  database_version    = "POSTGRES_17"
  deletion_protection = var.deletion_protection

  settings {
    edition               = "ENTERPRISE"
    tier                  = "db-f1-micro"
    availability_type     = "ZONAL"
    disk_type             = "PD_SSD"
    disk_size             = 10
    disk_autoresize       = true
    disk_autoresize_limit = 20
    backup_configuration {
      enabled                        = true
      point_in_time_recovery_enabled = true
    }
    ip_configuration {
      # Cloud Run reaches this address only through its authenticated Cloud SQL connector socket.
      ipv4_enabled = true
    }
  }
}

resource "google_sql_database_instance" "target" {
  name                = "racehunter-target-staging"
  region              = var.region
  database_version    = "POSTGRES_17"
  deletion_protection = var.deletion_protection

  settings {
    edition               = "ENTERPRISE"
    tier                  = "db-f1-micro"
    availability_type     = "ZONAL"
    disk_type             = "PD_SSD"
    disk_size             = 10
    disk_autoresize       = true
    disk_autoresize_limit = 20
    backup_configuration {
      enabled                        = true
      point_in_time_recovery_enabled = true
    }
    ip_configuration {
      # A separate instance makes target credentials unusable against primary data.
      ipv4_enabled = true
    }
  }
}

resource "google_sql_database" "racehunter" {
  name     = "racehunter"
  instance = google_sql_database_instance.main.name
}

resource "google_sql_database" "reference_target" {
  name     = "racehunter_target"
  instance = google_sql_database_instance.target.name
}

resource "google_sql_user" "racehunter" {
  name     = "racehunter"
  instance = google_sql_database_instance.main.name
  password = random_password.primary_database.result
}

resource "google_sql_user" "reference_target" {
  name     = "racehunter_target"
  instance = google_sql_database_instance.target.name
  password = random_password.target_database.result
}

resource "google_secret_manager_secret" "racehunter_database" {
  secret_id = "racehunter-database-connection"
  replication {
    auto {}
  }
}

resource "google_secret_manager_secret_version" "racehunter_database" {
  secret      = google_secret_manager_secret.racehunter_database.id
  secret_data = "Host=/cloudsql/${google_sql_database_instance.main.connection_name};Database=${google_sql_database.racehunter.name};Username=${google_sql_user.racehunter.name};Password=${random_password.primary_database.result};SSL Mode=Disable"
}

resource "google_secret_manager_secret" "target_database" {
  secret_id = "racehunter-target-database-connection"
  replication {
    auto {}
  }
}

resource "google_secret_manager_secret_version" "target_database" {
  secret      = google_secret_manager_secret.target_database.id
  secret_data = "Host=/cloudsql/${google_sql_database_instance.target.connection_name};Database=${google_sql_database.reference_target.name};Username=${google_sql_user.reference_target.name};Password=${random_password.target_database.result};SSL Mode=Disable"
}

resource "google_secret_manager_secret" "demo_control" {
  secret_id = "racehunter-demo-control-key"
  replication {
    auto {}
  }
}

resource "google_secret_manager_secret_version" "demo_control" {
  secret      = google_secret_manager_secret.demo_control.id
  secret_data = random_password.demo_control.result
}

resource "google_secret_manager_secret" "otel_collector_config" {
  secret_id = "racehunter-otel-collector-config"
  replication {
    auto {}
  }
}

resource "google_secret_manager_secret_version" "otel_collector_config" {
  secret      = google_secret_manager_secret.otel_collector_config.id
  secret_data = <<-YAML
    receivers:
      otlp:
        protocols:
          grpc:
            endpoint: 0.0.0.0:4317
    processors:
      batch: {}
      memory_limiter:
        check_interval: 1s
        limit_mib: 128
      resourcedetection:
        detectors: [gcp]
        timeout: 10s
        override: false
    exporters:
      googlecloud: {}
      googlemanagedprometheus: {}
    extensions:
      health_check:
        endpoint: 0.0.0.0:13133
    service:
      extensions: [health_check]
      pipelines:
        traces:
          receivers: [otlp]
          processors: [resourcedetection, memory_limiter, batch]
          exporters: [googlecloud]
        metrics:
          receivers: [otlp]
          processors: [resourcedetection, memory_limiter, batch]
          exporters: [googlemanagedprometheus]
  YAML
}

resource "google_service_account" "api" {
  account_id   = "racehunter-api"
  display_name = "RaceHunter public API"
}

resource "google_service_account" "worker" {
  account_id   = "racehunter-worker"
  display_name = "RaceHunter private execution worker"
}

resource "google_service_account" "target" {
  account_id   = "racehunter-target"
  display_name = "RaceHunter private reference target"
}

resource "google_service_account" "pubsub_push" {
  account_id   = "racehunter-pubsub-push"
  display_name = "Authenticated Pub/Sub push identity"
}

resource "google_project_iam_member" "cloud_sql_clients" {
  for_each = {
    api    = google_service_account.api.email
    worker = google_service_account.worker.email
    target = google_service_account.target.email
  }
  project = var.project_id
  role    = "roles/cloudsql.client"
  member  = "serviceAccount:${each.value}"
}

resource "google_project_iam_member" "worker_vertex" {
  project = var.project_id
  role    = "roles/aiplatform.user"
  member  = "serviceAccount:${google_service_account.worker.email}"
}

resource "google_project_iam_member" "telemetry_writers" {
  for_each = {
    "api-log"       = [google_service_account.api.email, "roles/logging.logWriter"]
    "api-metric"    = [google_service_account.api.email, "roles/monitoring.metricWriter"]
    "api-trace"     = [google_service_account.api.email, "roles/cloudtrace.agent"]
    "worker-log"    = [google_service_account.worker.email, "roles/logging.logWriter"]
    "worker-metric" = [google_service_account.worker.email, "roles/monitoring.metricWriter"]
    "worker-trace"  = [google_service_account.worker.email, "roles/cloudtrace.agent"]
    "target-log"    = [google_service_account.target.email, "roles/logging.logWriter"]
    "target-metric" = [google_service_account.target.email, "roles/monitoring.metricWriter"]
    "target-trace"  = [google_service_account.target.email, "roles/cloudtrace.agent"]
  }
  project = var.project_id
  role    = each.value[1]
  member  = "serviceAccount:${each.value[0]}"
}

resource "google_secret_manager_secret_iam_member" "otel_collector_config" {
  for_each = {
    api    = google_service_account.api.email
    worker = google_service_account.worker.email
    target = google_service_account.target.email
  }
  secret_id = google_secret_manager_secret.otel_collector_config.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${each.value}"
}

resource "google_secret_manager_secret_iam_member" "api_database" {
  secret_id = google_secret_manager_secret.racehunter_database.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.api.email}"
}

resource "google_secret_manager_secret_iam_member" "worker_database" {
  secret_id = google_secret_manager_secret.racehunter_database.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.worker.email}"
}

resource "google_secret_manager_secret_iam_member" "target_database" {
  secret_id = google_secret_manager_secret.target_database.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.target.email}"
}

resource "google_secret_manager_secret_iam_member" "target_demo_control" {
  secret_id = google_secret_manager_secret.demo_control.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.target.email}"
}

resource "google_secret_manager_secret_iam_member" "worker_demo_control" {
  secret_id = google_secret_manager_secret.demo_control.id
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.worker.email}"
}

resource "google_secret_manager_secret_iam_member" "worker_manual_targets" {
  for_each  = var.manual_target_secret_ids
  secret_id = each.value
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.worker.email}"
}

resource "google_cloud_run_v2_service" "api" {
  name                = "racehunter-api"
  location            = var.region
  ingress             = "INGRESS_TRAFFIC_ALL"
  deletion_protection = var.deletion_protection

  template {
    service_account = google_service_account.api.email
    scaling { max_instance_count = var.api_max_instance_count }
    max_instance_request_concurrency = 80
    volumes {
      name = "cloudsql"
      cloud_sql_instance { instances = [google_sql_database_instance.main.connection_name] }
    }
    volumes {
      name = "otel-config"
      secret {
        secret = google_secret_manager_secret.otel_collector_config.secret_id
        items {
          version = google_secret_manager_secret_version.otel_collector_config.version
          path    = "config.yaml"
        }
      }
    }
    containers {
      name  = "api"
      image = var.api_image
      resources { limits = { cpu = "1", memory = "512Mi" } }
      ports { container_port = 8080 }
      env {
        name = "ConnectionStrings__RaceHunter"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.racehunter_database.secret_id
            version = google_secret_manager_secret_version.racehunter_database.version
          }
        }
      }
      env {
        name  = "PubSub__ProjectId"
        value = var.project_id
      }
      env {
        name  = "PubSub__TopicId"
        value = google_pubsub_topic.work.name
      }
      env {
        name  = "PubSub__DeadLetterTopicId"
        value = google_pubsub_topic.dead_letter.name
      }
      env {
        name  = "Worker__BaseUrl"
        value = google_cloud_run_v2_service.worker.uri
      }
      env {
        name  = "Worker__Audience"
        value = google_cloud_run_v2_service.worker.uri
      }
      env {
        name  = "Worker__RequireAuthentication"
        value = "true"
      }
      env {
        name  = "CloudProof__WorkerService"
        value = google_cloud_run_v2_service.worker.name
      }
      env {
        name  = "CloudProof__CloudSqlInstance"
        value = google_sql_database_instance.main.connection_name
      }
      env {
        name  = "CloudProof__ModelId"
        value = "gemini-3.5-flash"
      }
      env {
        name  = "OTEL_SERVICE_NAME"
        value = "racehunter-api"
      }
      env {
        name  = "OTEL_EXPORTER_OTLP_ENDPOINT"
        value = "http://localhost:4317"
      }
      volume_mounts {
        name       = "cloudsql"
        mount_path = "/cloudsql"
      }
      startup_probe {
        http_get {
          path = "/healthz"
        }
      }
    }
    containers {
      name = "otel-collector"
      # Digest pinning is explicitly deferred until an approved collector digest is supplied;
      # credential-free Phase 2 does not query a registry or invent immutable identity evidence.
      image   = "us-docker.pkg.dev/cloud-ops-agents-artifacts/google-cloud-opentelemetry-collector/otelcol-google:0.156.0"
      command = ["/otelcol-google"]
      args    = ["--config=/etc/otelcol-google/config.yaml"]
      resources { limits = { cpu = "1", memory = "256Mi" } }
      volume_mounts {
        name       = "otel-config"
        mount_path = "/etc/otelcol-google"
      }
      startup_probe {
        http_get {
          path = "/"
          port = 13133
        }
      }
    }
  }

  depends_on = [
    google_secret_manager_secret_version.racehunter_database,
    google_secret_manager_secret_version.otel_collector_config,
    google_secret_manager_secret_iam_member.api_database,
    google_secret_manager_secret_iam_member.otel_collector_config,
    google_project_iam_member.telemetry_writers
  ]
}

resource "google_cloud_run_v2_service" "worker" {
  name     = "racehunter-worker"
  location = var.region
  # The run.app endpoint is internet-routable so Cloud Run service-to-service calls work
  # without an unconfigured VPC route; run.invoker IAM still makes the service private.
  ingress             = "INGRESS_TRAFFIC_ALL"
  deletion_protection = var.deletion_protection

  template {
    service_account = google_service_account.worker.email
    # One worker owns the process-wide global and target limiters.
    scaling {
      max_instance_count = var.worker_max_instance_count
    }
    max_instance_request_concurrency = 1
    volumes {
      name = "cloudsql"
      cloud_sql_instance { instances = [google_sql_database_instance.main.connection_name] }
    }
    volumes {
      name = "otel-config"
      secret {
        secret = google_secret_manager_secret.otel_collector_config.secret_id
        items {
          version = google_secret_manager_secret_version.otel_collector_config.version
          path    = "config.yaml"
        }
      }
    }
    containers {
      name  = "worker"
      image = var.worker_image
      resources { limits = { cpu = "1", memory = "1Gi" } }
      ports { container_port = 8080 }
      env {
        name = "ConnectionStrings__RaceHunter"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.racehunter_database.secret_id
            version = google_secret_manager_secret_version.racehunter_database.version
          }
        }
      }
      env {
        name = "ReferenceTarget__DemoControlKey"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.demo_control.secret_id
            version = google_secret_manager_secret_version.demo_control.version
          }
        }
      }
      env {
        name  = "ReferenceTarget__BaseUrl"
        value = google_cloud_run_v2_service.reference_target.uri
      }
      env {
        name  = "ReferenceTarget__Audience"
        value = google_cloud_run_v2_service.reference_target.uri
      }
      env {
        name  = "ReferenceTarget__RequireAuthentication"
        value = "true"
      }
      env {
        name  = "Gemini__ProjectId"
        value = var.project_id
      }
      env {
        name  = "Gemini__Location"
        value = "global"
      }
      env {
        name  = "PubSub__ProjectId"
        value = var.project_id
      }
      env {
        name  = "PubSub__TopicId"
        value = google_pubsub_topic.work.name
      }
      env {
        name  = "PubSub__DeadLetterTopicId"
        value = google_pubsub_topic.dead_letter.name
      }
      env {
        name  = "PubSub__RequireAuthentication"
        value = "true"
      }
      env {
        name  = "OTEL_SERVICE_NAME"
        value = "racehunter-worker"
      }
      env {
        name  = "OTEL_EXPORTER_OTLP_ENDPOINT"
        value = "http://localhost:4317"
      }
      volume_mounts {
        name       = "cloudsql"
        mount_path = "/cloudsql"
      }
      startup_probe {
        http_get {
          path = "/healthz"
        }
      }
    }
    containers {
      name    = "otel-collector"
      image   = "us-docker.pkg.dev/cloud-ops-agents-artifacts/google-cloud-opentelemetry-collector/otelcol-google:0.156.0"
      command = ["/otelcol-google"]
      args    = ["--config=/etc/otelcol-google/config.yaml"]
      resources { limits = { cpu = "1", memory = "256Mi" } }
      volume_mounts {
        name       = "otel-config"
        mount_path = "/etc/otelcol-google"
      }
      startup_probe {
        http_get {
          path = "/"
          port = 13133
        }
      }
    }
  }

  depends_on = [
    google_secret_manager_secret_version.racehunter_database,
    google_secret_manager_secret_version.demo_control,
    google_secret_manager_secret_version.otel_collector_config,
    google_secret_manager_secret_iam_member.worker_database,
    google_secret_manager_secret_iam_member.worker_demo_control,
    google_secret_manager_secret_iam_member.otel_collector_config,
    google_project_iam_member.telemetry_writers
  ]
}

resource "google_cloud_run_v2_service" "reference_target" {
  name     = "racehunter-reference-target"
  location = var.region
  # Reachable through run.app, but invocation remains service-account scoped below.
  ingress             = "INGRESS_TRAFFIC_ALL"
  deletion_protection = var.deletion_protection

  template {
    service_account = google_service_account.target.email
    scaling { max_instance_count = var.reference_target_max_instance_count }
    max_instance_request_concurrency = 20
    volumes {
      name = "cloudsql"
      cloud_sql_instance { instances = [google_sql_database_instance.target.connection_name] }
    }
    volumes {
      name = "otel-config"
      secret {
        secret = google_secret_manager_secret.otel_collector_config.secret_id
        items {
          version = google_secret_manager_secret_version.otel_collector_config.version
          path    = "config.yaml"
        }
      }
    }
    containers {
      name  = "reference-target"
      image = var.reference_target_image
      resources { limits = { cpu = "1", memory = "512Mi" } }
      ports { container_port = 8080 }
      env {
        name = "ConnectionStrings__ReferenceTarget"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.target_database.secret_id
            version = google_secret_manager_secret_version.target_database.version
          }
        }
      }
      env {
        name = "DemoControl__Key"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.demo_control.secret_id
            version = google_secret_manager_secret_version.demo_control.version
          }
        }
      }
      env {
        name  = "OTEL_EXPORTER_OTLP_ENDPOINT"
        value = "http://localhost:4317"
      }
      volume_mounts {
        name       = "cloudsql"
        mount_path = "/cloudsql"
      }
      startup_probe {
        http_get {
          path = "/healthz"
        }
      }
    }
    containers {
      name    = "otel-collector"
      image   = "us-docker.pkg.dev/cloud-ops-agents-artifacts/google-cloud-opentelemetry-collector/otelcol-google:0.156.0"
      command = ["/otelcol-google"]
      args    = ["--config=/etc/otelcol-google/config.yaml"]
      resources { limits = { cpu = "1", memory = "256Mi" } }
      volume_mounts {
        name       = "otel-config"
        mount_path = "/etc/otelcol-google"
      }
      startup_probe {
        http_get {
          path = "/"
          port = 13133
        }
      }
    }
  }

  depends_on = [
    google_secret_manager_secret_version.target_database,
    google_secret_manager_secret_version.demo_control,
    google_secret_manager_secret_version.otel_collector_config,
    google_secret_manager_secret_iam_member.target_database,
    google_secret_manager_secret_iam_member.target_demo_control,
    google_secret_manager_secret_iam_member.otel_collector_config,
    google_project_iam_member.telemetry_writers
  ]
}

resource "google_cloud_run_v2_service_iam_member" "public_api" {
  project  = var.project_id
  location = var.region
  name     = google_cloud_run_v2_service.api.name
  role     = "roles/run.invoker"
  member   = "allUsers"
}

resource "google_cloud_run_v2_service_iam_member" "worker_push" {
  project  = var.project_id
  location = var.region
  name     = google_cloud_run_v2_service.worker.name
  role     = "roles/run.invoker"
  member   = "serviceAccount:${google_service_account.pubsub_push.email}"
}

resource "google_cloud_run_v2_service_iam_member" "api_worker" {
  project  = var.project_id
  location = var.region
  name     = google_cloud_run_v2_service.worker.name
  role     = "roles/run.invoker"
  member   = "serviceAccount:${google_service_account.api.email}"
}

resource "google_cloud_run_v2_service_iam_member" "worker_target" {
  project  = var.project_id
  location = var.region
  name     = google_cloud_run_v2_service.reference_target.name
  role     = "roles/run.invoker"
  member   = "serviceAccount:${google_service_account.worker.email}"
}

resource "google_pubsub_topic" "work" {
  name = "racehunter-work"
}

resource "google_pubsub_topic_iam_member" "api_publisher" {
  topic  = google_pubsub_topic.work.name
  role   = "roles/pubsub.publisher"
  member = "serviceAccount:${google_service_account.api.email}"
}

resource "google_pubsub_subscription" "worker" {
  name                 = "racehunter-worker"
  topic                = google_pubsub_topic.work.name
  ack_deadline_seconds = 120
  push_config {
    push_endpoint = "${google_cloud_run_v2_service.worker.uri}/internal/pubsub/push"
    oidc_token {
      service_account_email = google_service_account.pubsub_push.email
      audience              = google_cloud_run_v2_service.worker.uri
    }
  }
  retry_policy {
    minimum_backoff = "1s"
    maximum_backoff = "30s"
  }
  dead_letter_policy {
    dead_letter_topic     = google_pubsub_topic.dead_letter.id
    max_delivery_attempts = 5
  }
  depends_on = [
    google_cloud_run_v2_service_iam_member.worker_push,
    google_service_account_iam_member.pubsub_push_token_creator
  ]
}

resource "google_service_account_iam_member" "pubsub_push_token_creator" {
  service_account_id = google_service_account.pubsub_push.name
  role               = "roles/iam.serviceAccountTokenCreator"
  member             = "serviceAccount:service-${data.google_project.current.number}@gcp-sa-pubsub.iam.gserviceaccount.com"
}

resource "google_pubsub_topic" "dead_letter" {
  name = "racehunter-work-dead-letter"
}

resource "google_pubsub_topic_iam_member" "worker_dead_letter_publisher" {
  topic  = google_pubsub_topic.dead_letter.name
  role   = "roles/pubsub.publisher"
  member = "serviceAccount:${google_service_account.worker.email}"
}

resource "google_pubsub_topic_iam_member" "pubsub_service_dead_letter_publisher" {
  topic  = google_pubsub_topic.dead_letter.name
  role   = "roles/pubsub.publisher"
  member = "serviceAccount:service-${data.google_project.current.number}@gcp-sa-pubsub.iam.gserviceaccount.com"
}

resource "google_pubsub_subscription_iam_member" "pubsub_service_forward_subscriber" {
  subscription = google_pubsub_subscription.worker.name
  role         = "roles/pubsub.subscriber"
  member       = "serviceAccount:service-${data.google_project.current.number}@gcp-sa-pubsub.iam.gserviceaccount.com"
}

resource "google_billing_budget" "staging" {
  billing_account = var.billing_account_id
  display_name    = "RaceHunter staging monthly budget"
  amount {
    specified_amount {
      currency_code = "USD"
      units         = tostring(var.monthly_budget_usd)
    }
  }
  budget_filter { projects = ["projects/${data.google_project.current.number}"] }
  threshold_rules { threshold_percent = 0.5 }
  threshold_rules { threshold_percent = 0.9 }
  threshold_rules { threshold_percent = 1.0 }
}
