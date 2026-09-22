using System;
using System.Collections.Generic;

namespace HealthExam.Infrastructure.Integrations.SignServer;

/// <summary>
/// Fail-fast lúc khởi động: MinIO client trong <c>MinioExamFileStore</c> dựng LAZY ở lượt
/// Upload/Download đầu tiên, nên một pod thiếu MINIO_*/SIGN_SERVER_BASE_URL/SSM_BASE_URL vẫn
/// healthy cho tới lượt ký kết luận đầu tiên. Validator này bắt lỗi đó ngay lúc dựng DI. Chỉ áp
/// dụng khi HIS_EMR_ENABLED=true — cùng công tắc bật tính năng ký KSK; tắt thì không endpoint
/// nào đụng MinIO/sign-server/ssm-server.
/// </summary>
public static class SigningOptionsValidator
{
    public static void Validate(
        bool enabled,
        string signServerBaseUrl,
        string ssmBaseUrl,
        string minioEndpoint,
        string minioBucket,
        string minioAccessKey,
        string minioSecretKey)
    {
        if (!enabled) return;

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(signServerBaseUrl)) missing.Add("SIGN_SERVER_BASE_URL");
        if (string.IsNullOrWhiteSpace(ssmBaseUrl)) missing.Add("SSM_BASE_URL");
        if (string.IsNullOrWhiteSpace(minioEndpoint)) missing.Add("MINIO_PATH");
        if (string.IsNullOrWhiteSpace(minioBucket)) missing.Add("MINIO_BUCKET_HEALTH_EXAM");
        if (string.IsNullOrWhiteSpace(minioAccessKey)) missing.Add("MINIO_USERNAME");
        if (string.IsNullOrWhiteSpace(minioSecretKey)) missing.Add("MINIO_PASS");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Thiếu biến môi trường bắt buộc cho ký số KSK (HIS_EMR_ENABLED=true): {string.Join(", ", missing)}");
        }
    }
}
