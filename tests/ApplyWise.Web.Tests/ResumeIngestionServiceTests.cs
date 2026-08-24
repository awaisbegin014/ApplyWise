using System.Text;
using ApplyWise.Web.Data;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ApplyWise.Web.Services.Monitoring;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ResumeIngestionServiceTests
{
    [Fact]
    public async Task Valid_text_pdf_is_persisted_with_extracted_text()
    {
        await using var context = CreateContext();
        using var files = new TemporaryStorage();
        var extractor = new StubExtractor(new PdfTextExtractionResult(
            PdfTextExtractionStatus.Success,
            "Jordan Lee\nProfessional Summary\nExperience"));
        var service = CreateService(context, files, extractor);

        var result = await service.IngestAsync(new ResumeIngestionRequest(
            "user-1",
            "  Backend Resume  ",
            PdfFile("backend.pdf"),
            "  Current version  "));

        Assert.True(result.Succeeded);
        Assert.Equal(PdfTextExtractionStatus.Success, result.InspectionStatus);
        Assert.NotNull(result.Resume);
        Assert.Equal("Backend Resume", result.Resume.VersionName);
        Assert.Equal("Current version", result.Resume.Notes);
        Assert.Equal("Jordan Lee\nProfessional Summary\nExperience", result.Resume.ExtractedText);
        Assert.True(File.Exists(files.ResolvePath(result.Resume.FilePath)));
        Assert.Equal(1, await context.Resumes.CountAsync());
    }

    [Fact]
    public async Task Valid_docx_is_accepted_and_preserves_document_metadata()
    {
        await using var context = CreateContext();
        using var files = new TemporaryStorage();
        var diagnostics = new ResumeFileDiagnostics("DOCX", true, HasTables: true);
        var extractor = new StubExtractor(new PdfTextExtractionResult(
            PdfTextExtractionStatus.Success, "Jordan Lee\nExperience", 2, diagnostics));
        var service = CreateService(context, files, extractor);

        var result = await service.IngestAsync(new ResumeIngestionRequest(
            "user-1", "DOCX Resume", DocxFile("resume.docx"), RequireSelectableText: true));

        Assert.True(result.Succeeded);
        Assert.Equal(".docx", Path.GetExtension(result.Resume!.StoredFileName));
        Assert.Equal(2, result.Resume.PageCount);
        Assert.Contains("DOCX", result.Resume.FileDiagnosticsJson);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", result.Resume.ContentType);
    }

    [Fact]
    public async Task Image_only_pdf_can_be_saved_by_the_resume_library()
    {
        await using var context = CreateContext();
        using var files = new TemporaryStorage();
        var service = CreateService(
            context,
            files,
            new StubExtractor(new PdfTextExtractionResult(PdfTextExtractionStatus.NoText)));

        var result = await service.IngestAsync(new ResumeIngestionRequest(
            "user-1",
            "Scanned Resume",
            PdfFile("scan.pdf")));

        Assert.True(result.Succeeded);
        Assert.Equal(PdfTextExtractionStatus.NoText, result.InspectionStatus);
        Assert.Null(result.Resume!.ExtractedText);
        Assert.Equal(1, await context.Resumes.CountAsync());
        Assert.True(File.Exists(files.ResolvePath(result.Resume.FilePath)));
    }

    [Fact]
    public async Task Ats_ingestion_rejects_image_only_pdf_before_persisting()
    {
        await using var context = CreateContext();
        using var files = new TemporaryStorage();
        var service = CreateService(
            context,
            files,
            new StubExtractor(new PdfTextExtractionResult(PdfTextExtractionStatus.NoText)));

        var result = await service.IngestAsync(new ResumeIngestionRequest(
            "user-1",
            "Scanned Resume",
            PdfFile("scan.pdf"),
            RequireSelectableText: true));

        Assert.False(result.Succeeded);
        Assert.Equal(PdfTextExtractionStatus.NoText, result.InspectionStatus);
        Assert.Contains(result.Errors, error => error.Contains("No selectable text", StringComparison.Ordinal));
        Assert.Equal(0, await context.Resumes.CountAsync());
        Assert.Empty(files.GetStoredFiles());
    }

    [Fact]
    public async Task Invalid_pdf_header_is_rejected_before_inspection_or_storage()
    {
        await using var context = CreateContext();
        using var files = new TemporaryStorage();
        var extractor = new StubExtractor(new PdfTextExtractionResult(PdfTextExtractionStatus.Success, "text"));
        var service = CreateService(context, files, extractor);

        var bytes = Encoding.UTF8.GetBytes("This is not a PDF.");
        var result = await service.IngestAsync(new ResumeIngestionRequest(
            "user-1",
            "Invalid Resume",
            FormFile(bytes, "resume.pdf")));

        Assert.False(result.Succeeded);
        Assert.Contains("The selected file does not contain a valid PDF header.", result.Errors);
        Assert.Equal(0, extractor.InspectionCount);
        Assert.Equal(0, await context.Resumes.CountAsync());
        Assert.Empty(files.GetStoredFiles());
    }

    [Fact]
    public async Task Durable_cleanup_queue_retries_private_file_removal()
    {
        var services = new ServiceCollection();
        var databaseName = "resume-cleanup-" + Guid.NewGuid().ToString("N");
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        using var files = new TemporaryStorage();
        var relativePath = files.CreateRelativePath("user-1", "orphan.pdf");
        var absolutePath = files.ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllTextAsync(absolutePath, "private test file");

        var cleanup = new ResumeFileCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            files,
            NullLogger<ResumeFileCleanupService>.Instance);
        await cleanup.ScheduleAsync(relativePath);
        await cleanup.ScheduleAsync(relativePath);

        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Equal(
                1,
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .ResumeFileCleanups.CountAsync());
        }

        await cleanup.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var pollingScope = provider.CreateAsyncScope();
                var pendingCount = await pollingScope.ServiceProvider
                    .GetRequiredService<ApplicationDbContext>()
                    .ResumeFileCleanups.CountAsync();
                if (!File.Exists(absolutePath) && pendingCount == 0)
                {
                    break;
                }

                await Task.Delay(25);
            }
        }
        finally
        {
            await cleanup.StopAsync(CancellationToken.None);
        }

        Assert.False(File.Exists(absolutePath));
        await using var verificationScope = provider.CreateAsyncScope();
        Assert.Empty(
            await verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .ResumeFileCleanups.ToListAsync());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("resume-ingestion-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ResumeIngestionService CreateService(
        ApplicationDbContext context,
        IResumeStorageService storage,
        IResumeTextExtractorService extractor) =>
        new(
            context,
            storage,
            extractor,
            new RecordingCleanupScheduler(),
            new NoOpProductEventRecorder(),
            Options.Create(new ResumeStorageOptions
            {
                MaxFileSizeBytes = ResumeIngestionLimits.MaxFileSizeBytes,
                MaxFilesPerUser = 25,
                MaxBytesPerUser = 50 * 1024 * 1024,
                ExtractionTimeoutSeconds = 15
            }),
            NullLogger<ResumeIngestionService>.Instance,
            new WorkspaceQuotaGate(context));

    private sealed class NoOpProductEventRecorder : IProductEventRecorder
    {
        public Task RecordAsync(string name, string source, string? userId = null, bool succeeded = true, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordLoginAsync(string userId, string source, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingCleanupScheduler : IResumeFileCleanupScheduler
    {
        public List<string> ScheduledPaths { get; } = [];

        public Task ScheduleAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            ScheduledPaths.Add(relativePath);
            return Task.CompletedTask;
        }
    }

    private static IFormFile PdfFile(string fileName) =>
        FormFile(Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n%%EOF"), fileName);

    private static IFormFile DocxFile(string fileName) =>
        FormFile([0x50, 0x4B, 0x03, 0x04], fileName,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

    private static IFormFile FormFile(byte[] bytes, string fileName, string contentType = "application/pdf") =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "ResumeFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private sealed class StubExtractor(PdfTextExtractionResult result) : IResumeTextExtractorService
    {
        public int InspectionCount { get; private set; }

        public Task<string?> ExtractTextAsync(
            string filePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result.Text);

        public Task<PdfTextExtractionResult> InspectAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            InspectionCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class TemporaryStorage : IResumeStorageService, IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "applywise-ingestion-tests-" + Guid.NewGuid().ToString("N"));

        public string CreateRelativePath(string userId, string storedFileName) =>
            Path.Combine(userId, storedFileName);

        public string ResolvePath(string relativePath) =>
            Path.Combine(_root, relativePath);

        public IReadOnlyList<string> GetStoredFiles() =>
            Directory.Exists(_root)
                ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                : [];

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
