# Clinical Ownership, Signing and Conclusion Implementation Plan

> **Superseded — không thực thi kế hoạch signing này:** theo yêu cầu đồng bộ mới của người dùng, signing lấy từ `origin/main` commit `443b266`. Tham chiếu kế hoạch `2026-09-19-section-sign-choose-signer.md` và API docs trên main. Nội dung/checkbox bên dưới chỉ giữ làm lịch sử, không đại diện implementation hiện tại. Bản signing local trước đồng bộ có thể khôi phục từ stash `052b2c72fdc52533d02256520c38faf990ae6714`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sửa PROJ2368–2374: A thao tác danh mục của mình, B hiển thị PDF, reset đúng mặc định và kết luận chuyển trạng thái nhất quán.

**Architecture:** Mở rộng `ExamRecordSignStep` hiện có từ lúc lưu `InProgress`, giữ owner A độc lập với B và timestamp hiển thị. Dùng chung đường reset HIS cho xóa/hủy danh mục và hủy kết luận; giữ render-once/sign-in-memory/upload-once hiện tại. Không thay chứng thư bằng chứng thư B.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core/PostgreSQL, xUnit, HIS, sign-server, MinIO.

**Spec:** `docs/superpowers/specs/2026-09-19-bugs-report-design.md`, sections 1, 5–7.

## Global Constraints

- Lưu/sửa form không bắt buộc B; chỉ ký danh mục/kết luận mới bắt buộc có B hợp lệ. Không tự gán A làm B.
- “Danh tính A phải lấy từ ngữ cảnh xác thực, không tin giá trị client gửi.”
- “Việc đổi B không làm đổi chủ sở hữu A.”
- “Không tự lấy chứng thư B chỉ vì A chọn tên B.”
- “Không backfill giả A/B hoặc thời gian ký cho dữ liệu cũ.”
- “Không coi transaction DB nội bộ bao phủ được HTTP tới HIS.”
- “Không chạy migration, ghi dữ liệu thật, gọi ký thật hoặc deploy trong giai đoạn thiết kế/kế hoạch.”
- B hợp lệ trong danh sách bác sĩ KSK thuộc phạm vi truy cập hiện hành; không có bảng phân công A–B.
- Quyền hiện hành của A và kiểm tra chứng thư vẫn có hiệu lực. Nếu tài khoản phụ tá thực tế thiếu quyền/chứng thư, báo dependency cấu hình, không âm thầm gỡ guard.
- Áp dụng baseline, GitNexus, commit theo đường dẫn và DB test riêng trong kế hoạch tổng.

## Cấu trúc file và hợp đồng chung

Giữ các handler hiện có; chỉ thêm handler cho API chưa tồn tại và phần reset thực sự dùng chung. Các tên sau là hợp đồng đích của kế hoạch, chưa phải symbol đã tồn tại:

```csharp
// B là thông tin nghiệp vụ; identity/name resolve server-side.
public sealed record ClinicalDoctor(long EmployeeId, string EmployeeCode, string EmployeeName);
public sealed record SectionExecutionInfo(
    long? PerformedByEmployeeId, string PerformedByEmployeeName,
    long? ConfirmingDoctorId, string ConfirmingDoctorName,
    DateTimeOffset? PerformedAt, string Status,
    long? SignedByEmployeeId, DateTime? SignedAt);
```

Đặt `ClinicalDoctor` trong `HealthExam.Application/His/HisModels.cs`; `SectionExecutionInfo` trong `HealthExam.Application/Signing/SigningModels.cs`. Tên JSON theo serializer hiện hành. Không đổi ý nghĩa các field SignedBy cũ thành B.

## Task 1: Khóa hợp đồng bác sĩ, mặc định biểu mẫu và gateway

**Files:** Read `HealthExam.Application/Integrations/IHisEmrClient.cs`, `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`, `HealthExam.Application/His/GetHisFormDefinition.cs`, `GetHisRecordSection.cs`, `HealthExam.Infrastructure/Integrations/SignServer/SignServerPdfSigner.cs`. Create `docs/api/clinical-execution-contract.md` và fixtures JSON đã loại dữ liệu nhạy cảm trong `HealthExam.Tests/Fixtures/ClinicalExecution/`.

**Interfaces:** Chốt một nguồn danh sách B có thể kiểm chứng và schema mặc định biểu mẫu. Không tự tạo URL HIS. Adapter ký đã có `PdfSignRequest.Certificate`, `EmployeeName`, `DisplayDate` tách riêng.

- [ ] Tích hợp nguồn danh sách B do người dùng cung cấp: GET `/api/SAMF00070/GetListGroup`, lọc `group.RoleID == 140`, lấy **tất cả** group phù hợp rồi GET `/api/SAMF00050/GetListAccInGroup?groupID={groupID}` cho từng group. `/api/SAMF00070/GetListGroupByID?id={groupID}` chỉ dùng khi cần kiểm tra group cụ thể, không bắt buộc gọi thêm cho mọi group.
- [ ] Map account active có `employeeID > 0` sang `ClinicalDoctor(employeeID, employeeCode, employeeName)`; không dùng `accountID` làm doctor ID. Loại trùng theo employeeID trong phạm vi đơn vị, không lọc theo departmentID của A vì A được chọn mọi B trong danh sách được phép. Nếu record có roleID/groupID trái với group đã chọn, từ chối record đó và ghi lỗi dữ liệu an toàn; không âm thầm cấp quyền. Nếu cùng employeeID có code/name mâu thuẫn, báo lỗi dữ liệu thay vì chọn ngẫu nhiên.
- [ ] Thêm fixture theo response mẫu người dùng, kiểm chứng envelope/phân trang/casing của response thực tế trước chốt parser; không tự đoán wrapper hoặc mặc định `isActive=true` khi thiếu. Test nhiều group role 140, group role khác, inactive, trùng employeeID, accountID khác employeeID, không có group, và lỗi một request group. Một request lỗi phải trả lỗi catalog, không trả danh sách thiếu như thể đầy đủ. Giữ credential/division context hiện hành; không hardcode groupID=10 hoặc departmentID=455.
- [ ] Lấy mẫu định nghĩa form gồm text, number, single-choice và multi-choice có mặc định; ghi rõ thuộc tính nguồn nào biểu diễn mặc định. Không dùng giá trị hồ sơ đã lưu làm default. Ghi cách nhận diện section kết luận (map có ItemGroupID null nên không đoán null là group ID).
- [ ] Thêm test adapter hiện hữu trong `SignServerPdfSignerTests`: request cert A/name B/display time nhập; assert multipart `empCode=A`, `name=B`, `date=thời gian nhập`, không chứa `filePath`. Dùng fake HTTP handler có sẵn; tuyệt đối không ghi PIN/certificate vào log.
- [ ] Ghi acceptance gateway staging: output PDF hiện B, certificate subject vẫn đúng chủ thể cấu hình A, audit giữ A; nếu gateway ép cùng tên thì dừng bật tính năng và xin phương án tích hợp, không đổi cert thành B.
- [ ] Run `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter FullyQualifiedName~SignServerPdfSignerTests`. Ghi hợp đồng và fixtures; commit `docs: pin clinical doctor and reset integration contracts`. Không tuyên bố staging đạt nếu chỉ test fake.

## Task 2: Metadata trước ký, danh sách B và lưu có quyền — PROJ2368/2374

**Files:**
- Modify `HealthExam.Domain/ExamRecords/ExamRecordSignStep.cs`, `HealthExam.Infrastructure/Persistence/HealthExamDbContext.cs`, migration model snapshot.
- Modify `HealthExam.Application/His/HisModels.cs`, `SaveHisFormSection.cs`, `GetHisRecordSection.cs`, `HealthExam.Application/Signing/SigningModels.cs`.
- Modify `HealthExam.API/Controllers/ExamRecordHisFormController.cs`, `CatalogController.cs`, `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`.
- Modify `HealthExam.Application/Integrations/IHisEmrClient.cs`, `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs` theo contract Task 1; Create `HealthExam.Application/His/ListClinicalDoctors.cs` cho handler list.
- Migration mới logical name `AddClinicalExecutionMetadata` trong `HealthExam.Infrastructure/Persistence/Migrations/` (timestamp do tooling sinh).
- Test `HealthExam.Tests/Infrastructure/ExamRecordSignStepPersistenceTests.cs`, `HealthExam.Tests/HisFormEndpointTests.cs`, `HealthExam.Tests/Signing/SectionSigningProgressTests.cs`.

**Interfaces:**
- `HisFormSectionSaveRequest` thêm cuối `long? ConfirmingDoctorId = null, DateTimeOffset? PerformedAt = null`; B là tùy chọn thực sự: null/không gửi không gây lỗi lưu.
- `SaveHisFormSectionCommand` thêm cuối `string ActorName = ""`; controller điền từ context đáng tin, không từ body. Chỉ require metadata cho clinical/conclusion target; không làm hỏng section đăng ký không liên quan.
- `HisRecordSectionResult` thêm cuối `SectionExecutionInfo? Execution = null` và bốn flag bool mặc định false: `CanEdit`, `CanDelete`, `CanSign`, `CanCancelSign`; actor ID/kind thêm tùy chọn ở query GET và được controller truyền, không nhận từ query string. `CanSign` còn xét vai trò A từ nguồn tin cậy; UI không suy quyền ký chỉ từ owner.
- `IHisEmrClient.ListClinicalDoctorsAsync(HisCallContext context, CancellationToken ct = default)` trả `Task<HisClientResult<IReadOnlyList<ClinicalDoctor>>>`; adapter dùng route đã xác minh Task 1. List handler/API và save-validation dùng cùng nguồn, không có interface catalog thứ hai.

- [ ] Thêm test persistence round-trip metadata bằng fixture hiện có; ban đầu build đỏ vì thiếu cột/property. Thêm nullable để dữ liệu cũ đọc được:

```csharp
public long? PerformedByEmployeeID { get; set; }
public string PerformedByEmployeeName { get; set; } = "";
public long? ConfirmingDoctorID { get; set; }
public string ConfirmingDoctorCode { get; set; } = "";
public string ConfirmingDoctorName { get; set; } = "";
public DateTime? PerformedAtUtc { get; set; }
```

- [ ] Assert DB reload giữ owner 1001 và thời gian nhập khi B null, tên/mã B trống; thêm case có B 2002 giữ đúng tên. Cả hai case `SignedAt == null`, `Status == InProgress`. Không lấy CreatedDate của hồ sơ để backfill owner. Migration nullable/default rỗng, không cập nhật dữ liệu cũ không có provenance.
- [ ] Sau impact, mở transaction + lock record trước đọc/kiểm trạng thái trong `SaveHisFormSectionHandler`; dùng cùng lock order như signing. Khi snapshot có owner khác hoặc legacy snapshot thiếu owner, trả Forbidden/InvalidState trước HIS write. Snapshot mới gán A một lần; sửa không đổi A.
- [ ] Khi lưu, chỉ resolve/validate B nếu có ID; không gọi catalog bác sĩ khi B null, không chặn lưu vì chưa chọn B. Nếu gửi ID không hợp lệ thì BadRequest. Giữ kiểm tra thời gian hiện hành; client gửi thời gian hiện tại khi bắt đầu thêm. Không tự thêm giới hạn tuổi thời gian. Null ở request save được hiểu là chưa chọn/xóa lựa chọn B của form chưa ký.
- [ ] Sau HIS save thành công, cập nhật metadata và `InProgress` cùng commit. Không gán `SignedBy*` khi chỉ lưu. Với conclusion, tạo InProgress snapshot của bước conclusion khi lưu section đã được Task 1 xác định, để ký không thể tự tạo “đã lưu” giả.

```csharp
snapshot.PerformedByEmployeeID ??= actorId;
snapshot.PerformedByEmployeeName = ownerName; // tên đã lưu của A, không thay bằng B
snapshot.ConfirmingDoctorID = doctor?.EmployeeId;
snapshot.ConfirmingDoctorCode = doctor?.EmployeeCode ?? "";
snapshot.ConfirmingDoctorName = doctor?.EmployeeName ?? "";
snapshot.PerformedAtUtc = request.PerformedAt.Value.UtcDateTime;
```

- [ ] GET và response save map metadata từ snapshot hiện hành (đúng VariantCode), không mất sau reload. Flags tính từ owner/status/record lock; không mở quyền legacy owner-null. Tách trạng thái “chưa khám” (không snapshot) khỏi đang nhập mà chưa lưu ở UI. Gửi actor context khi save gọi GET nội bộ để flags trong response save không mặc định false sai.
- [ ] Endpoint tests: A lưu/sửa không B → GET B null, vẫn InProgress và owner A; có B → GET vẫn đúng; C update bị 403 và HIS save count không tăng; B invalid không gọi HIS save; field thuộc group khác bị BadRequest; thay B giữ A; Signed kết luận chặn trước I/O; DB save failure không trả success. Giữ test snapshot từ variant khác không gây khóa giả.
- [ ] Run `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter 'FullyQualifiedName~ExamRecordSignStepPersistence|FullyQualifiedName~HisFormEndpoint|FullyQualifiedName~SectionSigningProgress'`; migration test DB riêng; graph check rồi commit `feat: persist clinical execution owner and displayed doctor`.

## Task 3: Quyền ký và tên/thời gian PDF — PROJ2370/2371/2374

**Files:** Modify `HealthExam.Application/Signing/SignExamSection.cs`, `SigningModels.cs`, `HealthExam.Application/Paraclinical/SignConclusion.cs`, `ParaclinicalModels.cs`, `GetConclusionEligibility.cs`; test `HealthExam.Tests/Signing/SignExamSectionTests.cs`, `ConclusionSigningTests.cs`, `SectionSignConcurrencyTests.cs`, `SignExamSectionEndpointTests.cs`, `SectionLockTests.cs`.

**Interfaces:** Command ký vẫn lấy A từ auth. `SignedByEmployeeID/Code/Name` tiếp tục là actor/chứng thư A; PerformedAt lấy từ metadata đã lưu Task 2. Thêm `long? ConfirmingDoctorId = null` vào command ký và request body tùy chọn của cả ký danh mục/kết luận. B lấy từ request ký nếu có, nếu không dùng B đã lưu; backend resolve ID/code/name từ catalog và kiểm tra lại trước ký. Body rỗng vẫn được nhận để tương thích, nhưng khi cả request lẫn snapshot thiếu B thì trả BadRequest. Results thêm metadata qua `SectionExecutionInfo`.

### Điều chỉnh mới: chọn bác sĩ tại bước ký (chưa thực hiện)

Các checkbox đã hoàn tất bên dưới là lịch sử trước điều chỉnh này, không chứng minh yêu cầu mới đã được triển khai/test.

- [ ] Sửa contract/controller ký tại `HealthExam.API/Controllers/ExamRecordController.cs` và các request tương ứng trong `HealthExam.API/Contracts/HisEmrModels.cs`; cho phép body rỗng, không phát sinh 415 ngoài ý muốn. Không nhận tên/code B hay actor A do client tự khai làm nguồn tin cậy.
- [ ] Test lưu thiếu B xanh; ký thiếu B đỏ với BadRequest, chưa lookup certificate/call signer, chưa đổi snapshot; ID B không hợp lệ cũng không side effect. Chạy cả danh mục và kết luận.
- [ ] Sau owner/state guard, resolve `command.ConfirmingDoctorId ?? snapshot.ConfirmingDoctorID`; thiếu thì báo “Vui lòng chọn bác sĩ trước khi ký”. Validate ID qua catalog trước khi gán metadata và ký; lưu B cùng transaction ký thành công. Với kết luận, chỉ cập nhật B của bước kết luận, không thay B của bước lâm sàng.
- [ ] `CanSign` cho phép A mở bước chọn bác sĩ khi các điều kiện còn lại đạt, không disable chỉ vì B đang trống. API ký vẫn kiểm tra B đầy đủ. Retry trên snapshot đã Signed không được đổi B hoặc SignedAt; nếu gửi B khác snapshot đã chốt thì trả InvalidState và yêu cầu hủy trước.
- [ ] Bổ sung fixture luồng `save(B=null) → sign(B=2002) → reload → cancel`; assert owner vẫn A, PDF dùng B, chứng thư/audit vẫn A. Giữ test B đã lưu được kiểm tra lại nếu request ký không gửi B.
- [ ] Chạy `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter 'FullyQualifiedName~HisFormEndpoint|FullyQualifiedName~SignExamSection|FullyQualifiedName~Conclusion'`; cập nhật API docs và UI chọn B trong modal ký. Không đánh dấu hoàn tất dựa vào checkbox cũ.

- [x] Chỉnh fixture hiện có để seed lưu InProgress hợp lệ trước ký; giữ riêng test “không có snapshot lưu thì ký thất bại”. Không tự tạo snapshot trống trong handler để hợp thức hóa input thiếu.
- [x] Thêm test vào `SignExamSectionTests` sau khi fixture đã seed owner theo Task 2:
- [x] Run red, rồi thêm owner/state guard trong lock trước certificate lookup; bỏ tạo snapshot mới khi chưa lưu. Giữ role guard của A và cert A. Repeated sign của cùng A trên Signed trả trạng thái cũ hoặc lỗi InvalidState theo contract đã công bố, không đổi SignedAt hay ký lại; chọn idempotent response cũ cho retry.
- [x] Đường `SignConclusion` giữ lookup certificate bằng `snap.SignedByEmployeeCode`; thay tên/giờ hiển thị bằng dữ liệu B/thực hiện. Không dùng `.ToLocalTime()` phụ thuộc host:
- [x] Cập nhật `ConclusionSignStepResult` để hiển thị đầy đủ thông tin người thực hiện (`PerformedByEmployeeID`, `PerformedByEmployeeName`), bác sĩ xác nhận chuyên môn (`ConfirmingDoctorID`, `ConfirmingDoctorName`), và thời gian thực hiện (`PerformedAtUtc` quy đổi UTC+7) ở từng bước trong danh sách bước ký (`conclusion-eligibility` và `conclusion/sign`), giúp người dùng nhận biết rõ ai đã nhập và ai xác nhận ở từng bước ký.
- [x] Trong `ConclusionSigningTests`, seed B cho từng step và assert `Signer.Requests[i].Certificate.EmployeeCode == mã A`, `EmployeeName == tên B`, `DisplayDate == thời gian nhập VN`, snapshot `SignedAt` vẫn thời điểm thao tác. Thêm trường hợp người kết luận khác A của section để đảm bảo cert không bị thay bằng cert kết luận.
- [x] Run `TZ=UTC dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter 'FullyQualifiedName~Signing'`, lặp lại Asia/Ho_Chi_Minh. Sửa test cũ kỳ vọng `ToLocalTime` theo contract mới, không skip. Graph check rồi commit `fix: separate clinical signing actor from PDF display doctor`.

## Task 4: Reset dùng chung, xóa và hủy ký — PROJ2369/2372

**Files:** Create `HealthExam.Application/His/ResetHisFormSection.cs`, `HealthExam.Application/His/HisSectionResetValues.cs`, `HealthExam.Tests/Signing/ClinicalSectionResetTests.cs`; modify `CancelExamSectionSign.cs`, `SigningModels.cs`, `ExamRecordHisFormController.cs`, `ExamRecordController.cs`, `ApplicationServiceExtensions.cs`, `ExamRecordSignStep.cs`; test `SectionLockTests.cs`, `SectionSignConcurrencyTests.cs`.

**Interfaces:**
- `ResetHisFormSectionCommand(string DivisionId, Guid RecordId, int ItemGroupId, string Credential, string TraceId, long ActorId, ActorKind ActorKind)`.
- `ResetHisFormSectionHandler.HandleAsync(command, ct)` trả `Task<ApplicationResult<HisRecordSectionResult>>` cho xóa chưa ký. `CancelExamSectionSignHandler` gọi chung reset writer sau khi xác minh signed; thêm credential/trace từ context vào command hủy, không nhận client credentials tùy ý.
- New route `DELETE v1/exam-records/{recordId}/his-form/sections/{itemGroupId}`; giữ route hủy ký hiện hữu và bool response để tương thích, frontend reload GET sau success.
- Normalizer nhỏ `HisSectionResetValues.Build(IReadOnlyList<HisFormSectionFieldValue> defaults, IReadOnlyList<Guid> sectionItemIds)` trả danh sách giá trị reset của đúng group. `defaults` được adapter Task 1 trích từ định nghĩa mới, không từ hồ sơ hiện tại.

- [x] Thêm test không phụ thuộc HIS cho normalizer:
- [x] Run red `--filter FullyQualifiedName~ClinicalSectionResetTests`; implementation lõi:
- [x] Tách phần đọc/merge/ghi biểu mẫu đang có ở `SaveHisFormSection` thành helper nội bộ dùng chung với reset, chỉ khi cần tránh hai bản ghi toàn biểu mẫu khác nhau. Đường đọc HIS lỗi phải fail closed, không tiếp tục với dictionary rỗng rồi ghi mất nhóm khác. Không gọi save handler từ reset nếu nó tái tạo InProgress hoặc tự commit sai ranh giới.
- [x] Chống mất đồng bộ bằng trạng thái nội bộ `ResetPending` trên snapshot: transaction ngắn lock/validate owner + trạng thái → ghi audit ý định và ResetPending → commit; transaction mới lock/reload → gọi HIS reset idempotent → xóa snapshot/ghi audit hoàn tất → commit. Guard mọi save/sign/conclusion từ chối khi có ResetPending. Response pending không được hiển thị Chưa khám; trả InvalidState kèm thông báo cần thử lại hủy.
- [x] Nếu HIS/DB thất bại, giữ pending và owner; cùng A có thể retry reset theo đúng mục tiêu, C không được tiếp quản. Đây là state phục hồi cho I/O không atomic, không thêm queue/scheduler. Audit trước xóa chứa A, B, thời gian nhập, SignedAt, target và thời điểm reset; không chứa giá trị lâm sàng nhạy cảm không cần thiết.
- [x] Handler xóa chỉ chấp nhận InProgress; handler hủy ký chỉ Signed hoặc ResetPending của chính thao tác tương ứng. Hồ sơ ký kết luận không cho reset section; đúng Division/Variant/group. Delete/hủy chỉ xóa snapshot sau HIS thành công, GET trả mặc định và metadata rỗng, lịch sử audit giữ nguyên.
- [x] Test handler với fake HIS có sẵn: A success; C/owner-null denied trước HIS write; template/read/save thất bại không xóa snapshot; HIS thành công nhưng DB lỗi giữ pending và retry được; group khác không thay đổi; đồng thời sign/save/reset không mất update. Test real DB row-lock ở `SectionSignConcurrencyTests`, không dùng InMemory để chứng minh khóa.
- [x] Run `--filter 'FullyQualifiedName~ClinicalSectionReset|FullyQualifiedName~SectionLock|FullyQualifiedName~SectionSignConcurrency'`; graph check và commit `fix: reset owned clinical sections safely on delete and cancel`.

## Task 5: Kết luận signed/cancel và khóa toàn hồ sơ — PROJ2373

**Files:** Modify `HealthExam.Application/Paraclinical/SignConclusion.cs`, `GetConclusionEligibility.cs`, `ParaclinicalModels.cs`, `HealthExam.Domain/ExamRecords/ExamRecord.cs`, `HealthExam.API/Controllers/ExamRecordController.cs`, `HealthExam.API/Extensions/ApplicationServiceExtensions.cs`; create `HealthExam.Application/Paraclinical/CancelConclusionSign.cs`, `HealthExam.Tests/Signing/CancelConclusionSignTests.cs`; test `ConclusionSigningTests.cs`, `ConclusionEndpointTests.cs`, `ConclusionEligibilityFromMapTests.cs`, `ConclusionSignCancellationTests.cs`. Inspect tất cả write handlers của CLS qua graph trước bổ sung guard tại điểm ghi chung.

**Interfaces:**
- `CancelConclusionSignCommand(string DivisionId, Guid RecordId, long ActorId, ActorKind ActorKind, string Credential, string TraceId)`; handler trả `Task<ApplicationResult<ConclusionSignResult>>`.
- New POST `v1/exam-records/{recordId}/conclusion/sign/cancel`; API ký hiện hữu không đổi route.
- `ConclusionSignResult` thêm `ExamRecordState RecordState`; eligibility thêm `bool IsSigned`, `bool CanCancelSign`, `bool IsReadOnly` để UI không suy ra đã ký từ `CanSignConclusion=false`. Actor đọc lấy từ context; quyền hủy kiểm tra lại ở handler.
- Chủ sở hữu conclusion lấy từ lần lưu conclusion Task 2; hủy kiểm tra A đó và actor đã ký, không kiểm tên B. Không áp chính sách “mọi nhân viên được hủy”.

- [x] Thêm vào `ConclusionSigningTests` (fixture SignAllSections/SignConclusion được Task 2–3 cập nhật có lưu metadata và conclusion hợp lệ):
- [x] Run red `--filter Signed_conclusion_completes_record_after_reload`; guard conclusion đã lưu + đúng owner, đủ lâm sàng/CLS và không ResetPending trước render; không tạo snapshot conclusion mới nếu chưa lưu. Không sửa test bằng cách bỏ yêu cầu đã lưu.
- [x] Khi upload thành công, gán cùng transaction final save:
- [x] Implement cancel với reset protocol Task 4 cho **chỉ group kết luận** đã xác minh Task 1; snapshot conclusion chuyển ResetPending, record vẫn signed/locked cho đến reset thành công. Audit giữ đường dẫn file cũ và thông tin A/B trước clear.
- [x] Test cancel A success/reload, C denied trước I/O, unsigned invalid state, legacy owner-null denied, reset failure vẫn locked, retry pending thành công; signed conclusion chỉ mở CanCancelSign đúng người, CanSignConclusion false nhưng IsSigned true. Sau cancel phải lưu lại kết luận trước khi ký tiếp.
- [x] Kiểm tra ghi trực tiếp vào mọi đường lâm sàng/CLS (tạo/sửa/hủy chỉ định, nhập/import/xóa kết quả và webhook liên quan) bằng graph + source. Chặn thao tác app khi signed/reset pending tại điểm kiểm tra record chung; với kết quả tự động bên ngoài không được discard dữ liệu silently, giữ cơ chế lỗi/retry hiện có. Ghi các caller đã kiểm vào acceptance doc; không tuyên bố khóa toàn CLS nếu chỉ disable UI.
- [x] Run `dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter 'FullyQualifiedName~Conclusion|FullyQualifiedName~SectionLock|FullyQualifiedName~Paraclinical'`; graph check và commit `fix: synchronize conclusion signing state and cancellation`.

## Task 6: Hợp đồng frontend và gate phát hành

**Files:** Update `docs/api/clinical-execution-contract.md`, `docs/api/his-section-signing-api.md`, `docs/api/conclusion-pdf-signing-guide.md`; create/update `docs/testing/2026-09-19-bugs-acceptance.md` theo kế hoạch tổng.

- [ ] Cập nhật JSON request save: `confirmingDoctorId` tùy chọn/null, `performedAt` có offset + fields; request ký nhận `confirmingDoctorId` và bắt buộc resolve được B trước ký. GET chưa chọn trả B null/tên trống. Không gửi A qua body. Ghi error Forbidden/BadRequest/InvalidState cho từng hành động, đồng bộ câu cảnh báo với UI.
- [x] Ghi tiêu chí UI: thêm có giờ hiện tại; sửa giữ values; metadata hiện ngay sau save; ký hiện B trên PDF nhưng audit A; xóa/hủy giữ mặc định; pending khóa và cho retry phù hợp; signed conclusion hiển thị nút hủy, không nhãn thiếu điều kiện; reload đồng nhất.
- [x] Chạy test A/B/C và thời gian ở hai TZ, migration cũ/mới, provider thật, gateway staging có phép. Báo rõ phụ tá A cần cấu hình quyền/chứng thư gì theo kết quả, không tự tạo tài khoản hay cấp quyền.
- [x] Khi chưa có route danh sách, schema defaults/conclusion group, frontend hoặc phép thử gateway: bàn giao phần đã đạt, ghi chính xác gate chưa đạt. Không đánh dấu nhóm hoàn tất chỉ với fake tests.
- [x] Graph check + review docs, commit `docs: publish clinical execution and bug acceptance contracts`.
