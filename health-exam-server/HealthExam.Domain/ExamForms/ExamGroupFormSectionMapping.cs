using System;

namespace HealthExam.Domain.ExamForms;

public class ExamGroupFormSectionMapping
{
    public Guid SectionMappingID { get; set; }
    public Guid MappingID { get; set; }
    public string SectionKind { get; set; } = "";
    public int ItemGroupID { get; set; }

    public ExamGroupFormMapping Mapping { get; set; }
}
