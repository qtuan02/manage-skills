using System;
using HealthExam.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HealthExam.Tests.Signing;

public class MinioExamFileStoreTests
{
    /// <summary>
    /// Constructor không được đụng MinioClient: endpoint rỗng phải dựng được store (HIS tắt
    /// thì không ai gọi Upload/Download), lỗi cấu hình chỉ lộ ở lượt dùng đầu tiên.
    /// </summary>
    [Fact]
    public void Constructor_with_empty_endpoint_does_not_throw()
    {
        var store = new MinioExamFileStore(new ExamFileStoreOptions(), NullLogger<MinioExamFileStore>.Instance);

        Assert.NotNull(store);
    }

    /// <summary>
    /// MinIO trên cụm chạy TLS với cert tự ký (minio-cm: MINIO_SSL=true + MINIO_SKIP_CERT_VALIDATION=true).
    /// Tên biến giữ nguyên như các service khác để ops copy sang không phải dịch.
    /// </summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void FromEnvironment_reads_skip_cert_validation(string raw, bool expected)
    {
        var previous = Environment.GetEnvironmentVariable("MINIO_SKIP_CERT_VALIDATION");
        try
        {
            Environment.SetEnvironmentVariable("MINIO_SKIP_CERT_VALIDATION", raw);

            var options = ExamFileStoreOptions.FromEnvironment();

            Assert.Equal(expected, options.SkipCertValidation);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINIO_SKIP_CERT_VALIDATION", previous);
        }
    }
}
