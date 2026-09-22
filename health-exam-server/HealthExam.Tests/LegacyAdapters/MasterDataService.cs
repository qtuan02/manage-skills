#nullable enable
using HealthExam.Infrastructure.Persistence.Legacy;
using HealthExam.API.Contracts;
using IHealthExamContext = HealthExam.Application.Common.IHealthExamContext;
using ValidationErrors = HealthExam.Application.Common.ValidationErrors;
using ValidationError = HealthExam.Application.Common.ValidationError;
using HealthExam.Domain.Common;
using HealthExam.Domain.ExamSessions;
using HealthExam.Domain.ExamRecords;
using HealthExam.Domain.Imports;
using HealthExam.Domain.Paraclinical;
using HealthExam.Domain.Webhooks;
using HealthExam.Domain.Integrations;
using HealthExam.Domain.Catalogs;
using Microsoft.EntityFrameworkCore;

namespace HealthExam.Server.Service;

public interface IMasterDataService
{
    Task<RegistrationOptionsItem> GetRegistrationOptionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MasterDataOptionItem>> ListProvincesAsync(string keyword, CancellationToken ct = default);
    Task<IReadOnlyList<MasterDataOptionItem>> ListWardsAsync(string provinceCode, string keyword, CancellationToken ct = default);
    Task<MasterDataOptionItem?> ResolveAsync(string category, string code, string field, CancellationToken ct = default);
    Task<ResolvedMasterDataOption?> ResolveOptionAsync(string category, string code, string field, CancellationToken ct = default);
    Task<MasterDataOptionItem?> ResolveWardAsync(string provinceCode, string wardCode, string field, CancellationToken ct = default);
}

public class MasterDataService : IMasterDataService
{
    private readonly IUnitOfWork _uow;
    private readonly IHealthExamContext _ctx;

    public static readonly IReadOnlyList<MasterDataOptionItem> StaticGenders = new[]
    {
        new MasterDataOptionItem("1", "Nam", 1),
        new MasterDataOptionItem("2", "Nữ", 2),
        new MasterDataOptionItem("3", "Khác", 3)
    };

    public static readonly IReadOnlyList<MasterDataOptionItem> StaticBloodAbos = new[]
    {
        new MasterDataOptionItem("A", "A", 1),
        new MasterDataOptionItem("B", "B", 2),
        new MasterDataOptionItem("AB", "AB", 3),
        new MasterDataOptionItem("O", "O", 4)
    };

    public static readonly IReadOnlyList<MasterDataOptionItem> StaticBloodRhs = new[]
    {
        new MasterDataOptionItem("+", "+", 1),
        new MasterDataOptionItem("-", "-", 2)
    };

    public static readonly IReadOnlyList<MasterDataOptionItem> StaticRelationships = new[]
    {
        new MasterDataOptionItem("FATHER", "Cha", 1),
        new MasterDataOptionItem("MOTHER", "Mẹ", 2),
        new MasterDataOptionItem("SPOUSE", "Vợ-chồng", 3),
        new MasterDataOptionItem("CHILD", "Con", 4),
        new MasterDataOptionItem("GUARDIAN", "Người giám hộ", 5),
        new MasterDataOptionItem("OTHER", "Khác", 6)
    };

    public static readonly IReadOnlyList<MasterDataOptionItem> StaticExamRecordStates = new[]
    {
        new MasterDataOptionItem("0", "Chưa đăng ký", 1),
        new MasterDataOptionItem("1", "Chờ khám", 2),
        new MasterDataOptionItem("2", "Đang khám", 3),
        new MasterDataOptionItem("3", "Đã khám", 4),
        new MasterDataOptionItem("4", "Hủy đăng ký", 5),
        new MasterDataOptionItem("5", "Hủy khám", 6)
    };

    public MasterDataService(IUnitOfWork uow, IHealthExamContext ctx)
    {
        _uow = uow;
        _ctx = ctx;
    }

    public async Task<RegistrationOptionsItem> GetRegistrationOptionsAsync(CancellationToken ct = default)
    {
        var tenantOptions = await _uow.MasterDataOptions.Query()
            .Where(x => x.DivisionID == _ctx.DivisionId && x.IsActive)
            .OrderBy(x => x.OrderNo)
            .ThenBy(x => x.Name)
            .Select(x => new { x.Category, x.Code, x.Name, x.OrderNo })
            .ToListAsync(ct);

        List<MasterDataOptionItem> GetCategory(string cat) =>
            tenantOptions.Where(x => x.Category == cat)
                .Select(x => new MasterDataOptionItem(x.Code, x.Name, x.OrderNo))
                .ToList();

        var examGroups = ExamGroups.All
            .Select(x => new ExamGroupItem
            {
                VariantCode = x.VariantCode,
                GroupName = x.GroupName,
                FormCode = x.FormCode,
                OrderNo = x.OrderNo
            })
            .ToList();

        return new RegistrationOptionsItem
        {
            Genders = StaticGenders,
            Ethnicities = GetCategory(MasterDataCategories.Ethnicity),
            Occupations = GetCategory(MasterDataCategories.Occupation),
            BloodAbos = StaticBloodAbos,
            BloodRhs = StaticBloodRhs,
            IdentityIssuers = GetCategory(MasterDataCategories.IdentityIssuer),
            Relationships = StaticRelationships,
            InsuranceObjects = GetCategory(MasterDataCategories.InsuranceObject),
            PatientTypes = GetCategory(MasterDataCategories.PatientType),
            PatientSubjects = GetCategory(MasterDataCategories.PatientSubject),
            RegistrationPlaces = GetCategory(MasterDataCategories.RegistrationPlace),
            PaymentSources = GetCategory(MasterDataCategories.PaymentSource),
            ExamLocations = GetCategory(MasterDataCategories.ExamLocation),
            ExamRecordStates = StaticExamRecordStates,
            ExamGroups = examGroups
        };
    }

    public async Task<IReadOnlyList<MasterDataOptionItem>> ListProvincesAsync(string keyword, CancellationToken ct = default)
    {
        var query = _uow.MasterDataOptions.Query()
            .Where(x => x.DivisionID == _ctx.DivisionId && x.Category == MasterDataCategories.Province && x.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(x => x.Code.ToLower().Contains(kw) || x.Name.ToLower().Contains(kw));
        }

        return await query
            .OrderBy(x => x.OrderNo)
            .ThenBy(x => x.Name)
            .Select(x => new MasterDataOptionItem(x.Code, x.Name, x.OrderNo))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MasterDataOptionItem>> ListWardsAsync(string provinceCode, string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provinceCode))
            throw HealthExamException.BadRequest("Mã tỉnh/thành phố không được để trống",
                ValidationErrors.Of("ProvinceCode", "Bỏ trống (bắt buộc)"));

        var provCode = provinceCode.Trim();
        var provinceExists = await _uow.MasterDataOptions.AnyAsync(
            x => x.DivisionID == _ctx.DivisionId && x.Category == MasterDataCategories.Province && x.Code == provCode && x.IsActive, ct);

        if (!provinceExists)
            throw HealthExamException.BadRequest("Tỉnh/thành phố không hợp lệ",
                ValidationErrors.Of("ProvinceCode", "Không tồn tại"));

        var query = _uow.MasterDataOptions.Query()
            .Where(x => x.DivisionID == _ctx.DivisionId && x.Category == MasterDataCategories.Ward && x.ParentCode == provCode && x.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(x => x.Code.ToLower().Contains(kw) || x.Name.ToLower().Contains(kw));
        }

        return await query
            .OrderBy(x => x.OrderNo)
            .ThenBy(x => x.Name)
            .Select(x => new MasterDataOptionItem(x.Code, x.Name, x.OrderNo))
            .ToListAsync(ct);
    }

    public async Task<MasterDataOptionItem?> ResolveAsync(string category, string code, string field, CancellationToken ct = default)
    {
        var resolved = await ResolveOptionAsync(category, code, field, ct);
        return resolved == null ? null : new MasterDataOptionItem(resolved.Code, resolved.Name, resolved.OrderNo);
    }

    public async Task<ResolvedMasterDataOption?> ResolveOptionAsync(string category, string code, string field, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var trimmedCode = code.Trim();

        if (category == MasterDataCategories.BloodAbo)
        {
            var match = StaticBloodAbos.FirstOrDefault(x => string.Equals(x.Code, trimmedCode, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw HealthExamException.BadRequest("Mã danh mục không hợp lệ", ValidationErrors.Of(field, "Mã danh mục không hợp lệ"));
            return new ResolvedMasterDataOption(Guid.Empty, match.Code, match.Name, match.OrderNo);
        }

        if (category == MasterDataCategories.BloodRh)
        {
            var match = StaticBloodRhs.FirstOrDefault(x => string.Equals(x.Code, trimmedCode, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw HealthExamException.BadRequest("Mã danh mục không hợp lệ", ValidationErrors.Of(field, "Mã danh mục không hợp lệ"));
            return new ResolvedMasterDataOption(Guid.Empty, match.Code, match.Name, match.OrderNo);
        }

        if (category == MasterDataCategories.Relationship)
        {
            var match = StaticRelationships.FirstOrDefault(x => string.Equals(x.Code, trimmedCode, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw HealthExamException.BadRequest("Mã danh mục không hợp lệ", ValidationErrors.Of(field, "Mã danh mục không hợp lệ"));
            return new ResolvedMasterDataOption(Guid.Empty, match.Code, match.Name, match.OrderNo);
        }

        if (category == MasterDataCategories.Gender)
        {
            var match = StaticGenders.FirstOrDefault(x => string.Equals(x.Code, trimmedCode, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw HealthExamException.BadRequest("Mã danh mục không hợp lệ", ValidationErrors.Of(field, "Mã danh mục không hợp lệ"));
            return new ResolvedMasterDataOption(Guid.Empty, match.Code, match.Name, match.OrderNo);
        }

        var option = await _uow.MasterDataOptions.Query()
            .Where(x => x.DivisionID == _ctx.DivisionId && x.Category == category && x.Code == trimmedCode && x.IsActive)
            .Select(x => new ResolvedMasterDataOption(x.OptionID, x.Code, x.Name, x.OrderNo))
            .FirstOrDefaultAsync(ct);

        if (option == null)
            throw HealthExamException.BadRequest("Mã danh mục không hợp lệ", ValidationErrors.Of(field, "Mã danh mục không hợp lệ"));

        return option;
    }

    public async Task<MasterDataOptionItem?> ResolveWardAsync(string provinceCode, string wardCode, string field, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(wardCode))
            return null;

        if (string.IsNullOrWhiteSpace(provinceCode))
            throw HealthExamException.BadRequest("Mã tỉnh/thành phố không được để trống",
                ValidationErrors.Of("ProvinceCode", "Bỏ trống (bắt buộc)"));

        var provCode = provinceCode.Trim();
        var code = wardCode.Trim();

        var option = await _uow.MasterDataOptions.Query()
            .Where(x => x.DivisionID == _ctx.DivisionId && x.Category == MasterDataCategories.Ward && x.ParentCode == provCode && x.Code == code && x.IsActive)
            .Select(x => new MasterDataOptionItem(x.Code, x.Name, x.OrderNo))
            .FirstOrDefaultAsync(ct);

        if (option == null)
            throw HealthExamException.BadRequest("Mã danh mục không hợp lệ", ValidationErrors.Of(field, "Mã danh mục không hợp lệ"));

        return option;
    }
}
