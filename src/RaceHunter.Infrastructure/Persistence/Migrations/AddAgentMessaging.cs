using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RaceHunter.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RaceHunterDbContext))]
[Migration("202608180003_AddAgentMessaging")]
internal sealed class AddAgentMessaging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "experiments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                objective = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                max_actors = table.Column<int>(type: "integer", nullable: false),
                max_concurrent_actors = table.Column<int>(type: "integer", nullable: false),
                max_requests = table.Column<int>(type: "integer", nullable: false),
                max_model_calls = table.Column<int>(type: "integer", nullable: false),
                max_duration_ms = table.Column<long>(type: "bigint", nullable: false),
                max_retries = table.Column<int>(type: "integer", nullable: false),
                plan_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                plan_json = table.Column<string>(type: "jsonb", nullable: true),
                approved_plan_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                approval_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                run_id = table.Column<Guid>(type: "uuid", nullable: true),
                failure_outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                failure_diagnostic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_experiments", value => value.id));
        migrationBuilder.CreateIndex("IX_experiments_approval_key", "experiments", "approval_key", unique: true);
        migrationBuilder.CreateIndex("IX_experiments_run_id", "experiments", "run_id", unique: true);

        migrationBuilder.CreateTable(
            name: "hunt_events",
            columns: table => new
            {
                hunt_id = table.Column<Guid>(type: "uuid", nullable: false),
                cursor = table.Column<long>(type: "bigint", nullable: false),
                kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_hunt_events", value => new { value.hunt_id, value.cursor });
                table.ForeignKey("FK_hunt_events_experiments_hunt_id", value => value.hunt_id, "experiments", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                work_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                correlation_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                work_created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                publish_attempts = table.Column<int>(type: "integer", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_outbox_messages", value => value.id));
        migrationBuilder.CreateIndex("IX_outbox_messages_published_at_utc", "outbox_messages", "published_at_utc");
        migrationBuilder.CreateIndex("IX_outbox_messages_work_id", "outbox_messages", "work_id", unique: true);

        migrationBuilder.CreateTable(
            name: "work_inbox",
            columns: table => new
            {
                work_id = table.Column<Guid>(type: "uuid", nullable: false),
                message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                delivery_attempt = table.Column<int>(type: "integer", nullable: false),
                lease_owner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                lease_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                checkpoint_boundary = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                checkpoint_iteration = table.Column<int>(type: "integer", nullable: true),
                checkpoint_state_json = table.Column<string>(type: "jsonb", nullable: true),
                checkpoint_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                failure_category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                failure_diagnostic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_work_inbox", value => value.work_id));
        migrationBuilder.CreateIndex("IX_work_inbox_lease_expires_at_utc", "work_inbox", "lease_expires_at_utc");
        migrationBuilder.CreateIndex("IX_work_inbox_message_id", "work_inbox", "message_id", unique: true);

        migrationBuilder.CreateTable(
            name: "dead_letters",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                work_id = table.Column<Guid>(type: "uuid", nullable: false),
                category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                diagnostic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_dead_letters", value => value.id));
        migrationBuilder.CreateIndex("IX_dead_letters_work_id", "dead_letters", "work_id", unique: true);

        migrationBuilder.CreateTable(
            name: "agent_iterations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                run_id = table.Column<Guid>(type: "uuid", nullable: false),
                iteration = table.Column<int>(type: "integer", nullable: false),
                evidence_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                rationale_summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                model_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                schema_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                model_invocation_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_iterations", value => value.id);
                table.ForeignKey("FK_agent_iterations_experiment_runs_run_id", value => value.run_id, "experiment_runs", "id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_agent_iterations_run_id_iteration", "agent_iterations", new[] { "run_id", "iteration" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("agent_iterations");
        migrationBuilder.DropTable("dead_letters");
        migrationBuilder.DropTable("hunt_events");
        migrationBuilder.DropTable("experiments");
        migrationBuilder.DropTable("outbox_messages");
        migrationBuilder.DropTable("work_inbox");
    }
}
