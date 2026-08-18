locals {
  services = toset([
    "aiplatform.googleapis.com",
    "artifactregistry.googleapis.com",
    "billingbudgets.googleapis.com",
    "cloudtrace.googleapis.com",
    "logging.googleapis.com",
    "pubsub.googleapis.com",
    "run.googleapis.com",
    "secretmanager.googleapis.com",
    "sqladmin.googleapis.com"
  ])
}

resource "google_project_service" "required" {
  for_each           = local.services
  project            = var.project_id
  service            = each.value
  disable_on_destroy = false
}

resource "google_artifact_registry_repository" "images" {
  location      = var.region
  repository_id = "racehunter"
  description   = "Immutable RaceHunter application images"
  format        = "DOCKER"
  depends_on    = [google_project_service.required]
}

resource "random_password" "database" {
  length  = 32
  special = false
}

resource "random_password" "demo_control" {
  length  = 48
  special = false
}

resource "google_sql_database_instance" "main" {
  name             = "racehunter-staging"
  region           = var.region
  database_version = "POSTGRES_17"
  deletion_protection = true

  settings {
    tier              = "db-f1-micro"
    availability_type = "ZONAL"
    disk_type         = "PD_SSD"
    disk_size         = 10
    disk_autoresize   = true
    backup_configuration {
      enabled                        = true
      point_in_time_recovery_enabled = true
    }
    ip_configuration {
      ipv4_enabled = false
    }
  }

  depends_on = [google_project_service.required]
}

resource "google_sql_database" "racehunter" {
  name     = "racehunter"
  instance = google_sql_database_instance.main.name
}

resource "google_sql_database" "reference_target" {
  name     = "racehunter_target"
  instance = google_sql_database_instance.main.name
}

resource "google_sql_user" "racehunter" {
  name     = "racehunter"
  instance = google_sql_database_instance.main.name
  password = random_password.database.result
}

resource "google_secret_manager_secret" "racehunter_database" {
  secret_id = "racehunter-database-connection"
  replication { auto {} }
  depends_on = [google_project_service.required]
}

resource "google_secret_manager_secret_version" "racehunter_database" {
  secret      = google_secret_manager_secret.racehunter_database.id
  secret_data = "Host=/cloudsql/${google_sql_database_instance.main.connection_name};Database=${google_sql_database.racehunter.name};Username=${google_sql_user.racehunter.name};Password=${random_password.database.result};SSL Mode=Disable"
}

resource "google_secret_manager_secret" "target_database" {
  secret_id = "racehunter-target-database-connection"
  replication { auto {} }
  depends_on = [google_project_service.required]
}

resource "google_secret_manager_secret_version" "target_database" {
  secret      = google_secret_manager_secret.target_database.id
  secret_data = "Host=/cloudsql/${google_sql_database_instance.main.connection_name};Database=${google_sql_database.reference_target.name};Username=${google_sql_user.racehunter.name};Password=${random_password.database.result};SSL Mode=Disable"
}

resource "google_secret_manager_secret" "demo_control" {
  secret_id = "racehunter-demo-control-key"
  replication { auto {} }
  depends_on = [google_project_service.required]
}

resource "google_secret_manager_secret_version" "demo_control" {
  secret      = google_secret_manager_secret.demo_control.id
  secret_data = random_password.demo_control.result
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

resource "google_cloud_run_v2_service" "api" {
  name                = "racehunter-api"
  location            = var.region
  ingress             = "INGRESS_TRAFFIC_ALL"
  deletion_protection = true

  template {
    service_account = google_service_account.api.email
    scaling { max_instance_count = 2 }
    volumes {
      name = "cloudsql"
      cloud_sql_instance { instances = [google_sql_database_instance.main.connection_name] }
    }
    containers {
      image = var.api_image
      resources { limits = { cpu = "1", memory = "512Mi" } }
      ports { container_port = 8080 }
      env {
        name = "ConnectionStrings__RaceHunter"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.racehunter_database.secret_id
            version = "latest"
          }
        }
      }
      volume_mounts { name = "cloudsql" mount_path = "/cloudsql" }
      startup_probe { http_get { path = "/healthz" } }
    }
  }

  depends_on = [google_project_service.required, google_secret_manager_secret_iam_member.api_database]
}

resource "google_cloud_run_v2_service" "worker" {
  name                = "racehunter-worker"
  location            = var.region
  ingress             = "INGRESS_TRAFFIC_INTERNAL_ONLY"
  deletion_protection = true

  template {
    service_account = google_service_account.worker.email
    scaling { max_instance_count = 2 }
    volumes {
      name = "cloudsql"
      cloud_sql_instance { instances = [google_sql_database_instance.main.connection_name] }
    }
    containers {
      image = var.worker_image
      resources { limits = { cpu = "1", memory = "1Gi" } }
      ports { container_port = 8080 }
      env {
        name = "ConnectionStrings__RaceHunter"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.racehunter_database.secret_id
            version = "latest"
          }
        }
      }
      env {
        name = "ReferenceTarget__DemoControlKey"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.demo_control.secret_id
            version = "latest"
          }
        }
      }
      volume_mounts { name = "cloudsql" mount_path = "/cloudsql" }
      startup_probe { http_get { path = "/healthz" } }
    }
  }

  depends_on = [
    google_project_service.required,
    google_secret_manager_secret_iam_member.worker_database,
    google_secret_manager_secret_iam_member.worker_demo_control
  ]
}

resource "google_cloud_run_v2_service" "reference_target" {
  name                = "racehunter-reference-target"
  location            = var.region
  ingress             = "INGRESS_TRAFFIC_INTERNAL_ONLY"
  deletion_protection = true

  template {
    service_account = google_service_account.target.email
    scaling { max_instance_count = 2 }
    volumes {
      name = "cloudsql"
      cloud_sql_instance { instances = [google_sql_database_instance.main.connection_name] }
    }
    containers {
      image = var.reference_target_image
      resources { limits = { cpu = "1", memory = "512Mi" } }
      ports { container_port = 8080 }
      env {
        name = "ConnectionStrings__ReferenceTarget"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.target_database.secret_id
            version = "latest"
          }
        }
      }
      env {
        name = "DemoControl__Key"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.demo_control.secret_id
            version = "latest"
          }
        }
      }
      volume_mounts { name = "cloudsql" mount_path = "/cloudsql" }
      startup_probe { http_get { path = "/healthz" } }
    }
  }

  depends_on = [
    google_project_service.required,
    google_secret_manager_secret_iam_member.target_database,
    google_secret_manager_secret_iam_member.target_demo_control
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

resource "google_cloud_run_v2_service_iam_member" "worker_target" {
  project  = var.project_id
  location = var.region
  name     = google_cloud_run_v2_service.reference_target.name
  role     = "roles/run.invoker"
  member   = "serviceAccount:${google_service_account.worker.email}"
}

resource "google_pubsub_topic" "work" {
  name       = "racehunter-work"
  depends_on = [google_project_service.required]
}

resource "google_pubsub_topic_iam_member" "api_publisher" {
  topic  = google_pubsub_topic.work.name
  role   = "roles/pubsub.publisher"
  member = "serviceAccount:${google_service_account.api.email}"
}

resource "google_pubsub_subscription" "worker" {
  name  = "racehunter-worker"
  topic = google_pubsub_topic.work.name
  ack_deadline_seconds = 120
  push_config {
    push_endpoint = "${google_cloud_run_v2_service.worker.uri}/pubsub"
    oidc_token { service_account_email = google_service_account.pubsub_push.email }
  }
  dead_letter_policy {
    dead_letter_topic     = google_pubsub_topic.dead_letter.id
    max_delivery_attempts = 5
  }
}

resource "google_pubsub_topic" "dead_letter" {
  name       = "racehunter-work-dead-letter"
  depends_on = [google_project_service.required]
}

resource "google_billing_budget" "staging" {
  count           = var.billing_account_id == null ? 0 : 1
  billing_account = var.billing_account_id
  display_name    = "RaceHunter staging monthly budget"
  amount { specified_amount { currency_code = "USD" units = tostring(var.monthly_budget_usd) } }
  budget_filter { projects = ["projects/${data.google_project.current.number}"] }
  threshold_rules { threshold_percent = 0.5 }
  threshold_rules { threshold_percent = 0.9 }
  threshold_rules { threshold_percent = 1.0 }
  depends_on = [google_project_service.required]
}
