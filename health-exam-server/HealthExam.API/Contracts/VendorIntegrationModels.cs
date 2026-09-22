using System;
using System.Collections.Generic;

namespace HealthExam.API.Contracts;

/// <summary>
/// Gói RIS gọi ngược về báo trạng thái — ĐÚNG BA TRƯỜNG, sao chép
/// <c>UpdateParaClinProcessStatusDto</c> của pacs-connect-server.
/// </summary>
public class VendorStatusRequest
{
    public string OrderID { get; set; } = "";
    public string VoucherType { get; set; } = "0";
    public byte NewStatus { get; set; }
}

/// <summary>Kết quả một lượt áp cờ trạng thái của vendor — thân <c>Data</c> của phản hồi.</summary>
public class VendorStatusResult
{
    public Guid OrderItemID { get; set; }
    public string ServiceCode { get; set; } = "";
    public short State { get; set; }
    public bool Duplicated { get; set; }
    public string Detail { get; set; } = "";
}

/// <summary>Kết quả xếp hàng gửi một phiếu — thân <c>Data</c> của POST …/dispatch.</summary>
public class OrderDispatchResult
{
    public Guid OrderID { get; set; }
    public string OrderNo { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Operation { get; set; } = "";
    public long OutboxID { get; set; }
    public bool AlreadyQueued { get; set; }
    public List<string> LineIDs { get; set; } = new();
}

/// <summary>Số liệu một lượt rút hàng đợi gửi — dùng cho endpoint nội bộ và cho test.</summary>
public class OutboxDrainResult
{
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Remaining { get; set; }
    public int DeadLettered { get; set; }
}
