using System;
using System.Collections.Generic;
using HealthExam.Domain.Common;

namespace HealthExam.Domain.ExamForms;

public class ExamGroupFormMapping
{
    public Guid MappingID { get; set; }
    public string DivisionID { get; set; } = "";
    public string VariantCode { get; set; } = "";
    public string TemplateCode { get; set; } = "";
    public string MedicalTypeCode { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public long CreatedBy { get; set; }
    public ActorKind CreatedActorKind { get; set; }
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
    public long ModifiedBy { get; set; }
    public ActorKind ModifiedActorKind { get; set; }

    public ICollection<ExamGroupFormSectionMapping> Sections { get; set; } = new List<ExamGroupFormSectionMapping>();
}
