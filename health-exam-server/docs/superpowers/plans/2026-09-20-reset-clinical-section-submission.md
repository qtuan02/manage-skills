# Reset Clinical Section Submission Implementation Plan

**Goal:** Xóa dữ liệu của đúng một danh mục khám trong submission HIS, giữ nguyên các danh mục khác, trả danh mục về default/“Chưa khám”, và bảo vệ quyền theo người thực hiện.

**Architecture:** Backend đọc definition layout để lập tập `ItemID` thuộc `ItemGroupID`, đọc submission hiện tại qua `REMR`, reset chỉ các detail trong tập đó rồi ghi lại cùng `EMRDataID` qua `CUEMR`. Chỉ sau khi HIS lưu thành công backend mới xóa `ExamRecordSignStep`; frontend gọi DELETE và refetch section.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core, HIS EMR adapter, React, TanStack Query, TypeScript.

## Global Constraints

- Không gọi API xóa toàn bộ submission và không tạo `EMRDataID` mới.
- Không thay đổi dữ liệu của `ItemID` ngoài `ItemGroupID` được yêu cầu.
- Người khác `PerformedByEmployeeID` nhận lỗi `403` và không gọi HIS write.
- Hủy ký và Xóa dùng chung quy tắc owner; người xác nhận ký có thể khác người thực hiện.

### Task 1: Chốt contract và default mapping

**Files:**
- Modify: `HealthExam.Application/His/ResetHisFormSection.cs`
- Modify: `HealthExam.Application/His/HisModels.cs`
- Modify: `HealthExam.Application/Integrations/IHisEmrClient.cs` (chỉ nếu cần thêm operation riêng; ưu tiên dùng `ReadFormData`/`SaveFormData`)

- [ ] Xác định các property default trong `LayoutJson` bằng fixture/response HIS thật: ưu tiên `DefaultValue`, `Default`, `Value`, lựa chọn mặc định của `Choices`/`Options`; ghi rõ thứ tự ưu tiên trong helper.
- [ ] Tạo value object nội bộ cho detail: `ItemID`, `Value`, `Text`, `DataType`, `ControlStyle` để giữ nguyên metadata khi reset.
- [ ] Tạo helper `HisSectionResetValues.Build(layout, existingDetails, itemGroupId)` trả toàn bộ details; chỉ thay value/text của field thuộc group, giữ nguyên field ngoài group.
- [ ] Với field có default, ghi default; không có default thì ghi `Value = ""`, `Text = ""`; không loại bỏ detail khỏi submission.

### Task 2: Implement reset transaction backend

**Files:**
- Modify: `HealthExam.Application/His/ResetHisFormSection.cs`
- Modify: `HealthExam.Api/Controllers/ExamRecordHisFormController.cs`
- Modify: `HealthExam.Api/Extensions/ApplicationServiceExtensions.cs`

- [ ] Lock/read `ExamRecord` theo division và record ID; reject hồ sơ đã ký kết luận.
- [ ] Tìm `SignStep` theo `VariantCode + ItemGroupID`; nếu có `PerformedByEmployeeID` khác actor, trả `Forbidden` trước mọi HIS call.
- [ ] Reject bước đã ký nếu contract Xóa chỉ cho phép xóa `InProgress`; hủy ký dùng đường riêng nhưng gọi cùng writer reset.
- [ ] Đảm bảo có `AdmissionID`, `HisEmrDataID`, template definition; nếu thiếu trả lỗi rõ ràng.
- [ ] Đọc `REMR`, parse toàn bộ `Details`, parse `LayoutJson`, gọi helper reset đúng group.
- [ ] Gửi `CUEMR` với cùng `EMRDataID`, `TemplateID`, `AdmissionID`, patient metadata và details đã merge.
- [ ] Nếu HIS read/save thất bại: không xóa `SignStep`, giữ `PerformedBy`, trả lỗi; không cập nhật trạng thái local thành “Chưa khám”.
- [ ] Nếu HIS save thành công: xóa `SignStep`, commit local transaction, audit actor/group; GET sau đó suy ra “Chưa khám”.

### Task 3: Hủy ký dùng reset writer

**Files:**
- Modify: `HealthExam.Application/Signing/CancelExamSectionSign.cs`
- Modify: `HealthExam.Application/Signing/SigningModels.cs`
- Modify: `HealthExam.Api/Controllers/ExamRecordHisFormController.cs`

- [ ] Truyền credential/trace/division/actor đầy đủ từ controller vào cancel command.
- [ ] Kiểm tra owner `PerformedByEmployeeID` trước reset.
- [ ] Không chỉ đổi status hoặc xóa local row; gọi reset writer để dữ liệu HIS của group cũng về default.
- [ ] Chỉ clear `SignedBy*`/`SignedAt` và giữ `PerformedBy*` nếu reset HIS thành công.

### Task 4: Frontend DELETE và dữ liệu form

**Files:**
- Modify: `/Users/nguyenhoanghai1502/MedViet/Frontend/turbo-web/packages/api/src/health-exam/health-exam-his-form-service.ts`
- Modify: `/Users/nguyenhoanghai1502/MedViet/Frontend/turbo-web/apps/health-exam/src/hooks/api/health-exam-his-form.ts`
- Modify: `/Users/nguyenhoanghai1502/MedViet/Frontend/turbo-web/apps/health-exam/src/features/results/components/clinical-tab/clinical-tab.tsx`

- [ ] Giữ DELETE chỉ là xóa data của section, không xử lý như xóa template/form.
- [ ] Không ghi response boolean vào cache `HisFormSection`; sau success invalidate/refetch section.
- [ ] Giữ nguyên selected category và mount lại `CategoryPanel`; không gọi `setPanel({ mode: null })` theo cách làm mất vùng form.
- [ ] Hiển thị section với default values từ GET mới, status “Chưa khám”, `PerformedBy` rỗng.
- [ ] Với lỗi 403, hiển thị nguyên văn “Không thể xóa dữ liệu của người khác”; không clear form/cache.

### Task 5: Verification

- [ ] Backend test: A lưu → DELETE thành công; details của group reset, group khác giữ nguyên.
- [ ] Backend test: C DELETE → 403, fake HIS không nhận write request.
- [ ] Backend test: HIS save lỗi → sign step và owner vẫn còn.
- [ ] Backend test: hủy ký thành công reset HIS và giữ `PerformedBy`.
- [ ] Frontend test: DELETE success refetch, panel vẫn hiển thị form; không cache boolean.
- [ ] Chạy `dotnet build HealthExam.Api/HealthExam.Api.csproj --no-restore`.
- [ ] Chạy `bun run typecheck` tại `apps/health-exam`.
- [ ] Chạy GitNexus `detect_changes --scope all` trước khi commit.

## Open Questions

- Cần một response thực tế của HIS `LayoutJson` để xác nhận chính xác tên property default và cách biểu diễn default choice.
- Cần xác nhận `CUEMR` có yêu cầu gửi cả detail không thuộc group (hiện code đang gửi full merged details; kế hoạch giữ nguyên cách này).
