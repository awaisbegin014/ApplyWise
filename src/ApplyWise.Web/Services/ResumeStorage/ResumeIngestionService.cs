using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.ResumeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.Services.Security;

namespace ApplyWise.Web.Services.ResumeStorage;

public sealed class ResumeIngestionService(
    ApplicationDbContext dbContext,
    IResumeStorageService resumeStorage,
    IResumeTextExtractorService textExtractor,
    IResumeFileCleanupScheduler cleanupScheduler,
    IProductEventRecorder events,
    IOptions<ResumeStorageOptions> storageOptions,
    ILogger<ResumeIngestionService> logger,
    IWorkspaceQuotaGate quotaGate) : IResumeIngestionService
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipSignature = [0x50, 0x4B];
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public async Task<ResumeIngestionResult> IngestAsync(
        ResumeIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);

        var validationErrors = await ValidateFileAsync(request.File, cancellationToken);
        if (validationErrors.Count > 0)
        {
            return ResumeIngestionResult.Failed(validationErrors);
        }

        var file = request.File!;
        var limits = storageOptions.Value;
        var usage = await GetUsageAsync(request.UserId, cancellationToken);
        if (ExceedsStorageLimit(usage.Count, usage.Bytes, file.Length, limits))
        {
            return ResumeIngestionResult.Failed(
            [
                $"Your resume library is limited to {limits.MaxFilesPerUser} files and {limits.MaxBytesPerUser / (1024 * 1024)} MB."
            ]);
        }

        var originalFileName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var relativePath = resumeStorage.CreateRelativePath(request.UserId, storedFileName);
        var absolutePath = resumeStorage.ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        PdfTextExtractionResult inspection;
        try
        {
            await using (var output = File.Create(absolutePath))
            {
                await file.CopyToAsync(output, cancellationToken);
            }

            inspection = await textExtractor.InspectAsync(absolutePath, cancellationToken);
        }
        catch (Exception exception)
        {
            await DeleteOrScheduleAsync(relativePath, absolutePath, CancellationToken.None);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }

        if (!inspection.IsValidDocument)
        {
            await DeleteOrScheduleAsync(relativePath, absolutePath, CancellationToken.None);
            return ResumeIngestionResult.Failed([GetInspectionError(inspection.Status)], inspection);
        }

        if (request.RequireSelectableText
            && (inspection.Status != PdfTextExtractionStatus.Success
                || string.IsNullOrWhiteSpace(inspection.Text)))
        {
            await DeleteOrScheduleAsync(relativePath, absolutePath, CancellationToken.None);
            return ResumeIngestionResult.Failed(
                ["No selectable text or other readable text was found. Upload a text-based PDF or DOCX exported directly from your editor."],
                inspection);
        }

        var now = DateTimeOffset.UtcNow;
        var resume = new Resume
        {
            UserId = request.UserId,
            VersionName = request.VersionName.Trim(),
            OriginalFileName = originalFileName,
            StoredFileName = storedFileName,
            FilePath = relativePath,
            ContentType = extension == ".docx"
                ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                : "application/pdf",
            FileSize = file.Length,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            IsDefault = request.IsDefault,
            UploadedAt = now,
            UpdatedAt = now,
            ExtractedText = inspection.Text,
            PageCount = inspection.PageCount,
            FileDiagnosticsJson = inspection.Diagnostics is null
                ? null
                : JsonSerializer.Serialize(inspection.Diagnostics, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };

        try
        {
            var saved = await quotaGate.RunAsync(
                WorkspaceQuotaResources.Resumes,
                request.UserId,
                async operationCancellationToken =>
                {
                    var currentUsage = await GetUsageAsync(
                        request.UserId,
                        operationCancellationToken);
                    if (ExceedsStorageLimit(
                            currentUsage.Count,
                            currentUsage.Bytes,
                            resume.FileSize,
                            limits))
                    {
                        return false;
                    }

                    if (resume.IsDefault)
                    {
                        await dbContext.Resumes
                            .Where(item => item.UserId == request.UserId && item.IsDefault)
                            .ExecuteUpdateAsync(
                                setters => setters.SetProperty(item => item.IsDefault, false),
                                operationCancellationToken);
                    }

                    dbContext.Resumes.Add(resume);
                    await dbContext.SaveChangesAsync(operationCancellationToken);
                    return true;
                },
                cancellationToken);
            if (!saved)
            {
                await DeleteOrScheduleAsync(relativePath, absolutePath, CancellationToken.None);
                return ResumeIngestionResult.Failed(
                    ["Your resume library reached its storage limit while this upload was being prepared."],
                    inspection);
            }
        }
        catch (Exception exception)
        {
            await DeleteOrScheduleAsync(relativePath, absolutePath, CancellationToken.None);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }

        await events.RecordAsync(
            ProductEventNames.ResumeUploaded,
            extension.TrimStart('.'),
            request.UserId,
            cancellationToken: cancellationToken);
        return new ResumeIngestionResult(resume, inspection, []);
    }

    private async Task<(int Count, long Bytes)> GetUsageAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var usage = await dbContext.Resumes
            .Where(resume => resume.UserId == userId)
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Bytes = group.Sum(resume => resume.FileSize) })
            .SingleOrDefaultAsync(cancellationToken);
        return usage is null ? (0, 0L) : (usage.Count, usage.Bytes);
    }

    private static bool ExceedsStorageLimit(
        int fileCount,
        long storedBytes,
        long incomingBytes,
        ResumeStorageOptions limits) =>
        fileCount >= limits.MaxFilesPerUser
        || storedBytes > limits.MaxBytesPerUser - incomingBytes;

    private static async Task<IReadOnlyList<string>> ValidateFileAsync(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return ["Choose a PDF or DOCX resume to upload."];
        }

        var errors = new List<string>();
        if (file.Length == 0)
        {
            errors.Add("The selected file is empty.");
        }
        else if (file.Length > ResumeIngestionLimits.MaxFileSizeBytes)
        {
            errors.Add("The resume must be 5 MB or smaller.");
        }

        var extension = Path.GetExtension(file.FileName);
        var isPdf = string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);
        var isDocx = string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase);
        if (!isPdf && !isDocx)
        {
            errors.Add("Only PDF and DOCX files are supported.");
        }

        if (file.Length > 0 && file.Length <= ResumeIngestionLimits.MaxFileSizeBytes)
        {
            await using var stream = file.OpenReadStream();
            var hasExpectedSignature = isPdf
                ? await HasPdfSignatureAsync(stream, cancellationToken)
                : isDocx && await HasZipSignatureAsync(stream, cancellationToken);
            if (!hasExpectedSignature)
            {
                errors.Add(isPdf
                    ? "The selected file does not contain a valid PDF header."
                    : "The selected file does not contain a valid DOCX package header.");
            }
        }

        return errors;
    }

    private static async Task<bool> HasPdfSignatureAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[PdfSignature.Length + 8];
        var bytesRead = await stream.ReadAsync(header, cancellationToken);
        var offset = header.AsSpan(0, bytesRead).StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        while (offset < bytesRead && header[offset] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            offset++;
        }

        return bytesRead - offset >= PdfSignature.Length
               && header.AsSpan(offset, PdfSignature.Length).SequenceEqual(PdfSignature);
    }

    private static async Task<bool> HasZipSignatureAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        var bytesRead = await stream.ReadAsync(header, cancellationToken);
        return bytesRead >= ZipSignature.Length && header.AsSpan(0, ZipSignature.Length).SequenceEqual(ZipSignature);
    }

    private static string GetInspectionError(PdfTextExtractionStatus status) => status switch
    {
        PdfTextExtractionStatus.Encrypted =>
            "Password-protected or encrypted PDFs are not supported. Save an unprotected copy and try again.",
        PdfTextExtractionStatus.PageLimitExceeded =>
            $"The PDF must contain between 1 and {PdfTextInspector.MaxPages} pages.",
        PdfTextExtractionStatus.TextLimitExceeded =>
            "The PDF contains too much embedded text to process safely.",
        PdfTextExtractionStatus.TimedOut =>
            "The PDF took too long to inspect. Try exporting a simpler PDF and upload it again.",
        PdfTextExtractionStatus.Unavailable =>
            "The PDF could not be inspected right now. Please try again.",
        _ => "The selected file is damaged or is not a valid PDF or DOCX document."
    };

    private static string SanitizeFileName(string fileName)
    {
        var baseName = Path.GetFileName(fileName);
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = new string(baseName.Where(character =>
            !invalidCharacters.Contains(character) && !char.IsControl(character)).ToArray()).Trim();

        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "resume.pdf";
        }

        if (safeName.Length <= 255) return safeName;
        var extension = Path.GetExtension(safeName);
        var allowedExtension = extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) ? ".docx" : ".pdf";
        return safeName[..(255 - allowedExtension.Length)] + allowedExtension;
    }

    private async Task DeleteOrScheduleAsync(
        string relativePath,
        string absolutePath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not immediately remove an uncommitted resume upload; scheduling durable cleanup.");
            await cleanupScheduler.ScheduleAsync(relativePath, cancellationToken);
        }
    }
}
