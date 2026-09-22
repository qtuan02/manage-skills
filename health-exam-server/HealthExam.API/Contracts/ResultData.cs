using System.Collections.Generic;

namespace HealthExam.API.Contracts;

/// <summary>
/// Envelope phản hồi — 02-api-spec §1.2.
/// Giữ tên trường của ResultData cũ (ErrorCode/Message/Data) để FE không phải viết
/// adapter thứ hai, và thêm TraceID để đối soát log Elasticsearch.
/// </summary>
public class ResultData<T>
{
    public int ErrorCode { get; set; }
    public string Message { get; set; } = "";
    public T Data { get; set; }
    public string TraceID { get; set; } = "";

    public ResultData() { }

    public ResultData(T data)
    {
        ErrorCode = ErrorCodes.Success;
        Data = data;
    }

    public static ResultData<T> Ok(T data, string traceId = "")
        => new(data) { TraceID = traceId };

    public static ResultData<T> Fail(int errorCode, string message = null, T data = default, string traceId = "")
        => new()
        {
            ErrorCode = errorCode,
            Message = message ?? ErrorCodes.DefaultMessage(errorCode),
            Data = data,
            TraceID = traceId
        };
}

/// <summary>Envelope không mang payload — dùng cho DELETE và các lệnh chỉ báo kết quả.</summary>
public class ResultData : ResultData<object>
{
    public ResultData() { }
    public ResultData(object data) : base(data) { }
}
