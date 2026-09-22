# September 19 Bugs Implementation Plan

> **Cập nhật phạm vi sau đồng bộ main:** giữ các thay đổi patient/search (kế hoạch 01/02); signing dùng nguyên `origin/main` tại `443b266`. Kế hoạch 03 cũ chỉ là lịch sử, không tiếp tục áp lên signing mới. Các quy tắc signing cũ bên dưới không ghi đè hợp đồng trên main.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Xử lý PROJ2363–PROJ2374 theo thiết kế đã được người dùng xác nhận, gồm backend và hợp đồng nghiệm thu frontend.

**Architecture:** Giữ các handler/repository hiện tại; bổ sung metadata trên snapshot bước khám thay vì dựng workflow mới. Chia thành ba kế hoạch độc lập về bàn giao; kết luận đi sau metadata/quyền/reset.

**Tech Stack:** C#/.NET 8, ASP.NET Core, EF Core, PostgreSQL, xUnit, HIS, sign-server, MinIO; không thêm dependency.

**Spec:** `docs/superpowers/specs/2026-09-19-bugs-report-design.md` — người dùng đã duyệt bản tài liệu trong hội thoại.

## Global Constraints

- Điều chỉnh signing: lưu/sửa form không bắt buộc B; chỉ ký danh mục/kết luận mới yêu cầu bác sĩ B hợp lệ. Có thể chọn B ngay ở bước ký; quyền sở hữu A không đổi.
- “Không viết lại hệ thống ký, không thêm hệ thống phân công phụ tá–bác sĩ.”
- “Danh tính A phải lấy từ ngữ cảnh xác thực, không tin giá trị client gửi.”
- “Việc đổi B không làm đổi chủ sở hữu A.”
- “Không tự lấy chứng thư B chỉ vì A chọn tên B.”
- “Không backfill giả A/B hoặc thời gian ký cho dữ liệu cũ.”
- “Không chạy migration, ghi dữ liệu thật, gọi ký thật hoặc deploy trong giai đoạn thiết kế/kế hoạch.”
- Không commit thay đổi người dùng chưa xác nhận. Workspace đang có 7 file C# sửa và migration địa chỉ chưa commit; chúng không phải sản phẩm của bước lập kế hoạch.
- Chỉ sửa symbol sau GitNexus impact upstream và tìm các caller liên quan; HIGH/CRITICAL phải báo, UNKNOWN phải xác minh bằng source. Detect_changes `scope: all` trước mỗi commit; partial/truncated không đạt.
- Actor A vẫn cần quyền thao tác app hiện hành; chọn B không cấp thêm quyền app hoặc chứng thư. Không mặc định gỡ kiểm tra vai trò/chứng thư để phụ tá ký được.

## Bản đồ kế hoạch / thứ tự

| Kế hoạch | Ticket | Đầu ra |
| --- | --- | --- |
| [01 Patient](2026-09-19-bugs-patient.md) | 2363–2364 | Profile round-trip, đối chiếu đúng trường và migration địa chỉ |
| [02 Search](2026-09-19-bugs-search.md) | 2365–2367 | Lọc ngày VN và chuỗi con |
| [03 Clinical/signing](2026-09-19-bugs-clinical-signing.md) | 2368–2374 | Metadata A/B, quyền, reset, PDF và kết luận |

Thực hiện 01 → 02 → 03 để có các đợt nghiệm thu nhỏ. 01/02 không phụ thuộc 03. Trong 03: xác minh tích hợp → metadata/lưu → quyền/ký → reset → kết luận → nghiệm thu end-to-end. Không chạy nhiều worker cùng sửa `HealthExamDbContext` hoặc migration snapshot.

## Task 0: Baseline và bảo toàn công việc đang có

**Files:** Read `git diff`, spec, `HealthExam.Tests/HealthExam.Tests.csproj`, `.gitnexus/run.cjs`; không sửa production.

**Interfaces:** Không đổi API; tạo bằng chứng baseline cho mọi kế hoạch con.

- [ ] Ghi lại branch, HEAD và danh sách file đang sửa; nếu dùng worktree, chuyển phần thay đổi địa chỉ vào baseline bằng patch được chủ sở hữu chấp thuận, không bỏ mất chúng khi tạo worktree từ HEAD.

```bash
git status --short
git diff --stat
git log -1 --oneline
dotnet --version
```

- [ ] Chạy baseline offline/mocked, ghi số pass/fail và test không thực sự chạy PostgreSQL.

```bash
dotnet test HealthExam.Tests/HealthExam.Tests.csproj --filter 'FullyQualifiedName~Patient|FullyQualifiedName~ExamRecordListFilter|FullyQualifiedName~Signing|FullyQualifiedName~HisFormEndpoint' --no-restore
```

Nếu thiếu restore assets, chạy restore theo quy trình repo rồi chạy lại; không thay package để làm baseline xanh. `HEALTHEXAM_TEST_DB` (fallback `HEX_PG_TESTS`) chỉ được trỏ tới DB test riêng: fixture có tạo/xóa tài nguyên. Không in connection string. Test PostgreSQL hiện có thể return sớm khi không cấu hình; PASS khi đó không phải bằng chứng đã kiểm provider thật.

- [ ] Làm mới graph trước impact tại thời điểm thực thi. Lần lập kế hoạch đã chạy `node .gitnexus/run.cjs analyze --index-only` thành công nhưng có cảnh báo C# callable-resolution; MCP còn báo cache chậm 1 commit docs. Không coi graph là đủ để chứng minh không có caller động.
- [ ] Với mỗi task code, thêm test đỏ → chạy đúng test → sửa tối thiểu → chạy xanh → kiểm thử nhóm → detect_changes → kiểm tra staged paths → commit riêng task. Không `git add .`.

## Điểm xác minh bắt buộc, không được đoán

1. **Ngày hiển thị:** repository lọc `CreatedDate`, DTO còn có `ExamDate` từ đợt khám. Plan 02 yêu cầu đối chiếu binding của hai màn hình trước nghiệm thu; không đổi ngày lịch sử hoặc đổi sang ngày đợt chỉ để test xanh.
2. **Danh sách B:** người dùng đã cung cấp nguồn: `/api/SAMF00070/GetListGroup` → group `RoleID == 140` → `/api/SAMF00050/GetListAccInGroup?groupID={groupID}`. Dùng employeeID, không accountID; lấy mọi group phù hợp, lọc active và loại trùng. Plan 03 còn cần kiểm chứng envelope/phân trang và tích hợp adapter; không tin tên B do client gửi.
3. **Chứng thư:** adapter gửi riêng `empCode` và `name`, nhưng chưa chứng minh gateway thật cho phép render khác tên. Giữ gate nghiệm thu trên môi trường thử nghiệm được cho phép.
4. **Hủy kết luận/reset:** chưa thấy API hủy kết luận trong controller hiện tại; không nhầm test `ConclusionSignCancellationTests` (hủy cancellation token) với nghiệp vụ hủy chữ ký. Phải xây dựng nghiệp vụ hủy theo plan 03.

## Task cuối: Nghiệm thu và bàn giao

**Files:** Create `docs/testing/2026-09-19-bugs-acceptance.md`; cập nhật các API docs được chỉ ra trong kế hoạch con.

- [ ] Ghi từng ticket với test tự động, thao tác UI, kết quả mong đợi, kết quả thực chạy và link bằng chứng; chưa chạy thì ghi rõ chưa nghiệm thu.
- [ ] Chạy toàn bộ suite trên timezone UTC và Asia/Ho_Chi_Minh. Các test cũ dùng `TzGuard` phải được thay bằng kiểm tra timezone tường minh khi sửa đường PDF, không skip để đạt xanh.

```bash
TZ=UTC dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore
TZ=Asia/Ho_Chi_Minh dotnet test HealthExam.Tests/HealthExam.Tests.csproj --no-restore
git diff --check
```

- [ ] Xác minh migration trên DB test cũ/mới; không áp dụng production. Test mất kết nối, lỗi HIS, lỗi signer/upload và concurrent save/sign/reset.
- [ ] Frontend: chọn NB cũ; đổi tỉnh/phường; tìm mốc nửa đêm và chuỗi giữa; A chọn B → lưu → sửa → ký → hủy; C bị chặn; ký/hủy kết luận và reload. Khi không có repo frontend, bàn giao hợp đồng + checklist, giữ trạng thái UI chưa nghiệm thu.
- [ ] Chỉ đánh dấu hoàn tất khi cả dữ liệu, quyền, API, PDF và UI tương ứng đạt; báo riêng dependency ngoài repo chưa được kiểm chứng.

## Tự rà soát kế hoạch

Coverage: 2363–2364 → 01; 2365/2367 → 02 Task 2; 2366 → 02 Task 1; 2368/2374 → 03 Tasks 1–3; 2369–2372 → 03 Tasks 3–4; 2373 → 03 Task 5. Lỗi tích hợp, dữ liệu cũ, concurrency, migration và UI có gate ở 03 và task cuối. Đây là kế hoạch, chưa phải báo cáo test đã chạy hoặc bugs đã sửa.
