using BridgeWindowsHost.Models;
using Microsoft.Extensions.Options;

namespace BridgeWindowsHost.Services;

public sealed class FileTransferService(IOptions<BridgeOptions> options)
{
    private readonly IOptions<BridgeOptions> _options = options;

    public FileListDto ListFiles()
    {
        var rootPath = EnsureRootDirectory();
        var rootDirectory = new DirectoryInfo(rootPath);
        var entries = rootDirectory.EnumerateFileSystemInfos("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            })
            .Select(ToEntry)
            .OrderBy(entry => entry.IsDirectory ? 0 : 1)
            .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FileListDto
        {
            RootDirectory = rootPath,
            Entries = entries
        };
    }

    public async Task<UploadFileResponse> SaveUploadAsync(IFormFile file, string? subdirectory, CancellationToken cancellationToken)
    {
        var targetDirectory = ResolvePathWithinRoot(subdirectory);
        Directory.CreateDirectory(targetDirectory);

        var safeName = Path.GetFileName(file.FileName);
        var destinationPath = Path.Combine(targetDirectory, safeName);

        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var uploadedStream = file.OpenReadStream();
        await uploadedStream.CopyToAsync(fileStream, cancellationToken);

        return new UploadFileResponse
        {
            RelativePath = Path.GetRelativePath(EnsureRootDirectory(), destinationPath),
            SizeBytes = file.Length,
            UploadedAt = DateTimeOffset.UtcNow
        };
    }

    public DownloadFileHandle OpenDownload(string relativePath)
    {
        var fullPath = ResolvePathWithinRoot(relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Requested file was not found.", fullPath);
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new DownloadFileHandle(stream, Path.GetFileName(fullPath));
    }

    private FileEntryDto ToEntry(FileSystemInfo fileSystemInfo)
    {
        var fullRootPath = EnsureRootDirectory();
        return new FileEntryDto
        {
            RelativePath = Path.GetRelativePath(fullRootPath, fileSystemInfo.FullName),
            IsDirectory = (fileSystemInfo.Attributes & FileAttributes.Directory) != 0,
            SizeBytes = fileSystemInfo is FileInfo fileInfo ? fileInfo.Length : null,
            LastModifiedAt = fileSystemInfo.LastWriteTimeUtc
        };
    }

    private string EnsureRootDirectory()
    {
        var expandedRoot = Environment.ExpandEnvironmentVariables(_options.Value.StorageRoot);
        Directory.CreateDirectory(expandedRoot);
        return Path.GetFullPath(expandedRoot);
    }

    private string ResolvePathWithinRoot(string? relativePath)
    {
        var rootPath = EnsureRootDirectory();
        var combinedPath = string.IsNullOrWhiteSpace(relativePath)
            ? rootPath
            : Path.GetFullPath(Path.Combine(rootPath, relativePath));

        // Normalize both paths before comparison so uploads and downloads cannot escape the configured root.
        var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var allowedPrefix = normalizedRoot + Path.DirectorySeparatorChar;
        var isExactRoot = string.Equals(combinedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase);
        var isUnderRoot = combinedPath.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase);

        if (!isExactRoot && !isUnderRoot)
        {
            throw new InvalidOperationException("The requested path escapes the shared folder.");
        }

        return combinedPath;
    }
}

public sealed record DownloadFileHandle(Stream Stream, string DownloadName);
