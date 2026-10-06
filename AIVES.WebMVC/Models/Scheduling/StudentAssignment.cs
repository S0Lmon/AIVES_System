using System;

namespace AIVES.WebMVC.Models.Scheduling
{
    public class StudentAssignment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string StudentId { get; set; } = string.Empty;
        public Guid VivaSessionId { get; set; }
        public Guid TimeSlotId { get; set; }
        public string Status { get; set; } = "Not Scheduled"; // matches status list in plan
    }
}
