using System;
using System.Collections.Generic;

namespace HealthExam.API.Contracts;

public record MasterDataOptionItem(string Code, string Name, int OrderNo);

public record ResolvedMasterDataOption(Guid OptionID, string Code, string Name, int OrderNo);

public class RegistrationOptionsItem
{
    public IReadOnlyList<MasterDataOptionItem> Genders { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> Ethnicities { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> Occupations { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> BloodAbos { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> BloodRhs { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> IdentityIssuers { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> Relationships { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> InsuranceObjects { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> RegistrationPlaces { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> PatientTypes { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> PatientSubjects { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> PaymentSources { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> ExamLocations { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<MasterDataOptionItem> ExamRecordStates { get; init; } = Array.Empty<MasterDataOptionItem>();
    public IReadOnlyList<ExamGroupItem> ExamGroups { get; init; } = Array.Empty<ExamGroupItem>();
}
