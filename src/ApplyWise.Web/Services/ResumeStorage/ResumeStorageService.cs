using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.ResumeStorage;

public sealed class ResumeStorageService : IResumeStorageService
{
    private readonly string _contentRoot;
    private readonly string _storageRoot;

    public ResumeStorageService(IWebHostEnvironment environment, IOptions<ResumeStorageOptions> options)
    {
        _contentRoot = Path.GetFullPath(environment.ContentRootPath);
        var configuredRoot = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            throw new InvalidOperationException("ResumeStorage:RootPath must be configured.");
        }

        _storageRoot = Path.GetFullPath(Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(_contentRoot, configuredRoot));
    }

    public string CreateRelativePath(string userId, string storedFileName)
    {
        EnsureSafePathSegment(userId, nameof(userId));
        EnsureSafePathSegment(storedFileName, nameof(storedFileName));
        return Path.Combine(userId, storedFileName);
    }

    public string ResolvePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath))
        {
            var absoluteCandidate = Path.GetFullPath(relativePath);
            if (IsInsideStorageRoot(absoluteCandidate)) return absoluteCandidate;
        }
        else
        {
            // Rows created before storage-root-relative keys were introduced used a
            // path relative to ContentRoot. Preserve those rows while the configured
            // storage location remains unchanged.
            var legacyCandidate = Path.GetFullPath(Path.Combine(_contentRoot, relativePath));
            if (IsInsideStorageRoot(legacyCandidate)) return legacyCandidate;
        }

        // A restored volume may have a different absolute root. Resume files always
        // use the stable {userId}/{generatedFileName} layout, so safely rebase legacy
        // absolute/content-root-relative rows onto the configured storage root.
        var segments = relativePath
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment is not "." and not "..")
            .ToArray();
        if (segments.Length < 2)
        {
            throw new InvalidOperationException(
                "The resume path is not a valid storage-relative key.");
        }

        var userId = segments[^2];
        var storedFileName = segments[^1];
        EnsureSafePathSegment(userId, nameof(relativePath));
        EnsureSafePathSegment(storedFileName, nameof(relativePath));
        var rebasedCandidate = Path.GetFullPath(
            Path.Combine(_storageRoot, userId, storedFileName));
        if (!IsInsideStorageRoot(rebasedCandidate))
        {
            throw new InvalidOperationException(
                "The resume path is outside the private storage directory.");
        }

        return rebasedCandidate;
    }

    private bool IsInsideStorageRoot(string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = Path.TrimEndingDirectorySeparator(_storageRoot);
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(candidate);
        var rootWithSeparator = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(rootWithSeparator, comparison);
    }

    private static void EnsureSafePathSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Contains('/')
            || value.Contains('\\')
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "Resume storage path segments must be non-empty file-name components.",
                parameterName);
        }
    }
}
