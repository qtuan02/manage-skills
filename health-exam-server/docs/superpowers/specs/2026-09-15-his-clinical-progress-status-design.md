# HIS Clinical Examination Progress Status

## 1. Mục tiêu

Cho frontend biết người bệnh đang ở đâu trong quy trình khám lâm sàng trên HIS.

Luồng hiển thị cần hỗ trợ:

- Chưa hoàn tất phòng lâm sàng nào: `Chờ khám`.
- Đã hoàn tất phòng lâm sàng đầu tiên nhưng còn phòng khác: `Đang khám`.
- Có thể xác định phòng lâm sàng hiện tại tiếp theo.
- Không dùng trạng thái ký HIS làm thẳng trạng thái hồ sơ nội bộ nếu chưa đủ điều kiện.

## 2. Phạm vi

### Trong phạm vi

- Đọc tiến độ trực tiếp từ HIS qua `M02F01500/RMedicalProcessByID`.
- Thêm endpoint tiến độ cho một hồ sơ.
- Trả về trạng thái tổng hợp và phòng lâm sàng hiện tại cho FE.
- Dùng `M02_MedicalProcessDetail.SignStatus` làm nguồn trạng thái phòng.

### Ngoài phạm vi

- Không sửa trạng thái ký hoặc dữ liệu form trên HIS.
- Không gọi HIS cho từng dòng trong endpoint danh sách hồ sơ.
- Không thay đổi `ExamRecord.State` trong phase đầu; trạng thái đó vẫn là trạng thái hồ sơ nội bộ.
- Không suy đoán trạng thái từ `ProgressDone/ProgressTotal` của form-server.

## 3. Nguồn dữ liệu và định nghĩa

HIS là nguồn chuẩn. `AdmissionID` trên `HEX_ExamRecord` là khóa liên kết.

Từ HIS:

- `M02_MedicalProcess.ID`: mã quy trình khám.
- `M02_MedicalProcessDetail.ItemGroupID`: mã phòng/phần khám lâm sàng.
- `M02_MedicalProcessDetail.SignStatus`: trạng thái hoàn tất của phần khám.
- `SignStatus = 2` (`SWTStatus.Finish`): phần khám đã hoàn tất/ký xong.
- `SignStatus = 0`: chưa trình ký.
- `SignStatus = 1`: đang ký số.
- `SignStatus = 3`: đã hủy ký.

Một phòng/phần khám được xem là hoàn tất khi tất cả detail thuộc cùng `ItemGroupID` có `SignStatus = 2`.
Các detail không có `ItemGroupID` không được tính là phòng lâm sàng.

## 4. API contract đề xuất

### Endpoint

`GET /v1/exam-records/{recordId}/his-form/clinical-progress`

Endpoint yêu cầu credential HIS giống các endpoint `his-form` hiện tại.

### Response thành công

```json
{
  "recordId": "guid",
  "admissionId": 12345,
  "status": "WAITING",
  "statusName": "Chờ khám",
  "currentSection": null,
  "completedSectionCount": 0,
  "totalSectionCount": 3,
  "updatedAt": "2026-09-15T10:00:00Z"
}
```

Khi đã hoàn tất phòng đầu tiên:

```json
{
  "recordId": "guid",
  "admissionId": 12345,
  "status": "IN_PROGRESS",
  "statusName": "Đang khám",
  "currentSection": {
    "itemGroupId": 12,
    "sectionKey": "12",
    "sectionName": "Nội tổng quát"
  },
  "completedSectionCount": 1,
  "totalSectionCount": 3,
  "updatedAt": "2026-09-15T10:05:00Z"
}
```

Nếu toàn bộ phòng lâm sàng hoàn tất, trả:

```json
{
  "status": "COMPLETED",
  "statusName": "Đã khám",
  "currentSection": null
}
```

`sectionName` lấy từ definition/layout HIS theo `ItemGroupID`; nếu HIS không cung cấp tên thì trả chuỗi rỗng và vẫn giữ `itemGroupId`.

## 5. Quy tắc tính trạng thái

1. Không có `AdmissionID` hợp lệ → lỗi nghiệp vụ giống các endpoint HIS hiện tại.
2. Không tìm thấy process đúng template KSK → `WAITING`, `totalSectionCount = 0`.
3. Không có phòng nào hoàn tất (`completedSectionCount = 0`) → `WAITING`.
4. Có ít nhất một phòng hoàn tất và còn phòng chưa hoàn tất → `IN_PROGRESS`.
5. Tất cả phòng hoàn tất → `COMPLETED`.
6. `currentSection` là phòng chưa hoàn tất đầu tiên theo thứ tự HIS trả về.
7. Phòng có `SignStatus = 1` hoặc `3` chưa được tính là hoàn tất.
8. Trạng thái chỉ phản ánh tiến độ lâm sàng; không đổi `ExamRecord.State` trong spec này.

## 6. Lỗi và bảo mật

- Hồ sơ không tồn tại: giữ mã lỗi hiện tại.
- Chưa có `AdmissionID`: giữ lỗi `AdmissionID` hiện tại.
- HIS tắt/chưa cấu hình/lỗi kết nối: map lỗi HIS hiện tại, không trả dữ liệu cũ giả định.
- Credential chỉ chuyển tiếp sang HIS, không lưu hoặc ghi log.

## 7. Acceptance criteria

- Hồ sơ có 3 phòng, chưa phòng nào `SignStatus = 2` → FE nhận `WAITING / Chờ khám`.
- Phòng 1 có toàn bộ detail `SignStatus = 2`, phòng 2 chưa xong → FE nhận `IN_PROGRESS / Đang khám`, current section là phòng 2.
- Phòng 1 mới chỉ có một detail `SignStatus = 2`, còn detail khác chưa xong → chưa tính phòng 1 hoàn tất.
- Tất cả phòng có `SignStatus = 2` → FE nhận `COMPLETED / Đã khám`.
- Endpoint danh sách hồ sơ hiện tại không phát sinh thêm request HIS cho từng record.

## 8. Files dự kiến khi triển khai

- `HealthExam.API/Controllers/ExamRecordHisFormController.cs`: thêm route endpoint.
- `HealthExam.Application/His/`: thêm query, result model và handler tính tiến độ.
- `HealthExam.Infrastructure/Integrations/HisEmr/HisEmrClient.cs`: tái sử dụng operation hiện có, không thêm client riêng.
- `HealthExam.Tests/HisFormEndpointTests.cs` hoặc test application HIS hiện có: test contract và các trạng thái biên.

