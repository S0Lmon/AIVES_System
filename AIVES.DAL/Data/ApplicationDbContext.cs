using AIVES.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace AIVES.DAL.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
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
            });
            modelBuilder.Entity<RubricCriterion>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Criterion).IsRequired().HasMaxLength(300);
                entity.Property(e => e.Description).HasMaxLength(1000);
                entity.HasIndex(e => e.RubricId);
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
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Topic)
                    .WithMany()
                    .HasForeignKey(e => e.TopicId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.BloomLevelId);
                entity.HasIndex(e => e.RubricId);
                entity.HasIndex(e => e.TopicId);
                entity.HasIndex(e => e.IsActive);
            });
            SeedBloomLevels(modelBuilder);
            ConfigureCatalog(modelBuilder);
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
