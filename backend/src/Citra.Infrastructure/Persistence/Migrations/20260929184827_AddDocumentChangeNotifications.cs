using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Citra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Live updates: every change of a document the documents page shows (added, deleted, status, error, counts,
    /// name, finish time) sends a PostgreSQL notification on the "document_changes" channel, which the API listens
    /// to and forwards to the browsers (DocumentsHub). A trigger, so every writer is covered (Worker, API, a manual
    /// fix); and PostgreSQL delivers notifications only when the change is committed, never for a rolled-back one.
    /// Payload: {"id", "tenant_id", "bot_id", "deleted"} — the listener reads the current state itself.
    /// </summary>
    public partial class AddDocumentChangeNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION notify_document_change() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    changed documents;
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        changed := OLD;
                    ELSE
                        changed := NEW;
                    END IF;

                    PERFORM pg_notify('document_changes', json_build_object(
                        'id', changed.id,
                        'tenant_id', changed.tenant_id,
                        'bot_id', changed.bot_id,
                        'deleted', TG_OP = 'DELETE')::text);
                    RETURN NULL; -- AFTER trigger: the result is ignored
                END $$;

                CREATE TRIGGER documents_notify_insert_delete
                    AFTER INSERT OR DELETE ON documents
                    FOR EACH ROW EXECUTE FUNCTION notify_document_change();

                -- Only changes the page shows: not e.g. updated_at alone.
                CREATE TRIGGER documents_notify_update
                    AFTER UPDATE ON documents
                    FOR EACH ROW
                    WHEN (OLD.status IS DISTINCT FROM NEW.status
                       OR OLD.error_message IS DISTINCT FROM NEW.error_message
                       OR OLD.page_count IS DISTINCT FROM NEW.page_count
                       OR OLD.chunk_count IS DISTINCT FROM NEW.chunk_count
                       OR OLD.file_name IS DISTINCT FROM NEW.file_name
                       OR OLD.processed_at IS DISTINCT FROM NEW.processed_at)
                    EXECUTE FUNCTION notify_document_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER documents_notify_update ON documents;
                DROP TRIGGER documents_notify_insert_delete ON documents;
                DROP FUNCTION notify_document_change();
                """);
        }
    }
}
