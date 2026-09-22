using System;

namespace HealthExam.Infrastructure.Storage;

/// <summary>
/// Tên biến lấy theo <c>minio-cm</c>/<c>minio-secret</c> dùng chung của cụm (sign-server,
/// his-server… cùng đọc) — pod envFrom hai cái đó là đủ, không chép lại cấu hình MinIO
/// vào ConfigMap riêng của service.
/// </summary>
public class ExamFileStoreOptions
{
    public string Endpoint { get; init; } = "";
    public string AccessKey { get; init; } = "";
    public string SecretKey { get; init; } = "";
    public string Bucket { get; init; } = "";
    public bool UseSsl { get; init; }
    public bool SkipCertValidation { get; init; }

    public static ExamFileStoreOptions FromEnvironment() => new()
    {
        Endpoint = Environment.GetEnvironmentVariable("MINIO_PATH")?.Trim() ?? "",
        AccessKey = Environment.GetEnvironmentVariable("MINIO_USERNAME")?.Trim() ?? "",
        SecretKey = Environment.GetEnvironmentVariable("MINIO_PASS")?.Trim() ?? "",
        Bucket = Environment.GetEnvironmentVariable("MINIO_BUCKET_HEALTH_EXAM")?.Trim() ?? "",
        UseSsl = string.Equals(
            Environment.GetEnvironmentVariable("MINIO_SSL"), "true", StringComparison.OrdinalIgnoreCase),
        SkipCertValidation = string.Equals(
            Environment.GetEnvironmentVariable("MINIO_SKIP_CERT_VALIDATION"), "true", StringComparison.OrdinalIgnoreCase)
    };
}
