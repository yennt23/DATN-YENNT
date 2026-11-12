using System;

namespace DATN.Entities;

public partial class Application
{
    public DateTime? LastViewedAt { get; set; }
    public Guid? LastViewedBy { get; set; }
}
