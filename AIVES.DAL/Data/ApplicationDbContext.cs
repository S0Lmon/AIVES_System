using AIVES.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace AIVES.DAL.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IDataProtectionKeyContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<BloomLevel> BloomLevels
        {
            get; set;
        }
        public DbSet<Rubric> Rubrics
        {
            get; set;
        }
        public DbSet<RubricCriterion> RubricCriteria
        {
            get; set;
        }
        public DbSet<RubricLevel> RubricLevels
        {
            get; set;
        }
        public DbSet<RubricCriterionLevel> RubricCriterionLevels
        {
            get; set;
        }
        public DbSet<Question> Questions
        {
            get; set;
        }
        public DbSet<Subject> Subjects
        {
            get; set;
        }
        public DbSet<Topic> Topics
        {
            get; set;
        }
        public DbSet<Material> Materials
        {
            get; set;
        }
        public DbSet<EmailVerificationCode> EmailVerificationCodes
        {
            get; set;
        }
        public DbSet<Exam> Exams
        {
            get; set;
        }
        public DbSet<ExamCandidate> ExamCandidates
        {
            get; set;
        }
        public DbSet<ExamCandidateQuestion> ExamCandidateQuestions
        {
            get; set;
        }
        public DbSet<ExamAttempt> ExamAttempts
        {
            get; set;
        }
        public DbSet<ExamTurn> ExamTurns
        {
            get; set;
        }
        public DbSet<QuestionGrade> QuestionGrades
        {
            get; set;
        }
        public DbSet<TurnRecording> TurnRecordings
        {
            get; set;
        }
        public DbSet<AuditEntry> AuditEntries
        {
            get; set;
        }
        public DbSet<SubjectLecturer> SubjectLecturers
        {
            get; set;
        }
        public DbSet<GlossaryTerm> GlossaryTerms
        {
            get; set;
        }
        public DbSet<SystemSetting> SystemSettings
        {
            get; set;
        }
        // Shared cookie/antiforgery keys so every web instance can read what another issued.
        public DbSet<DataProtectionKey> DataProtectionKeys
        {
            get; set;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(e => e.DisplayName).HasMaxLength(120);
            });

            modelBuilder.Entity<EmailVerificationCode>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.CodeHash).IsRequired().HasMaxLength(64);
                entity.Property(e => e.Salt).IsRequired().HasMaxLength(32);
                entity.HasIndex(e => new { e.UserId, e.IsConsumed, e.ExpiresAtUtc });
                entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<BloomLevel>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.HasIndex(e => e.Order);
            });
            modelBuilder.Entity<Rubric>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(1000);
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.ModifiedDate).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasMany(e => e.Criteria)
                    .WithOne(e => e.Rubric)
                    .HasForeignKey(e => e.RubricId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.Levels)
                    .WithOne(e => e.Rubric)
                    .HasForeignKey(e => e.RubricId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<RubricCriterion>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Criterion).IsRequired().HasMaxLength(300);
                entity.Property(e => e.Description).HasMaxLength(1000);
                entity.HasIndex(e => e.RubricId);
            });

            modelBuilder.Entity<RubricLevel>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(120);
                entity.Property(e => e.Description).HasMaxLength(1000);
                entity.HasIndex(e => e.RubricId);
                entity.HasMany(e => e.Cells)
                    .WithOne(e => e.RubricLevel)
                    .HasForeignKey(e => e.RubricLevelId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<RubricCriterionLevel>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Descriptor).HasMaxLength(1000);
                entity.HasIndex(e => e.RubricCriterionId);
                entity.HasIndex(e => e.RubricLevelId);
                entity.HasOne(e => e.RubricCriterion)
                    .WithMany(e => e.Levels)
                    .HasForeignKey(e => e.RubricCriterionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<Question>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Content).IsRequired().HasColumnType("nvarchar(max)");
                entity.Property(e => e.Context).HasMaxLength(300);
                entity.Property(e => e.ExpectedAnswer).HasColumnType("nvarchar(max)");
                entity.Property(e => e.CreatedDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.ModifiedDate).HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasOne(e => e.BloomLevel)
                    .WithMany(e => e.Questions)
                    .HasForeignKey(e => e.BloomLevelId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Rubric)
                    .WithMany(e => e.Questions)
                    .HasForeignKey(e => e.RubricId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Topic)
                    .WithMany()
                    .HasForeignKey(e => e.TopicId)
                    .OnDelete(DeleteBehavior.SetNull);

                // SQL Server allows only one cascading path into a table, and Subjects already
                // reaches Questions through Topics. So the direct link is NO ACTION and
                // SubjectRepository clears it before deleting.
                entity.HasOne(e => e.Subject)
                    .WithMany()
                    .HasForeignKey(e => e.SubjectId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasIndex(e => e.BloomLevelId);
                entity.HasIndex(e => e.RubricId);
                entity.HasIndex(e => e.TopicId);
                entity.HasIndex(e => e.SubjectId);
                entity.HasIndex(e => e.IsActive);
            });
            SeedBloomLevels(modelBuilder);
            ConfigureCatalog(modelBuilder);
            ConfigureGradingAndOperations(modelBuilder);
        }

        private static void ConfigureGradingAndOperations(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<QuestionGrade>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.ExamCandidateQuestionId).IsUnique();
                entity.Property(e => e.MaxScore).HasPrecision(6, 2);
                entity.Property(e => e.AiScore).HasPrecision(6, 2);
                entity.Property(e => e.LecturerScore).HasPrecision(6, 2);
                entity.Property(e => e.AiModel).HasMaxLength(100);
                entity.Property(e => e.LecturerComment).HasMaxLength(2000);
                entity.Property(e => e.UpdatedById).HasMaxLength(450);
                entity.HasOne(e => e.Question)
                    .WithOne(e => e.Grade)
                    .HasForeignKey<QuestionGrade>(e => e.ExamCandidateQuestionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<TurnRecording>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.ExamTurnId).IsUnique();
                entity.HasIndex(e => e.CreatedAtUtc);
                entity.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
                entity.Property(e => e.StoragePath).IsRequired().HasMaxLength(400);
                entity.HasOne(e => e.Turn)
                    .WithOne(e => e.Recording)
                    .HasForeignKey<TurnRecording>(e => e.ExamTurnId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<AuditEntry>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Action).IsRequired().HasMaxLength(64);
                entity.Property(e => e.ActorId).HasMaxLength(450);
                entity.Property(e => e.ActorEmail).HasMaxLength(256);
                entity.Property(e => e.Details).HasMaxLength(4000);
                entity.HasIndex(e => e.AtUtc);
                entity.HasIndex(e => new { e.ExamId, e.AtUtc });
                entity.HasIndex(e => e.Action);
            });
            modelBuilder.Entity<SubjectLecturer>(entity =>
            {
                entity.HasKey(e => new { e.SubjectId, e.UserId });
                entity.Property(e => e.UserId).HasMaxLength(450);
                entity.HasIndex(e => e.UserId);
                entity.HasOne(e => e.Subject).WithMany().HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<GlossaryTerm>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Term).IsRequired().HasMaxLength(100);
                entity.Property(e => e.SpokenForms).HasMaxLength(1000);
                entity.HasIndex(e => new { e.SubjectId, e.Term }).IsUnique();
                entity.HasOne(e => e.Subject).WithMany().HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<SystemSetting>(entity =>
            {
                entity.HasKey(e => e.Key);
                entity.Property(e => e.Key).HasMaxLength(100);
                entity.Property(e => e.Value).IsRequired().HasMaxLength(2000);
            });
        }

        private static void ConfigureCatalog(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Subject>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(1000);
                entity.HasIndex(e => e.Name).IsUnique();

                entity.HasMany(e => e.Topics)
                    .WithOne(e => e.Subject)
                    .HasForeignKey(e => e.SubjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<Topic>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(1000);
                entity.HasIndex(e => new { e.SubjectId, e.Name }).IsUnique();

                entity.HasMany(e => e.Materials)
                    .WithOne(e => e.Topic)
                    .HasForeignKey(e => e.TopicId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<Material>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(300);
                entity.Property(e => e.Content).IsRequired().HasColumnType("nvarchar(max)");
                entity.Property(e => e.SourceFileName).HasMaxLength(260);
                entity.HasIndex(e => e.TopicId);
                entity.HasIndex(e => e.IsActive);
            });
        }

        private void SeedBloomLevels(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Exam>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.SubjectName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.TopicName).HasMaxLength(200);
                entity.Property(e => e.CreatedById).IsRequired().HasMaxLength(450);
                entity.Property(e => e.Language).IsRequired().HasMaxLength(10).HasDefaultValue("vi-VN");
                entity.Property(e => e.AnswerTimeLimitSeconds).HasDefaultValue(120);
                entity.Property(e => e.MaxFollowUpsPerQuestion).HasDefaultValue(2);
                entity.HasIndex(e => e.CreatedById);
                entity.HasIndex(e => e.StartsAtUtc);
                // NO ACTION like Question -> Subject: Subject -> Topic -> Exam would be a second cascade
                // path. SubjectRepository.DeleteAsync clears SubjectId; the stored name stays.
                entity.HasOne(e => e.Subject)
                    .WithMany()
                    .HasForeignKey(e => e.SubjectId)
                    .OnDelete(DeleteBehavior.NoAction);
                entity.HasOne(e => e.Topic)
                    .WithMany()
                    .HasForeignKey(e => e.TopicId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
            modelBuilder.Entity<ExamCandidate>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
                entity.HasIndex(e => new { e.ExamId, e.Email }).IsUnique();
                entity.HasIndex(e => new { e.ExamId, e.Order }).IsUnique();
                entity.HasIndex(e => e.Email);
                entity.HasOne(e => e.Exam)
                    .WithMany(e => e.Candidates)
                    .HasForeignKey(e => e.ExamId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<ExamCandidateQuestion>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Content).IsRequired();
                entity.Property(e => e.BloomLevelName).HasMaxLength(100);
                entity.HasIndex(e => new { e.ExamCandidateId, e.Order }).IsUnique();
                entity.HasOne(e => e.Candidate)
                    .WithMany(e => e.Questions)
                    .HasForeignKey(e => e.ExamCandidateId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Question)
                    .WithMany()
                    .HasForeignKey(e => e.QuestionId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
            modelBuilder.Entity<ExamAttempt>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.ExamCandidateId).IsUnique();
                entity.HasIndex(e => new { e.Status, e.GradingStatus });
                entity.Property(e => e.GradingClaimedAtUtc).IsConcurrencyToken();
                entity.Property(e => e.FinalScore).HasPrecision(5, 2);
                entity.Property(e => e.FinalizedById).HasMaxLength(450);
                entity.Property(e => e.GradingError).HasMaxLength(1000);
                entity.Property(e => e.LecturerComment).HasMaxLength(2000);
                entity.HasOne(e => e.Candidate)
                    .WithOne(e => e.Attempt)
                    .HasForeignKey<ExamAttempt>(e => e.ExamCandidateId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<ExamTurn>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.QuestionText).IsRequired();
                entity.Property(e => e.AnsweredAtUtc).IsConcurrencyToken();
                entity.HasIndex(e => new { e.ExamAttemptId, e.Order }).IsUnique();
                entity.HasOne(e => e.Attempt)
                    .WithMany(e => e.Turns)
                    .HasForeignKey(e => e.ExamAttemptId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<BloomLevel>().HasData(
                new BloomLevel { Id = 1, Name = "Remember", Order = 1, Description = "Recall facts and basic concepts" },
                new BloomLevel { Id = 2, Name = "Understand", Order = 2, Description = "Explain ideas or concepts" },
                new BloomLevel { Id = 3, Name = "Apply", Order = 3, Description = "Use information in new situations" },
                new BloomLevel { Id = 4, Name = "Analyze", Order = 4, Description = "Draw connections among ideas" },
                new BloomLevel { Id = 5, Name = "Evaluate", Order = 5, Description = "Justify a stand or decision" },
                new BloomLevel { Id = 6, Name = "Create", Order = 6, Description = "Produce new or original work" }
            );
        }
    }
}
