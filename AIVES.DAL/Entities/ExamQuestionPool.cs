namespace AIVES.DAL.Entities;

/// <summary>A question selected by the lecturer as part of an exam's assignment pool.</summary>
public class ExamQuestionPool
{
    public int ExamId { get; set; }
    public int QuestionId { get; set; }
    public virtual Exam Exam { get; set; } = null!;
    public virtual Question Question { get; set; } = null!;
}
