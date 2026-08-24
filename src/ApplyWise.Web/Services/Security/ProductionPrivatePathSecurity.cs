namespace ApplyWise.Web.Services.Security;

public static class ProductionPrivatePathSecurity
{
    public static void EnsureOutsideDeploymentRoot(
        string contentRootPath,
        string? webRootPath,
        string resumeStoragePath,
        string dataProtectionKeysPath,
        string dataProtectionCertificatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var deploymentRoots = new[] { contentRootPath, webRootPath }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(PathComparer)
            .ToArray();

        foreach (var (setting, configuredPath) in new[]
                 {
                     ("ResumeStorage:RootPath", resumeStoragePath),
                     ("DataProtection:KeysPath", dataProtectionKeysPath),
                     ("DataProtection:CertificatePath", dataProtectionCertificatePath)
                 })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);
            var fullPath = Path.GetFullPath(configuredPath);
            if (deploymentRoots.Any(root => IsSameOrDescendant(fullPath, root)))
            {
                throw new InvalidOperationException(
                    $"{setting} must be outside the application deployment directory so a deployment cannot erase private persistent data.");
            }
        }
    }

    public static void EnsureSinglePathOutsideDeploymentRoot(
        string contentRootPath,
        string? webRootPath,
        string setting,
        string configuredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);

        var fullPath = Path.GetFullPath(configuredPath);
        var deploymentRoots = new[] { contentRootPath, webRootPath }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(PathComparer);
        if (deploymentRoots.Any(root => IsSameOrDescendant(fullPath, root)))
        {
            throw new InvalidOperationException(
                $"{setting} must be outside the application deployment directory so a deployment cannot erase private persistent data.");
        }
    }

    public static bool IsSameOrDescendant(string candidatePath, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (string.Equals(candidate, root, PathComparison)) return true;

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, PathComparison);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
