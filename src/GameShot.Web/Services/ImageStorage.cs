namespace GameShot.Web.Services;

public record ImageSaveResult(bool Success, string? Path, string? Error)
{
    public static ImageSaveResult Ok(string path) => new(true, path, null);
    public static ImageSaveResult Fail(string error) => new(false, null, error);
}

public interface IImageStorage
{
    Task<ImageSaveResult> SaveProductImageAsync(Stream content, string fileName, CancellationToken ct = default);
}

/// <summary>
/// 상품 이미지를 wwwroot/uploads/products 아래에 저장한다.
/// 확장자만 믿지 않고 파일 앞부분(매직 넘버)으로 실제 이미지인지 확인하고, 파일 이름은 새로 만든다.
/// </summary>
public class LocalImageStorage(IWebHostEnvironment env) : IImageStorage
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, byte[][]> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        // RIFF....WEBP 는 아래에서 따로 확인
        [".webp"] = [[0x52, 0x49, 0x46, 0x46]],
    };

    public async Task<ImageSaveResult> SaveProductImageAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!Signatures.TryGetValue(ext, out var signatures))
        {
            return ImageSaveResult.Fail("jpg, png, webp 이미지만 올릴 수 있습니다.");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length == 0)
        {
            return ImageSaveResult.Fail("빈 파일입니다.");
        }
        if (buffer.Length > MaxBytes)
        {
            return ImageSaveResult.Fail("이미지는 5MB까지 올릴 수 있습니다.");
        }

        var bytes = buffer.ToArray();
        var looksRight = signatures.Any(sig => bytes.AsSpan().StartsWith(sig))
            && (ext != ".webp" || (bytes.Length > 12 && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)));
        if (!looksRight)
        {
            return ImageSaveResult.Fail("이미지 파일이 아니거나 확장자와 내용이 다릅니다.");
        }

        var month = DateTime.UtcNow.ToString("yyyyMM");
        var dir = Path.Combine(env.WebRootPath, "uploads", "products", month);
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{(ext == ".jpeg" ? ".jpg" : ext)}";
        await File.WriteAllBytesAsync(Path.Combine(dir, name), bytes, ct);

        return ImageSaveResult.Ok($"/uploads/products/{month}/{name}");
    }
}
