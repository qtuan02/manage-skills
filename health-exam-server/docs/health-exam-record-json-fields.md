# Cấu trúc JSON hồ sơ khám sức khỏe

API `GET /v1/exam-records/{recordId}` trả về `ResultData<ExamRecordItem>`. Tên field JSON sử dụng **PascalCase**.

## Envelope ngoài cùng

| Field | Kiểu JSON | Ý nghĩa |
|---|---|---|
| `ErrorCode` | `number` | Mã kết quả; `0` là thành công |
| `Message` | `string` | Thông báo kết quả hoặc lỗi |
| `Data` | `object \| null` | Dữ liệu hồ sơ khám sức khỏe |
| `TraceID` | `string` | Mã truy vết request trong log |

## `Data` – định danh hồ sơ và đợt khám

| Field | Kiểu JSON | Ý nghĩa |
|---|---|---|
| `RecordID` | `string (UUID)` | ID hồ sơ khám sức khỏe |
| `SessionID` | `string (UUID)` | ID đợt khám chứa hồ sơ |
| `SessionCode` | `string` | Mã đợt khám |
| `ExamDate` | `string (date)` | Ngày khám, dạng `YYYY-MM-DD` |
| `RecordCode` | `string` | Mã hồ sơ khám sức khỏe |

## Thông tin hành chính người khám

| Field | Kiểu JSON | Ý nghĩa |
|---|---|---|
| `PatientID` | `number` | ID người bệnh trên HIS |
| `AdmissionID` | `number \| null` | ID lượt tiếp nhận trên HIS |
| `HisAdmissionLinkStatus` | `string` | Trạng thái liên kết lượt tiếp nhận HIS |
| `PatientCode` | `string` | Mã người bệnh |
| `FullName` | `string` | Họ và tên người khám |
| `Dob` | `string (date) \| null` | Ngày sinh, dạng `YYYY-MM-DD` |
| `BirthYear` | `number \| null` | Năm sinh, dùng khi không có ngày sinh đầy đủ |
| `GenderID` | `number` | ID/mã số giới tính |
| `IdentityNumber` | `string` | Số CCCD, CMND hoặc giấy tờ tùy thân |
| `InsuranceNumber` | `string` | Số thẻ bảo hiểm |
| `PhoneNumber` | `string` | Số điện thoại |
| `Email` | `string` | Địa chỉ email |
| `Address` | `string` | Địa chỉ cư trú |
| `StaffCode` | `string` | Mã nhân viên tại đơn vị |
| `OrgDeptName` | `string` | Tên phòng ban hoặc đơn vị công tác |
| `JobTitle` | `string` | Chức danh công việc |

## Nhóm khám, gói khám và biểu mẫu

| Field | Kiểu JSON | Ý nghĩa |
|---|---|---|
| `VariantCode` | `string` | Mã đối tượng/biến thể khám, ví dụ `DTK_01` |
| `VariantName` | `string` | Tên đối tượng/biến thể khám |
| `PackageID` | `string (UUID) \| null` | ID gói khám áp dụng |
| `PackageName` | `string` | Tên gói khám |
| `FormID` | `string (UUID) \| null` | ID biểu mẫu được sử dụng |
| `FormCode` | `string` | Mã biểu mẫu |
| `SubmissionID` | `string (UUID) \| null` | ID bản ghi dữ liệu đã nhập trên form-server |

## Trạng thái hồ sơ

| Field | Kiểu JSON | Ý nghĩa |
|---|---|---|
| `State` | `number` | Mã trạng thái hồ sơ |
| `StateName` | `string` | Tên tiếng Việt của trạng thái |
| `RegisteredAt` | `string (date-time) \| null` | Thời điểm hoàn tất đăng ký |
| `ExamStartedAt` | `string (date-time) \| null` | Thời điểm bắt đầu khám |
| `ExamFinishedAt` | `string (date-time) \| null` | Thời điểm hoàn tất khám |
| `CancelledAt` | `string (date-time) \| null` | Thời điểm hủy đăng ký hoặc hủy khám |
| `CancelReason` | `string` | Lý do hủy |

### Giá trị `State`

| Giá trị | Ý nghĩa |
|---:|---|
| `0` | Chưa đăng ký |
| `1` | Chờ khám |
| `2` | Đang khám |
| `3` | Đã khám |
| `4` | Hủy đăng ký |
| `5` | Hủy khám |

## Tiến độ và kết quả tổng quát

| Field | Kiểu JSON | Ý nghĩa |
|---|---|---|
| `ProgressDone` | `number` | Số phần khám đã hoàn thành |
| `ProgressTotal` | `number` | Tổng số phần khám |
| `HealthClassCode` | `string` | Mã phân loại sức khỏe |
| `Note` | `string \| null` | Ghi chú hồ sơ |

> `ProgressDone` và `ProgressTotal` hiện luôn trả `0/0` vì webhook cập nhật tiến độ chưa được triển khai.

## Dân tộc, nghề nghiệp và nhóm máu

| Field | Kiểu JSON | Ý nghĩa |
|---|---|---|
| `EthnicityCode` | `string` | Mã dân tộc |
| `EthnicityName` | `string` | Tên dân tộc |
| `OccupationCode` | `string` | Mã nghề nghiệp |
| `OccupationName` | `string` | Tên nghề nghiệp |
| `BloodAboCode` | `string` | Mã nhóm máu hệ ABO |
| `BloodAboName` | `string` | Tên nhóm máu hệ ABO |
| `BloodRhCode` | `string` | Mã yếu tố Rh |
| `BloodRhName` | `string` | Tên yếu tố Rh |

## Địa chỉ và giấy tờ tùy thân

| Field | Kiểu JSON | Ý nghĩa |
|---|---|---|
| `ProvinceCode` | `string` | Mã tỉnh/thành phố |
| `ProvinceName` | `string` | Tên tỉnh/thành phố |
| `WardCode` | `string` | Mã phường/xã |
| `WardName` | `string` | Tên phường/xã |
| `IdentityIssuedDate` | `string (date) \| null` | Ngày cấp giấy tờ tùy thân |
| `IdentityIssuerCode` | `string` | Mã nơi/cơ quan cấp giấy tờ |
| `IdentityIssuerName` | `string` | Tên nơi/cơ quan cấp giấy tờ |

## Thông tin người liên hệ

| Field | Kiểu JSON | Ý nghĩa |
|---|---|---|
| `RelativeRelationshipCode` | `string` | Mã quan hệ của người liên hệ với người khám |
| `RelativeRelationshipName` | `string` | Tên mối quan hệ |
| `RelativeFullName` | `string` | Họ tên người liên hệ/người thân |
| `RelativeIdentityNumber` | `string` | Số giấy tờ tùy thân của người liên hệ |
| `RelativePhoneNumber` | `string` | Số điện thoại người liên hệ |

## Thông tin bảo hiểm

| Field | Kiểu JSON | Ý nghĩa |
|---|---|---|
| `InsuranceObjectCode` | `string` | Mã đối tượng bảo hiểm/thanh toán |
| `InsuranceObjectName` | `string` | Tên đối tượng bảo hiểm/thanh toán |
| `InsuranceValidFrom` | `string (date) \| null` | Ngày bắt đầu hiệu lực bảo hiểm |
| `InsuranceValidTo` | `string (date) \| null` | Ngày hết hiệu lực bảo hiểm |

## Thông tin khám sức khỏe

| Field | Kiểu JSON | Ý nghĩa |
|---|---|---|
| `ExamReason` | `string` | Lý do khám sức khỏe |
| `PatientTypeCode` | `string` | Mã loại người bệnh |
| `PatientTypeName` | `string` | Tên loại người bệnh |
| `PatientSubjectCode` | `string` | Mã đối tượng người bệnh/người khám |
| `PatientSubjectName` | `string` | Tên đối tượng người bệnh/người khám |
| `PaymentSourceCode` | `string` | Mã nguồn thanh toán |
| `PaymentSourceName` | `string` | Tên nguồn thanh toán |
| `PaymentSourceOther` | `string` | Mô tả nguồn thanh toán khác |
| `ExamLocationCode` | `string` | Mã địa điểm khám |
| `ExamLocationName` | `string` | Tên địa điểm khám |
| `RegistrationPlaceCode` | `string` | Mã nơi đăng ký khám |
| `RegistrationPlaceName` | `string` | Tên nơi đăng ký khám |

## ID tham chiếu nội bộ

| Field | Kiểu JSON | Ý nghĩa |
|---|---|---|
| `PatientRefID` | `string (UUID) \| null` | ID bản ghi thông tin người bệnh nội bộ |
| `InsuranceRefID` | `string (UUID) \| null` | ID bản ghi bảo hiểm nội bộ |
| `EmploymentRefID` | `string (UUID) \| null` | ID bản ghi thông tin việc làm nội bộ |
| `RelativeRefID` | `string (UUID) \| null` | ID bản ghi người liên hệ nội bộ |
| `PatientTypeOptionID` | `string (UUID) \| null` | ID master data của loại người bệnh |
| `PaymentSourceOptionID` | `string (UUID) \| null` | ID master data của nguồn thanh toán |
| `ExamLocationOptionID` | `string (UUID) \| null` | ID master data của địa điểm khám |

## Nguồn đối chiếu

- DTO response: `HealthExam.API/Contracts/ExamRecordModels.cs`
- Endpoint: `HealthExam.API/Controllers/ExamRecordController.cs`
- Envelope: `HealthExam.API/Contracts/ResultData.cs`
- Trạng thái hồ sơ: `HealthExam.Domain/Common/Enums.cs`

> Lưu ý: `HisAdmissionLinkStatus` có trong DTO nhưng chưa được gán trong hàm mapping, nên hiện rơi về giá trị mặc định `"Pending"`.
