using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class AuditLog
{
    public long Id { get; set; }

    public long? ActorId { get; set; }

    public string Action { get; set; } = null!;

    public string EntityName { get; set; } = null!;

    public string? EntityKey { get; set; }

    public string? Summary { get; set; }

    public Guid? CorrelationId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public virtual User? Actor { get; set; }
}
