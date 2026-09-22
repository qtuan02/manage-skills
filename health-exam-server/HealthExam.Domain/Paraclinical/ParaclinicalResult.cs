using System;
using System.Collections.Generic;
using HealthExam.Domain.Common;

namespace HealthExam.Domain.Paraclinical;

/// <summary>
/// HEX_ParaclinicalResult — Kết quả cận lâm sàng của phiếu chỉ định (docs/superpowers/specs/2026-09-18-health-exam-paraclinical-design.md §Result).
/// </summary>
public class ParaclinicalResult : AuditableEntity
{
    public Guid ResultId { get; set; }
    public string DivisionID { get; set; } = "";
    public Guid OrderId { get; set; }
    public string HisResultId { get; set; } = "";
    public DateTime? ResultDate { get; set; }
    public string Status { get; set; } = "";
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;

    public ParaclinicalOrder Order { get; set; }
    public ICollection<ParaclinicalResultItem> Items { get; set; } = new List<ParaclinicalResultItem>();
}

/// <summary>
/// HEX_ParaclinicalResultItem — Chi tiết chỉ số kết quả xét nghiệm / mô tả kết quả CĐHA.
/// </summary>
public class ParaclinicalResultItem : AuditableEntity
{
    public Guid ResultItemId { get; set; }
    public string DivisionID { get; set; } = "";
    public Guid ResultId { get; set; }
    public Guid? OrderItemId { get; set; }
    public string HisDetailId { get; set; } = "";
    public string Value { get; set; } = "";
    public string Text { get; set; } = "";
    public string Unit { get; set; } = "";
    public string ReferenceRange { get; set; } = "";
    public string AbnormalFlag { get; set; } = "";

    public ParaclinicalResult Result { get; set; }
    public ParaclinicalOrderItem OrderItem { get; set; }
}
