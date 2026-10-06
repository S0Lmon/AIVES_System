using System;

namespace AIVES.WebMVC.Models.Scheduling
{
    public class TimeSlot
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid VivaSessionId { get; set; }
        public string StudentId { get; set; } = string.Empty;
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public bool IsBreak { get; set; }
    }
}
