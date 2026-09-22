using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Integrations;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace HealthExam.Infrastructure.Storage;

/// <summary>
/// Kho PDF đã ký trên MinIO. Client dựng LAZY ở lượt Upload/Download đầu tiên: MinioClient ném
/// lỗi ngay khi endpoint rỗng, mà IExamFileStore lại là dependency của ExamRecordController —
/// dựng sớm thì pod tắt HIS_EMR_ENABLED cũng không phục vụ được endpoint hồ sơ nào. Cấu hình
/// thiếu khi HIS bật đã có SigningOptionsValidator bắt lúc khởi động. Bucket phải có sẵn.
///
/// HttpClient tự dựng thay vì để SDK dựng: SocketsHttpHandler mặc định KHÔNG có ConnectTimeout,
/// nên endpoint sai scheme/port (SYN bị nuốt, không bị từ chối) treo PutObject cho tới khi caller
/// hủy — trên dhtesting là 60s của ingress. Upload chạy cuối giao dịch ký kết luận, trong lúc
/// đang giữ row lock, nên phải fail trong vài giây.
/// </summary>
public class MinioExamFileStore : IExamFileStore
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly Lazy<IMinioClient> _client;
    private readonly ExamFileStoreOptions _options;
    private readonly ILogger<MinioExamFileStore> _logger;

    public MinioExamFileStore(ExamFileStoreOptions options, ILogger<MinioExamFileStore> logger)
    {
        _options = options;
        _logger = logger;
        _client = new Lazy<IMinioClient>(() =>
        {
            var handler = new SocketsHttpHandler { ConnectTimeout = ConnectTimeout };
            if (options.SkipCertValidation)
                handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;

            var builder = new MinioClient()
                .WithEndpoint(options.Endpoint)
                .WithCredentials(options.AccessKey, options.SecretKey)
                .WithHttpClient(new HttpClient(handler) { Timeout = RequestTimeout }, disposeHttpClient: true);
            if (options.UseSsl) builder = builder.WithSSL();
            return builder.Build();
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<bool> UploadPdfAsync(string objectPath, byte[] pdf, CancellationToken ct = default)
    {
        try
        {
            using var stream = new MemoryStream(pdf);
            await _client.Value.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_options.Bucket)
                .WithObject(objectPath)
                .WithStreamData(stream)
                .WithObjectSize(pdf.LongLength)
                .WithContentType("application/pdf"), ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không ghi được object {ObjectPath} vào bucket {Bucket}", objectPath, _options.Bucket);
            return false;
        }
    }

    public async Task<byte[]> DownloadAsync(string objectPath, CancellationToken ct = default)
    {
        try
        {
            using var buffer = new MemoryStream();
            await _client.Value.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_options.Bucket)
                .WithObject(objectPath)
                .WithCallbackStream(s => s.CopyTo(buffer)), ct);
            return buffer.ToArray();
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không đọc được object {ObjectPath} từ bucket {Bucket}", objectPath, _options.Bucket);
            return null;
        }
    }
}
