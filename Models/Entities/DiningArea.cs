using System;
using System.Collections.Generic;

namespace DoraFood.Models;

public partial class DiningArea
{
    public long Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public bool IsActive { get; set; }

    public virtual ICollection<DiningTable> DiningTables { get; set; } = new List<DiningTable>();
}
