using ApplyWise.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ResumeAnalysis> ResumeAnalyses => Set<ResumeAnalysis>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<JobScamCheck> JobScamChecks => Set<JobScamCheck>();
    public DbSet<CareerProfile> CareerProfiles => Set<CareerProfile>();
    public DbSet<AccountSecurityCode> AccountSecurityCodes => Set<AccountSecurityCode>();
    public DbSet<ResumeFileCleanup> ResumeFileCleanups => Set<ResumeFileCleanup>();
    public DbSet<GmailConnection> GmailConnections => Set<GmailConnection>();
    public DbSet<GmailOAuthFlow> GmailOAuthFlows => Set<GmailOAuthFlow>();
    public DbSet<ApplicationImport> ApplicationImports => Set<ApplicationImport>();
    public DbSet<UserAccountActivity> UserAccountActivities => Set<UserAccountActivity>();
    public DbSet<ProductEvent> ProductEvents => Set<ProductEvent>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Resume>(entity =>
        {
            entity.Property(resume => resume.VersionName).HasMaxLength(100);
            entity.Property(resume => resume.OriginalFileName).HasMaxLength(255);
            entity.Property(resume => resume.StoredFileName).HasMaxLength(100);
            entity.Property(resume => resume.FilePath).HasMaxLength(500);
            entity.Property(resume => resume.ContentType).HasMaxLength(100);
            entity.Property(resume => resume.Notes).HasMaxLength(1000);

            entity.HasIndex(resume => new { resume.UserId, resume.UploadedAt });
            entity.HasIndex(resume => resume.UserId)
                .IsUnique()
                .HasFilter("[IsDefault] = 1");

            entity.HasOne(resume => resume.User)
                .WithMany()
                .HasForeignKey(resume => resume.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<JobApplication>(entity =>
        {
            entity.Property(application => application.CompanyName).HasMaxLength(150);
            entity.Property(application => application.JobTitle).HasMaxLength(150);
            entity.Property(application => application.JobLocation).HasMaxLength(150);
            entity.Property(application => application.SalaryRange).HasMaxLength(100);
            entity.Property(application => application.JobUrl).HasMaxLength(2048);
            entity.Property(application => application.JobDescription).HasMaxLength(8000);
            entity.Property(application => application.Notes).HasMaxLength(2000);
            entity.Property(application => application.CustomFieldsJson).HasColumnType("nvarchar(max)");

            entity.HasIndex(application => new { application.UserId, application.CreatedAt });
            entity.HasIndex(application => new { application.UserId, application.UpdatedAt });
            entity.HasIndex(application => new { application.UserId, application.Deadline });
            entity.HasIndex(application => new { application.UserId, application.Status });
            entity.HasIndex(application => new { application.UserId, application.Source });
            entity.HasIndex(application => new { application.UserId, application.AppliedDate });

            entity.HasOne(application => application.User)
                .WithMany()
                .HasForeignKey(application => application.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(application => application.Resume)
                .WithMany(resume => resume.JobApplications)
                .HasForeignKey(application => application.ResumeId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<ResumeAnalysis>(entity =>
        {
            entity.Property(analysis => analysis.AnalysisType)
                .HasConversion<string>()
                .HasMaxLength(30);
            entity.Property(analysis => analysis.MatchedKeywordsJson).HasColumnType("nvarchar(max)");
            entity.Property(analysis => analysis.MissingKeywordsJson).HasColumnType("nvarchar(max)");
            entity.Property(analysis => analysis.SuggestionsJson).HasColumnType("nvarchar(max)");
            entity.Property(analysis => analysis.ScoreVersion).HasMaxLength(30);
            entity.Property(analysis => analysis.InputHash).HasMaxLength(64).IsFixedLength();
            entity.Property(analysis => analysis.ScoreBreakdownJson).HasColumnType("nvarchar(max)");
            entity.Property(analysis => analysis.EvidenceJson).HasColumnType("nvarchar(max)");
            entity.Property(analysis => analysis.WarningsJson).HasColumnType("nvarchar(max)");
            entity.Property(analysis => analysis.ReviewJson).HasColumnType("nvarchar(max)");
            entity.Property(analysis => analysis.ResumeTextSnapshot).HasColumnType("nvarchar(max)");
            entity.Property(analysis => analysis.JobDescriptionSnapshot).HasColumnType("nvarchar(max)");

            entity.HasIndex(analysis => new { analysis.UserId, analysis.CreatedAt });
            entity.HasIndex(analysis => new { analysis.UserId, analysis.ScoreVersion, analysis.CreatedAt });
            entity.HasIndex(analysis => new { analysis.UserId, analysis.ResumeId });
            entity.HasIndex(analysis => new { analysis.UserId, analysis.JobApplicationId });
            entity.HasIndex(analysis => new
            {
                analysis.UserId,
                analysis.ResumeId,
                analysis.JobApplicationId,
                analysis.AnalysisType,
                analysis.InputHash,
                analysis.ScoreVersion
            })
                .IsUnique()
                .HasDatabaseName("UX_ResumeAnalyses_CurrentInput")
                .HasFilter("[InputHash] IS NOT NULL AND [ScoreVersion] IS NOT NULL");

            entity.HasOne(analysis => analysis.User)
                .WithMany()
                .HasForeignKey(analysis => analysis.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(analysis => analysis.Resume)
                .WithMany(resume => resume.Analyses)
                .HasForeignKey(analysis => analysis.ResumeId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(analysis => analysis.JobApplication)
                .WithMany(application => application.Analyses)
                .HasForeignKey(analysis => analysis.JobApplicationId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<Interview>(entity =>
        {
            entity.Property(interview => interview.MeetingLink).HasMaxLength(2048);
            entity.Property(interview => interview.InterviewerName).HasMaxLength(150);
            entity.Property(interview => interview.PreparationNotes).HasMaxLength(4000);
            entity.Property(interview => interview.FeedbackNotes).HasMaxLength(4000);
            entity.Property(interview => interview.ResultNotes).HasMaxLength(2000);

            entity.HasIndex(interview => new { interview.UserId, interview.ScheduledAt });
            entity.HasIndex(interview => new { interview.UserId, interview.Status });

            entity.HasOne(interview => interview.User)
                .WithMany()
                .HasForeignKey(interview => interview.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(interview => interview.JobApplication)
                .WithMany(application => application.Interviews)
                .HasForeignKey(interview => interview.JobApplicationId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<JobScamCheck>(entity =>
        {
            entity.Property(check => check.RedFlagsJson).HasColumnType("nvarchar(max)");
            entity.Property(check => check.MissingInformationJson).HasColumnType("nvarchar(max)");
            entity.Property(check => check.Recommendation).HasMaxLength(1000);

            entity.HasIndex(check => new { check.UserId, check.CreatedAt });
            entity.HasIndex(check => new { check.UserId, check.JobApplicationId });

            entity.HasOne(check => check.User)
                .WithMany()
                .HasForeignKey(check => check.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(check => check.JobApplication)
                .WithMany(application => application.ScamChecks)
                .HasForeignKey(check => check.JobApplicationId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<CareerProfile>(entity =>
        {
            entity.HasKey(profile => profile.UserId);
            entity.Property(profile => profile.FullName).HasColumnName("DisplayName");
            entity.Property(profile => profile.CareerStage).HasConversion<string>().HasMaxLength(40);
            entity.Property(profile => profile.CurrentSemester).HasMaxLength(80);
            entity.Property(profile => profile.PreferredLocations).HasColumnName("PreferredJobLocations");
            entity.Property(profile => profile.FullName).HasMaxLength(100);
            entity.Property(profile => profile.Gender).HasConversion<string>().HasMaxLength(30);
            entity.Property(profile => profile.DateOfBirth).HasColumnType("date");
            entity.Property(profile => profile.Institution).HasMaxLength(180);
            entity.Property(profile => profile.DegreeProgram).HasMaxLength(150);
            entity.Property(profile => profile.FieldOfStudy).HasMaxLength(150);
            entity.Property(profile => profile.PreferredLocations).HasMaxLength(500);
            entity.Property(profile => profile.PreferredWorkModes).HasMaxLength(100);
            entity.Property(profile => profile.Skills).HasMaxLength(2000);
            entity.Property(profile => profile.CareerInterests).HasMaxLength(1000);
            entity.Property(profile => profile.AcademicHighlights).HasMaxLength(2000);
            entity.Property(profile => profile.AcademicHighlights).HasColumnName("AcademicProjects");
            entity.Property(profile => profile.AvatarContentType).HasMaxLength(50);
            entity.Property(profile => profile.SelectedAvatarId).HasMaxLength(80);
            entity.HasOne(profile => profile.User).WithOne().HasForeignKey<CareerProfile>(profile => profile.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });


        builder.Entity<AccountSecurityCode>(entity =>
        {
            entity.HasIndex(code => new { code.UserId, code.Action, code.CreatedAt });
            entity.Property(code => code.Action).HasConversion<string>().HasMaxLength(30);
            entity.Property(code => code.ProtectedCode).HasColumnType("varbinary(512)");
            entity.HasOne(code => code.User).WithMany().HasForeignKey(code => code.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserAccountActivity>(entity =>
        {
            entity.HasKey(activity => activity.UserId);
            entity.Property(activity => activity.LastLoginProvider).HasMaxLength(30);
            entity.HasIndex(activity => activity.RegisteredAt);
            entity.HasIndex(activity => activity.LastActivityAt);
            entity.HasOne(activity => activity.User)
                .WithOne()
                .HasForeignKey<UserAccountActivity>(activity => activity.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProductEvent>(entity =>
        {
            entity.Property(productEvent => productEvent.Name).HasMaxLength(64);
            entity.Property(productEvent => productEvent.Source).HasMaxLength(32);
            entity.HasIndex(productEvent => productEvent.OccurredAt);
            entity.HasIndex(productEvent => new
            {
                productEvent.UserId,
                productEvent.OccurredAt
            });
            entity.HasIndex(productEvent => new
            {
                productEvent.Name,
                productEvent.OccurredAt
            });
            entity.HasOne(productEvent => productEvent.User)
                .WithMany()
                .HasForeignKey(productEvent => productEvent.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ContactMessage>(entity =>
        {
            entity.Property(message => message.FullName).HasMaxLength(100);
            entity.Property(message => message.Email).HasMaxLength(320);
            entity.Property(message => message.Topic).HasConversion<string>().HasMaxLength(40);
            entity.Property(message => message.Subject).HasMaxLength(160);
            entity.Property(message => message.Body).HasMaxLength(4000);
            entity.HasIndex(message => message.CreatedAt);
            entity.HasIndex(message => new { message.ReadAt, message.CreatedAt });
            entity.HasOne(message => message.User)
                .WithMany()
                .HasForeignKey(message => message.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ResumeFileCleanup>(entity =>
        {
            entity.Property(cleanup => cleanup.FilePath).HasMaxLength(500);
            entity.Property(cleanup => cleanup.LastErrorType).HasMaxLength(200);
            entity.HasIndex(cleanup => cleanup.FilePath).IsUnique();
            entity.HasIndex(cleanup => cleanup.NextAttemptAt);
        });

        builder.Entity<GmailConnection>(entity =>
        {
            entity.Property(connection => connection.EmailAddress).HasMaxLength(320);
            entity.Property(connection => connection.ProtectedRefreshToken).HasColumnType("nvarchar(max)");
            entity.Property(connection => connection.LastErrorCode).HasMaxLength(100);
            entity.Property(connection => connection.AutoAddHighConfidenceApplications)
                .HasDefaultValue(false);
            entity.HasIndex(connection => connection.UserId).IsUnique();
            entity.HasIndex(connection => connection.NextSyncAt);
            entity.HasOne(connection => connection.User)
                .WithMany()
                .HasForeignKey(connection => connection.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GmailOAuthFlow>(entity =>
        {
            entity.HasKey(flow => flow.UserId);
            entity.Property(flow => flow.FlowId).HasMaxLength(64);
            entity.HasOne(flow => flow.User)
                .WithOne()
                .HasForeignKey<GmailOAuthFlow>(flow => flow.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApplicationImport>(entity =>
        {
            entity.Property(import => import.ExternalMessageId).HasMaxLength(200);
            entity.Property(import => import.ExternalThreadId).HasMaxLength(200);
            entity.Property(import => import.EmailSubject).HasMaxLength(500);
            entity.Property(import => import.SenderDomain).HasMaxLength(255);
            entity.Property(import => import.CompanyName).HasMaxLength(150);
            entity.Property(import => import.JobTitle).HasMaxLength(150);
            entity.Property(import => import.JobLocation).HasMaxLength(150);
            entity.Property(import => import.JobUrl).HasMaxLength(2048);
            entity.Property(import => import.ResumeFileName).HasMaxLength(255);
            entity.Property(import => import.RowVersion).IsRowVersion();
            entity.HasIndex(import => new { import.GmailConnectionId, import.ExternalMessageId })
                .IsUnique();
            entity.HasIndex(import => new { import.UserId, import.Status, import.DetectedAt });
            entity.HasIndex(import => new { import.UserId, import.Status, import.ReviewedAt });
            entity.HasIndex(import => new { import.UserId, import.CreatedApplicationId });
            entity.HasOne(import => import.User)
                .WithMany()
                .HasForeignKey(import => import.UserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(import => import.GmailConnection)
                .WithMany(connection => connection.Imports)
                .HasForeignKey(import => import.GmailConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
