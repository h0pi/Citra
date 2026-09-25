namespace Citra.Domain.Common;

/// <summary>
/// Base class for all entities. Ids are UUID v7 (time-ordered), which keeps database indexes
/// compact and fast compared to random GUIDs. Timestamps are set by the persistence layer.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
