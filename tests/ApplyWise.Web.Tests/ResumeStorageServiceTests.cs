using ApplyWise.Web.Services.ResumeStorage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class ResumeStorageServiceTests
{
    [Fact]
    public void New_rows_store_a_storage_root_relative_logical_key()
    {
        var contentRoot = TemporaryPath("content");
        var storageRoot = TemporaryPath("private-resumes");
        var storage = CreateStorage(contentRoot, storageRoot);

        var key = storage.CreateRelativePath("user-1", "resume.pdf");
        var resolved = storage.ResolvePath(key);

        Assert.Equal(Path.Combine("user-1", "resume.pdf"), key);
        Assert.Equal(Path.GetFullPath(Path.Combine(storageRoot, key)), resolved);
    }

    [Fact]
    public void Content_root_relative_legacy_rows_still_resolve()
    {
        var contentRoot = TemporaryPath("content");
        var storageRoot = Path.Combine(contentRoot, "App_Data", "Uploads", "Resumes");
        var storage = CreateStorage(contentRoot, storageRoot);
        var legacyPath = Path.Combine(
            "App_Data",
            "Uploads",
            "Resumes",
            "legacy-user",
            "resume.pdf");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(storageRoot, "legacy-user", "resume.pdf")),
            storage.ResolvePath(legacyPath));
    }

    [Fact]
    public void Absolute_legacy_rows_rebase_when_private_storage_moves()
    {
        var contentRoot = TemporaryPath("content");
        var oldStorageRoot = TemporaryPath("old-private-resumes");
        var newStorageRoot = TemporaryPath("new-private-resumes");
        var storage = CreateStorage(contentRoot, newStorageRoot);
        var legacyAbsolutePath = Path.Combine(
            oldStorageRoot,
            "legacy-user",
            "resume.pdf");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(newStorageRoot, "legacy-user", "resume.pdf")),
            storage.ResolvePath(legacyAbsolutePath));
    }

    [Fact]
    public void Invalid_paths_cannot_escape_private_storage()
    {
        var storage = CreateStorage(
            TemporaryPath("content"),
            TemporaryPath("private-resumes"));

        Assert.Throws<InvalidOperationException>(() =>
            storage.ResolvePath(Path.Combine("..", "outside.pdf")));
        Assert.Throws<ArgumentException>(() =>
            storage.CreateRelativePath("../outside", "resume.pdf"));
    }

    private static ResumeStorageService CreateStorage(
        string contentRoot,
        string storageRoot) =>
        new(
            new TestWebHostEnvironment(contentRoot),
            Options.Create(new ResumeStorageOptions { RootPath = storageRoot }));

    private static string TemporaryPath(string label) => Path.Combine(
        Path.GetTempPath(),
        $"applywise-{label}-{Guid.NewGuid():N}");

    private sealed class TestWebHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ApplyWise.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(contentRoot, "wwwroot");
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
