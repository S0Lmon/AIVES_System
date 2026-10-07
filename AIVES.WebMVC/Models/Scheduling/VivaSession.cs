using System;
using System.Collections.Generic;

namespace AIVES.WebMVC.Models.Scheduling
{
    public class VivaSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Course { get; set; } = string.Empty;
        public string Semester { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int DurationPerStudentMinutes { get; set; }
        public int PrimaryQuestionsPerStudent { get; set; }
        public int MaxFollowUpQuestionsPerStudent { get; set; }
        public string QuestionSelectionStrategy { get; set; } = string.Empty;
        public string GradingRubric { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
        public List<TimeSlot> TimeSlots { get; set; } = new();
        public List<string> ParticipatingStudentIds { get; set; } = new();
    }
}
