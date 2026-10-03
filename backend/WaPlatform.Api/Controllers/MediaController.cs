using Microsoft.AspNetCore.Mvc;
using WaPlatform.Api.Auth;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.Services;

namespace WaPlatform.Api.Controllers;

public record MediaFileDto(long Id, string FileName, string ContentType, long Size, string Kind);

[ApiController]
[Route("api/media")]
public class MediaController(AppDbContext db) : ControllerBase
{
    /// <summary>File types WhatsApp accepts, by extension (browsers report content types unreliably).</summary>
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".mp4"] = "video/mp4",
        [".3gp"] = "video/3gpp",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".amr"] = "audio/amr",
        [".ogg"] = "audio/ogg",
    };

    private const long MaxBytes = 16 * 1024 * 1024;
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private const long MaxRequestBytes = 105 * 1024 * 1024;

    /// <summary>Upload one or more files (form field "files"); returns their ids for sending.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<List<MediaFileDto>>> Upload([FromForm] List<IFormFile> files, CancellationToken ct)
    {
        if (files.Count == 0) return BadRequest(new ApiError("Choose at least one file."));
        if (files.Count > 100) return BadRequest(new ApiError("Upload at most 100 files at a time."));

        var saved = new List<MediaFile>();
        foreach (var f in files)
        {
            var (file, error) = await ReadAsync(f, ct);
            if (error is not null) return BadRequest(new ApiError(error));
            file!.UploadedById = User.GetUserId();
            saved.Add(file);
        }
        db.MediaFiles.AddRange(saved);
        await db.SaveChangesAsync(ct);
        return saved.Select(ToDto).ToList();
    }

    /// <summary>Downloads a file that was uploaded for sending.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        var file = await db.MediaFiles.FindAsync([id], ct);
        return file is null ? NotFound() : File(file.Content, file.ContentType, file.FileName);
    }

    public static async Task<(MediaFile? File, string? Error)> ReadAsync(IFormFile f, CancellationToken ct)
    {
        var name = Path.GetFileName(f.FileName);
        if (!Types.TryGetValue(Path.GetExtension(name), out var type))
            return (null, $"{name}: WhatsApp can't send this file type. Use PDF, Word, Excel, PowerPoint, text, JPG, PNG, MP4 or audio.");
        if (CheckSize(name, type, f.Length) is { } error) return (null, error);

        using var ms = new MemoryStream();
        await f.CopyToAsync(ms, ct);
        return (new MediaFile { FileName = name, ContentType = type, Size = f.Length, Content = ms.ToArray() }, null);
    }

    public static (MediaFile? File, string? Error) FromBytes(string fileName, byte[] content)
    {
        var name = Path.GetFileName(fileName);
        if (!Types.TryGetValue(Path.GetExtension(name), out var type))
            return (null, $"{name}: unsupported file type.");
        if (CheckSize(name, type, content.Length) is { } error) return (null, error);
        return (new MediaFile { FileName = name, ContentType = type, Size = content.Length, Content = content }, null);
    }

    private static string? CheckSize(string name, string type, long size)
    {
        if (size == 0) return $"{name} is empty.";
        if (type.StartsWith("image/") && size > MaxImageBytes) return $"{name} is larger than 5 MB, WhatsApp's limit for images.";
        if (size > MaxBytes) return $"{name} is larger than 16 MB.";
        return null;
    }

    public static MediaFileDto ToDto(MediaFile f) => new(f.Id, f.FileName, f.ContentType, f.Size, MessageService.MediaKind(f.ContentType));
}
