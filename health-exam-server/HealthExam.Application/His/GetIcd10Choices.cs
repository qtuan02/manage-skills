#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HealthExam.Application.Common;
using HealthExam.Application.Integrations;

namespace HealthExam.Application.His;

public interface IGetIcd10ChoicesHandler
{
    Task<ApplicationResult<IReadOnlyList<Icd10Choice>>> HandleAsync(
        GetIcd10ChoicesQuery query, CancellationToken ct = default);
}

public sealed class GetIcd10ChoicesHandler : IGetIcd10ChoicesHandler
{
    private readonly IHisEmrClient _client;
    private readonly IHisFormDefinitionCache _cache;

    public GetIcd10ChoicesHandler(
        IHisEmrClient client,
        IHisFormDefinitionCache cache)
    {
        _client = client;
        _cache = cache;
    }

    public async Task<ApplicationResult<IReadOnlyList<Icd10Choice>>> HandleAsync(
        GetIcd10ChoicesQuery query, CancellationToken ct = default)
    {
        var trimmedFilter = query.Filter?.Trim();
        var amount = query.Amount > 0 ? query.Amount : 20;

        var cached = await _cache.GetOrCreateIcd10Async(
            query.DivisionId,
            trimmedFilter ?? "",
            amount,
            async token =>
            {
                try
                {
                    var hisRes = await _client.GetIcd10ChoicesAsync(
                        trimmedFilter ?? "",
                        amount,
                        new HisCallContext(query.Credential, query.TraceId, query.DivisionId),
                        token);

                    if (hisRes.IsSuccess && hisRes.Value != null && hisRes.Value.Count > 0)
                    {
                        return hisRes.Value;
                    }
                }
                catch
                {
                }

                return FallbackChoices
                    .Where(c => string.IsNullOrEmpty(trimmedFilter) ||
                                c.Code.Contains(trimmedFilter, StringComparison.OrdinalIgnoreCase) ||
                                c.Label.Contains(trimmedFilter, StringComparison.OrdinalIgnoreCase))
                    .Take(amount)
                    .ToList();
            },
            ct);

        return ApplicationResult<IReadOnlyList<Icd10Choice>>.Success(cached);
    }

    private static readonly IReadOnlyList<Icd10Choice> FallbackChoices = new List<Icd10Choice>
    {
        new("Z00.0", "Z00.0 - Khám sức khỏe định kỳ (tổng quát)"),
        new("Z10.0", "Z10.0 - Khám sức khỏe nghề nghiệp"),
        new("I10", "I10 - Tăng huyết áp vô căn (nguyên phát)"),
        new("I11", "I11 - Bệnh tim do tăng huyết áp"),
        new("I20", "I20 - Cơn đau thắt ngực"),
        new("I49.9", "I49.9 - Rối loạn nhịp tim không xác định"),
        new("J00", "J00 - Viêm mũi họng cấp (cảm thường)"),
        new("J02", "J02 - Viêm họng cấp"),
        new("J06.9", "J06.9 - Nhiễm trùng đường hô hấp trên cấp"),
        new("J20.9", "J20.9 - Viêm phế quản cấp không xác định"),
        new("J45", "J45 - Hen phế quản (suyễn)"),
        new("K21.0", "K21.0 - Bệnh trào ngược dạ dày - thực quản"),
        new("K29.5", "K29.5 - Viêm dạ dày mạn tính không xác định"),
        new("K29.7", "K29.7 - Viêm dạ dày không xác định"),
        new("K25", "K25 - Loét dạ dày"),
        new("K58", "K58 - Hội chứng ruột kích thích"),
        new("K76.0", "K76.0 - Gan thoái hóa mỡ (gan nhiễm mỡ)"),
        new("M54.5", "M54.5 - Đau thắt lưng (đau cột sống thắt lưng)"),
        new("M50", "M50 - Bệnh lý đĩa đệm cột sống cổ"),
        new("M51", "M51 - Thoát vị đĩa đệm cột sống khác"),
        new("M17", "M17 - Thoái hóa khớp gối"),
        new("M19", "M19 - Thoái hóa khớp khác"),
        new("G43", "G43 - Đau nửa đầu (Migraine)"),
        new("G44.2", "G44.2 - Đau đầu căng thẳng"),
        new("G47.0", "G47.0 - Rối loạn giấc ngủ (Mất ngủ)"),
        new("H52.1", "H52.1 - Cận thị"),
        new("H52.2", "H52.2 - Loạn thị"),
        new("H52.4", "H52.4 - Viễn thị / Lão thị"),
        new("H10", "H10 - Viêm kết mạc"),
        new("H66", "H66 - Viêm tai giữa"),
        new("J30", "J30 - Viêm mũi dị ứng"),
        new("J31", "J31 - Viêm mũi mạn tính"),
        new("J35.0", "J35.0 - Viêm amiđan mạn tính"),
        new("K02", "K02 - Sâu răng"),
        new("K05", "K05 - Viêm lợi và bệnh nha chu"),
        new("L20", "L20 - Viêm da cơ địa (chàm)"),
        new("L70", "L70 - Mụn trứng cá"),
        new("L50", "L50 - Mày đay"),
        new("E11", "E11 - Đái tháo đường týp 2"),
        new("E78.0", "E78.0 - Tăng cholesterol máu"),
        new("E78.5", "E78.5 - Rối loạn lipid máu không xác định"),
        new("E04", "E04 - Bướu giáp không độc khác"),
        new("E66", "E66 - Thừa cân / Béo phì"),
        new("N20", "N20 - Sỏi thận và sỏi niệu quản"),
        new("N39.0", "N39.0 - Nhiễm trùng đường tiết niệu không xác định"),
        new("N76", "N76 - Viêm âm đạo và âm hộ khác"),
        new("N72", "N72 - Viêm cổ tử cung")
    };
}
