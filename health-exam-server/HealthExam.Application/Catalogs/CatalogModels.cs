using System;
using System.Collections.Generic;

namespace HealthExam.Application.Catalogs;

public sealed record OrganizationResult(
    Guid OrganizationID,
    string OrgCode,
    string OrgName,
    string ShortName,
    string TaxCode,
    string Address,
    string ContactName,
    string ContactPhone,
    string ContactEmail,
    bool IsActive);

public sealed record ExamPackageResult(
    Guid PackageID,
    string PackageCode,
    string PackageName,
    string VariantCode,
    string Description,
    int ServiceCount,
    bool IsActive);

public sealed record ExamGroupResult(
    string VariantCode,
    string GroupName,
    string FormCode,
    int OrderNo);

public sealed record MasterDataOptionResult(
    string Code,
    string Name,
    int OrderNo);

public sealed class RegistrationOptionsResult
{
    public IReadOnlyList<MasterDataOptionResult> Genders { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<MasterDataOptionResult> Ethnicities { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<MasterDataOptionResult> Occupations { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<MasterDataOptionResult> BloodAbos { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<MasterDataOptionResult> BloodRhs { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<MasterDataOptionResult> IdentityIssuers { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<MasterDataOptionResult> Relationships { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<MasterDataOptionResult> InsuranceObjects { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<MasterDataOptionResult> PatientTypes { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<MasterDataOptionResult> PatientSubjects { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<MasterDataOptionResult> PaymentSources { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<MasterDataOptionResult> ExamLocations { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<MasterDataOptionResult> ExamRecordStates { get; init; } = Array.Empty<MasterDataOptionResult>();
    public IReadOnlyList<ExamGroupResult> ExamGroups { get; init; } = Array.Empty<ExamGroupResult>();
}

public sealed record ServiceCategoryResult(
    string CategoryCode,
    string CategoryName,
    int ServiceCount);

public sealed record ServiceGroupResult(
    string GroupCode,
    string GroupName,
    string CategoryCode,
    int ServiceCount);

public sealed record ServiceCatalogResult(
    long ServiceID,
    string ServiceCode,
    string ServiceName,
    string CategoryCode,
    string GroupCode,
    IReadOnlyList<Guid> InPackages);

public sealed record ServiceCatalogFilter(
    string CategoryCode = null,
    string GroupCode = null,
    string Keyword = null,
    int Page = 1,
    int Size = 20);
