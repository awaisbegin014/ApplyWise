using System.Diagnostics;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.BestResumePicker;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ApplyWise.Web.Tests;

public sealed class ResumeTaxonomyArtifactTests(ITestOutputHelper output)
{
    private const string JobDescription = """
        Senior Software Engineer
        Must Have:
        C# and AWS development expertise is mandatory.
        Requirements:
        SQL and REST API experience is required.
        Responsibilities:
        Develop reliable APIs using C# and collaborate with product teams.
        """;

    private const string ResumeText = """
        Awais Example
        awais@example.com | +1 555 010 1234 | Lahore, Pakistan | linkedin.com/in/awais

        Professional Summary
        Senior software engineer building reliable customer-facing platforms with C# and AWS.

        Experience
        Software Engineer | Example Company | Jan 2022 - Present
        - Developed REST APIs using C# and deployed services with AWS, reducing response time by 25%.
        - Collaborated with product teams to deliver customer workflows used by 10k users.

        Education
        Bachelor of Computer Science | Example University | 2018 - 2022

        Technical Skills
        C#, AWS, SQL, Git
        """;

    [Fact]
    public void Runtime_loader_uses_supported_relative_artifact_and_its_ambiguity_metadata()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("taxonomy.json", Artifact("runtime-2026.07"));

        var taxonomy = CreateTaxonomy(directory.RootPath, "taxonomy.json");

        Assert.Equal("artifact:runtime-2026.07", taxonomy.Version);
        var entry = Assert.Single(taxonomy.Entries);
        Assert.Equal("custom.quantum-ledger", entry.Id);
        Assert.Equal("Quantum Ledger", entry.PreferredLabel);
        Assert.Equal(["Q Ledger", "QL"], entry.Aliases);
        Assert.Equal(["QL"], entry.AmbiguousAliases);
        Assert.Single(taxonomy.FindMatches("Built a Quantum Ledger platform"), match =>
            match.SkillId == "custom.quantum-ledger");
        Assert.Empty(taxonomy.FindMatches("The QL policy was archived."));
        Assert.Single(taxonomy.FindMatches("Required QL skill for this role."), match =>
            match.SkillId == "custom.quantum-ledger");
        Assert.DoesNotContain(taxonomy.Entries, item => item.Id == "it.csharp");
    }

    [Fact]
    public void Runtime_loader_rejects_invalid_artifacts_and_missing_files_to_curated_fallback()
    {
        using var directory = new TemporaryDirectory();
        var invalidArtifacts = new[]
        {
            "{ this is not valid JSON",
            """{"schemaVersion":2,"taxonomyVersion":"unsupported","entries":[{"id":"x","preferredLabel":"X","aliases":[],"category":"Technical"}]}""",
            """{"schemaVersion":1,"taxonomyVersion":"empty","entries":[]}""",
            """{"schemaVersion":1,"taxonomyVersion":"duplicate","entries":[{"id":"x","preferredLabel":"X","aliases":[],"category":"Technical"},{"id":"X","preferredLabel":"Other X","aliases":[],"category":"Technical"}]}"""
        };

        for (var index = 0; index < invalidArtifacts.Length; index++)
        {
            var fileName = $"invalid-{index}.json";
            directory.Write(fileName, invalidArtifacts[index]);
            AssertCuratedFallback(CreateTaxonomy(directory.RootPath, fileName));
        }

        AssertCuratedFallback(CreateTaxonomy(directory.RootPath, "missing.json"));
    }

    [Fact]
    public async Task Taxonomy_version_changes_cache_identity_without_changing_score_version()
    {
        using var directory = new TemporaryDirectory();
        directory.Write("taxonomy-v1.json", Artifact("cache-v1"));
        directory.Write("taxonomy-v2.json", Artifact("cache-v2"));
        var taxonomyV1 = CreateTaxonomy(directory.RootPath, "taxonomy-v1.json");
        var taxonomyV2 = CreateTaxonomy(directory.RootPath, "taxonomy-v2.json");

        await using var db = CreateDbContext();
        var resume = CreateResume("owner", "Primary", "SKILLS\nQuantum Ledger");
        db.Resumes.Add(resume);
        await db.SaveChangesAsync();
        const string job = "Must Have:\nQuantum Ledger expertise is mandatory for this financial systems role.";

        var first = await CreateStore(db, taxonomyV1).AnalyzeAndStageAsync(
            resume,
            resume.ExtractedText!,
            job,
            null,
            ResumeAnalysisType.PastedRequirements);
        await db.SaveChangesAsync();

        var versionChanged = await CreateStore(db, taxonomyV2).AnalyzeAndStageAsync(
            resume,
            resume.ExtractedText!,
            job,
            null,
            ResumeAnalysisType.PastedRequirements);
        await db.SaveChangesAsync();

        var sameVersion = await CreateStore(db, taxonomyV2).AnalyzeAndStageAsync(
            resume,
            resume.ExtractedText!,
            job,
            null,
            ResumeAnalysisType.PastedRequirements);

        Assert.False(first.IsCacheHit);
        Assert.False(versionChanged.IsCacheHit);
        Assert.True(sameVersion.IsCacheHit);
        Assert.Equal("artifact:cache-v1", taxonomyV1.Version);
        Assert.Equal("artifact:cache-v2", taxonomyV2.Version);
        Assert.NotEqual(first.Analysis.InputHash, versionChanged.Analysis.InputHash);
        Assert.Equal(versionChanged.Analysis.InputHash, sameVersion.Analysis.InputHash);
        Assert.Equal(ResumeAnalysisResult.CurrentScoreVersion, first.Analysis.ScoreVersion);
        Assert.Equal(first.Analysis.ScoreVersion, versionChanged.Analysis.ScoreVersion);
        Assert.Equal(2, await db.ResumeAnalyses.CountAsync());
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Comparison_work_is_capped_and_cache_hit_timings_are_reported()
    {
        await using var db = CreateDbContext();
        var resumes = Enumerable.Range(1, 25)
            .Select(index => CreateResume(
                "owner",
                $"Version {index:00}",
                ResumeText + $"\nProjects\nVariant {index:00} customer platform."))
            .ToArray();
        db.Resumes.AddRange(resumes);
        await db.SaveChangesAsync();

        var normalizer = new ResumeTextNormalizer();
        var taxonomy = new SkillTaxonomyService(normalizer);
        var analyzer = CreateAnalyzer(normalizer, taxonomy);
        analyzer.Analyze(ResumeText, JobDescription);
        var store = new ResumeAnalysisStore(
            db,
            normalizer,
            taxonomy,
            analyzer,
            NullLogger<ResumeAnalysisStore>.Instance);
        var picker = new BestResumePickerService(
            db,
            new UnexpectedStorageService(),
            new UnexpectedTextExtractor(),
            store,
            new WorkspaceQuotaService(db, Options.Create(new WorkspaceQuotaOptions())),
            new WorkspaceQuotaGate(db),
            NullLogger<BestResumePickerService>.Instance);

        var firstWatch = Stopwatch.StartNew();
        var first = await picker.CompareResumesWithRequirementsAsync("owner", JobDescription);
        firstWatch.Stop();
        var rowsAfterFirstPass = await db.ResumeAnalyses.CountAsync();

        var cachedWatch = Stopwatch.StartNew();
        var cached = await picker.CompareResumesWithRequirementsAsync("owner", JobDescription);
        cachedWatch.Stop();

        output.WriteLine(
            "{0} capped cached-text resumes: analyze+persist={1:F3} ms; repeat cache-hit rank={2:F3} ms",
            BestResumePickerService.MaxResumesPerComparison,
            firstWatch.Elapsed.TotalMilliseconds,
            cachedWatch.Elapsed.TotalMilliseconds);

        Assert.Equal(BestResumePickerService.MaxResumesPerComparison, first.ComparedResumeCount);
        Assert.Equal(BestResumePickerService.MaxResumesPerComparison, first.ReadableResumeCount);
        Assert.Equal(BestResumePickerService.MaxResumesPerComparison, first.ComparedResumes.Count);
        Assert.Equal(BestResumePickerService.MaxResumesPerComparison, rowsAfterFirstPass);
        Assert.Equal(rowsAfterFirstPass, await db.ResumeAnalyses.CountAsync());
        Assert.Equal(first.RecommendedResumeId, cached.RecommendedResumeId);
        Assert.All(first.ComparedResumes, item => Assert.Null(item.AnalysisError));
        Assert.True(firstWatch.Elapsed >= TimeSpan.Zero);
        Assert.True(cachedWatch.Elapsed >= TimeSpan.Zero);
    }

    private static SkillTaxonomyService CreateTaxonomy(string contentRoot, string artifactPath) =>
        new(
            new ResumeTextNormalizer(),
            Options.Create(new SkillTaxonomyOptions { ArtifactPath = artifactPath }),
            new TestHostEnvironment(contentRoot),
            NullLogger<SkillTaxonomyService>.Instance);

    private static void AssertCuratedFallback(SkillTaxonomyService taxonomy)
    {
        Assert.Equal("curated-fallback-2026.08-v3", taxonomy.Version);
        Assert.Contains(taxonomy.Entries, item => item.Id == "it.csharp");
        Assert.Single(taxonomy.FindMatches("C# developer"), match => match.SkillId == "it.csharp");
    }

    private static ResumeAnalysisStore CreateStore(ApplicationDbContext db, ISkillTaxonomyService taxonomy)
    {
        var normalizer = new ResumeTextNormalizer();
        return new ResumeAnalysisStore(
            db,
            normalizer,
            taxonomy,
            CreateAnalyzer(normalizer, taxonomy),
            NullLogger<ResumeAnalysisStore>.Instance);
    }

    private static ResumeAnalysisService CreateAnalyzer(
        IResumeTextNormalizer normalizer,
        ISkillTaxonomyService taxonomy) =>
        new(
            new ResumeSectionDetector(normalizer),
            new JobRequirementExtractor(normalizer, taxonomy),
            new AtsReadinessScorer(),
            new JobMatchScorer(normalizer, taxonomy),
            NullLogger<ResumeAnalysisService>.Instance);

    private static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("taxonomy-runtime-tests-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static Resume CreateResume(string userId, string name, string text) => new()
    {
        UserId = userId,
        VersionName = name,
        OriginalFileName = name + ".pdf",
        StoredFileName = Guid.NewGuid().ToString("N") + ".pdf",
        FilePath = "private/" + Guid.NewGuid().ToString("N") + ".pdf",
        ContentType = "application/pdf",
        FileSize = 1024,
        ExtractedText = text,
        UploadedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static string Artifact(string version) => $$"""
        {
          "schemaVersion": 1,
          "taxonomyVersion": "{{version}}",
          "source": {
            "name": "Synthetic runtime fixture",
            "version": "1",
            "license": "CC0-1.0"
          },
          "entries": [
            {
              "id": "custom.quantum-ledger",
              "preferredLabel": "Quantum Ledger",
              "aliases": ["Q Ledger", "QL"],
              "category": "Domain",
              "ambiguity": {
                "requiresContext": true,
                "aliases": ["QL"]
              }
            }
          ]
        }
        """;

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "applywise-taxonomy-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Write(string relativePath, string content) =>
            File.WriteAllText(Path.Combine(RootPath, relativePath), content);

        public void Dispose()
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ApplyWise.Web.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class UnexpectedStorageService : IResumeStorageService
    {
        public string CreateRelativePath(string userId, string storedFileName) =>
            throw new Xunit.Sdk.XunitException("Cached resume text should avoid storage access.");

        public string ResolvePath(string relativePath) =>
            throw new Xunit.Sdk.XunitException("Cached resume text should avoid storage access.");
    }

    private sealed class UnexpectedTextExtractor : IResumeTextExtractorService
    {
        public Task<string?> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new Xunit.Sdk.XunitException("Cached resume text should avoid PDF extraction.");

        public Task<PdfTextExtractionResult> InspectAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new Xunit.Sdk.XunitException("Cached resume text should avoid PDF extraction.");
    }
}
