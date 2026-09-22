# Thiết kế dữ liệu đăng ký và Master Data cho hồ sơ khám sức khỏe

**Ngày:** 2026-09-07

**Phạm vi:** `health-exam-server`

**Nguồn nghiệp vụ:** `Documents/1. Màn hình Đăng ký hồ sơ khám sức khỏe/`

## 1. Mục tiêu

Bổ sung đủ dữ liệu và API để frontend ráp màn hình đăng ký hồ sơ khám sức khỏe, bao gồm:

- các dropdown và ô nhập còn thiếu ở bước thông tin hành chính và thông tin khám;
- dữ liệu danh mục theo mô hình hybrid: danh mục ổn định nằm trong code, danh mục cần cấu hình nằm trong DB;
- lưu đồng thời mã danh mục và ảnh chụp tên trên hồ sơ;
- phân biệt rõ `Hủy đăng ký` và `Hủy khám`;
- giữ tương thích với API, dữ liệu và file import hiện có.

Thiết kế không thay đổi `form-server`. Service này vẫn chỉ cung cấp biểu mẫu động cho hai khu vực “Tiền sử bệnh” và “Thông tin bổ sung”.

## 2. Ranh giới trách nhiệm

### health-exam-server

- Sở hữu dữ liệu hành chính, thân nhân, BHYT và thông tin đăng ký khám của `HEX_ExamRecord`.
- Cung cấp options cho các dropdown tĩnh và danh mục cấu hình.
- Cung cấp tỉnh/phường cho cả các trường cố định và các field động có `LookupCode=CityProvince/Ward`.
- Kiểm tra mã danh mục khi tạo, sửa hoặc xác nhận hồ sơ.
- Sở hữu máy trạng thái hồ sơ và thao tác hủy.

### form-server

- Tiếp tục cung cấp layout và dữ liệu của “Tiền sử bệnh” và “Thông tin bổ sung” theo nhóm khám.
- Không bổ sung API master data trong phạm vi này.
- Không có foreign key hoặc truy vấn chéo DB tới `health-exam-server`.

### frontend

- Gọi `registration-options` một lần khi mở wizard.
- Tìm tỉnh/phường qua các endpoint chuyên biệt.
- Khi form động trả `LookupCode=CityProvince` hoặc `LookupCode=Ward`, ánh xạ lookup đó sang API tỉnh/phường của `health-exam-server`.
- Gửi mã option đã chọn; tên trả về từ backend được dùng làm ảnh chụp hiển thị.

## 3. Mô hình Master Data hybrid

### 3.1 Danh mục tĩnh trong code

Các danh mục nhỏ, ổn định và gắn với logic nghiệp vụ không lưu DB:

- giới tính;
- nhóm máu ABO: `A`, `B`, `AB`, `O`;
- nhóm máu Rh: `+`, `-`;
- quan hệ thân nhân;
- trạng thái hồ sơ;
- 10 nhóm khám hiện có trong `ExamGroups`.

Các option dùng shape thống nhất:

```json
{
  "code": "O",
  "name": "O",
  "orderNo": 1
}
```

Giới tính vẫn dùng giá trị số tương thích với `GenderID`; API trả giá trị đó dưới dạng `code` để frontend không cần một contract riêng.

### 3.2 Danh mục cấu hình trong DB

Thêm bảng `HEX_MasterDataOption` trong DB riêng của `health-exam-server`, schema `public`:

| Cột | Kiểu/ý nghĩa |
|---|---|
| `OptionID` | `uuid`, khóa chính |
| `DivisionID` | tenant sở hữu dữ liệu |
| `Category` | mã nhóm danh mục |
| `Code` | mã ổn định được lưu vào hồ sơ |
| `Name` | tên hiện hành |
| `ParentCode` | mã cha; dùng để nối phường với tỉnh |
| `OrderNo` | thứ tự hiển thị |
| `IsActive` | có còn được chọn cho hồ sơ mới hay không |
| `CreatedDate`, `CreatedBy`, `ModifiedDate`, `ModifiedBy` | audit kỹ thuật theo pattern hiện có |

Ràng buộc duy nhất là `(DivisionID, Category, Code)`. Index tra cứu gồm `(DivisionID, Category, IsActive, OrderNo)` và `(DivisionID, Category, ParentCode, IsActive)`.

Các `Category` được hỗ trợ:

- `ETHNICITY`
- `OCCUPATION`
- `PROVINCE`
- `WARD`
- `IDENTITY_ISSUER`
- `INSURANCE_OBJECT`
- `PATIENT_TYPE`
- `PAYMENT_SOURCE`
- `EXAM_LOCATION`

Danh mục chỉ đọc trong phạm vi tính năng này. Dữ liệu khởi tạo bằng migration/seed; API quản trị thêm/sửa/xóa option nằm ngoài phạm vi.

Option “Nguồn khác” của `PAYMENT_SOURCE` dùng mã ổn định `OTHER`; đây là mã nghiệp vụ để backend bật validation cho `PaymentSourceOther`, không phụ thuộc tên hiển thị.

## 4. API Master Data

### 4.1 Options tổng hợp

```http
GET /v1/master-data/registration-options
```

Endpoint trả toàn bộ options nhỏ cần để dựng wizard:

```json
{
  "data": {
    "genders": [],
    "ethnicities": [],
    "occupations": [],
    "bloodAbos": [],
    "bloodRhs": [],
    "identityIssuers": [],
    "relationships": [],
    "insuranceObjects": [],
    "patientTypes": [],
    "paymentSources": [],
    "examLocations": [],
    "examRecordStates": [],
    "examGroups": []
  }
}
```

Chỉ trả option đang active. Không đưa tỉnh/phường vào response tổng hợp để tránh tải một danh sách lớn mỗi lần mở wizard.

Các API hiện có vẫn được giữ nguyên để không làm hỏng consumer:

- `GET /v1/exam-groups`
- `GET /v1/exam-packages`
- `GET /v1/organizations`

### 4.2 Tìm tỉnh

```http
GET /v1/master-data/provinces?keyword=
```

- Chỉ trả `PROVINCE` đang active của `DivisionID` hiện tại.
- `keyword` tìm không phân biệt hoa thường theo mã hoặc tên.
- Response là danh sách option sắp theo `OrderNo`, sau đó theo `Name`.

### 4.3 Tìm phường/xã

```http
GET /v1/master-data/wards?provinceCode=&keyword=
```

- `provinceCode` là bắt buộc và phải là tỉnh đang active.
- Chỉ trả `WARD` đang active có `ParentCode` bằng `provinceCode`.
- `keyword` tìm không phân biệt hoa thường theo mã hoặc tên.
- Tỉnh không hợp lệ trả lỗi validation, không trả một danh sách rỗng gây hiểu nhầm.

## 5. Mở rộng dữ liệu hồ sơ

Thêm các cột nullable hoặc chuỗi mặc định rỗng vào `HEX_ExamRecord`, đồng thời thêm trường tương ứng vào `ExamRecordSaveRequest` và `ExamRecordItem`:

| Nhóm | Trường |
|---|---|
| Hành chính | `EthnicityCode`, `EthnicityName`, `OccupationCode`, `OccupationName` |
| Nhóm máu | `BloodAboCode`, `BloodAboName`, `BloodRhCode`, `BloodRhName` |
| Địa chỉ | `ProvinceCode`, `ProvinceName`, `WardCode`, `WardName` |
| Giấy tờ | `IdentityIssuedDate`, `IdentityIssuerCode`, `IdentityIssuerName` |
| Thân nhân | `RelativeRelationshipCode`, `RelativeRelationshipName`, `RelativeFullName`, `RelativeIdentityNumber`, `RelativePhoneNumber` |
| BHYT | `InsuranceObjectCode`, `InsuranceObjectName`, `InsuranceValidFrom`, `InsuranceValidTo` |
| Thông tin khám | `ExamReason`, `PatientTypeCode`, `PatientTypeName`, `PaymentSourceCode`, `PaymentSourceName`, `PaymentSourceOther`, `ExamLocationCode`, `ExamLocationName` |

Quy tắc lưu option:

1. Frontend gửi `Code`; không được tự quyết định `Name`.
2. Backend tra option theo `DivisionID`, `Category`, `Code` và `IsActive=true`.
3. Backend ghi cả mã và tên hiện hành vào hồ sơ. Tên là snapshot lịch sử, không tự đổi khi tên danh mục đổi sau này.
4. Khi sửa một hồ sơ cũ, mã đang lưu nhưng đã inactive vẫn được trả về để hiển thị; người dùng chỉ có thể chọn lại một mã active.
5. Xóa lựa chọn gửi mã rỗng/null sẽ xóa cả cặp code/name nếu trường đó không bắt buộc.

`WardCode` phải thuộc `ProvinceCode`. `InsuranceValidTo` không được trước `InsuranceValidFrom`. `PaymentSourceOther` chỉ được giữ khi `PaymentSourceCode=OTHER`; với mọi nguồn khác backend xóa giá trị nhập tay để tránh dữ liệu ẩn còn sót.

`ExamReason` là bắt buộc khi xác nhận hồ sơ, không bắt buộc ở lần lưu nháp đầu tiên. Quy tắc này cho phép wizard lưu bước 1 trước khi người dùng hoàn tất bước 2.

## 6. Trạng thái hủy hồ sơ

### 6.1 Bảng trạng thái

| Giá trị | Enum | Tên hiển thị |
|---:|---|---|
| 0 | `NotRegistered` | Chưa đăng ký |
| 1 | `Waiting` | Chờ khám |
| 2 | `InProgress` | Đang khám |
| 3 | `Completed` | Đã khám |
| 4 | `RegistrationCancelled` | Hủy đăng ký |
| 5 | `ExamCancelled` | Hủy khám |

Giá trị cũ `Cancelled=4` được đổi tên thành `RegistrationCancelled=4`. Vì giá trị số không đổi, dữ liệu hiện có không cần rewrite và được hiểu là “Hủy đăng ký”. `ExamCancelled=5` là giá trị mới.

### 6.2 Chuyển trạng thái

```text
NotRegistered (0) ──cancel──> RegistrationCancelled (4)
Waiting (1)       ──cancel──> ExamCancelled (5)
InProgress (2)    ──cancel──> ExamCancelled (5)
Completed (3)     ──cancel──> bị từ chối
```

Hai trạng thái hủy là trạng thái cuối. Gọi hủy lại trên chính hồ sơ đã hủy trả thành công với dữ liệu hiện tại để thao tác có tính idempotent; reason mới trong lần gọi lặp bị bỏ qua. Không có đường chuyển từ loại hủy này sang loại hủy kia.

### 6.3 API hủy

```http
POST /v1/exam-records/{recordId}/cancel
Content-Type: application/json

{
  "reason": "Người bệnh không tiếp tục khám"
}
```

- Backend tự suy ra trạng thái đích từ trạng thái hiện tại; frontend không gửi `kind` hoặc `targetState`.
- `reason` bắt buộc sau khi trim.
- Khi thành công, backend ghi `State`, `CancelledAt`, `CancelledBy`, `CancelReason`, các trường modified và audit nghiệp vụ trong cùng transaction.
- Hồ sơ không tồn tại trả 404; trạng thái không hợp lệ hoặc đợt đã đóng/hủy trả lỗi state theo convention hiện có.

### 6.4 Ảnh hưởng tới logic hiện có

Mọi nhánh đang kiểm tra `ExamRecordState.Cancelled` phải được phân loại lại:

- sửa hồ sơ, cấp credential, tạo chỉ định và xử lý webhook: chặn cả trạng thái `4` và `5`;
- kiểm tra trùng CCCD/mã người bệnh: loại cả hai trạng thái hủy khỏi tập hồ sơ đang hoạt động, giữ đúng hành vi cũ;
- thống kê tiến độ đợt: giữ `Cancelled` là tổng của hai loại để tương thích, đồng thời bổ sung `RegistrationCancelled` và `ExamCancelled` cho frontend mới;
- job đối soát và webhook không được kéo hồ sơ từ trạng thái `4` hoặc `5` trở lại luồng khám;
- danh sách hồ sơ vẫn filter bằng tham số `state`, nên `state=4` và `state=5` phân biệt được hai loại hủy.

## 7. Luồng dữ liệu

```text
FE mở wizard
  ├─ GET /v1/master-data/registration-options
  ├─ GET /v1/master-data/provinces?keyword=...
  └─ GET /v1/master-data/wards?provinceCode=...&keyword=...

FE lưu bước 1/2
  └─ health-exam-server kiểm tra code
       └─ ghi code + name snapshot vào HEX_ExamRecord

FE chọn Nhóm khám
  └─ GET /v1/exam-records/{id}/form-draft
       └─ form-server trả layout Tiền sử/Thông tin bổ sung
            └─ FE map LookupCode tỉnh/phường về master-data API

FE xác nhận
  └─ POST /v1/exam-records/{id}/confirm
       ├─ kiểm tra các trường bắt buộc, gồm ExamReason
       └─ NotRegistered → Waiting
```

Không dùng distributed transaction giữa hai service. Lưu hồ sơ/master data thuộc transaction DB của `health-exam-server`; lưu submission thuộc transaction DB của `form-server`; webhook tiếp tục đảm bảo eventual consistency cho tiến độ khám.

## 8. Migration và tương thích

Cần EF Core migration vì có bảng và cột mới.

- Tạo `HEX_MasterDataOption`, index và unique constraint.
- Thêm các cột mới vào `HEX_ExamRecord` dưới dạng nullable hoặc mặc định chuỗi rỗng.
- Không xóa hoặc đổi dữ liệu hiện có.
- Không chuyển schema và không dùng chung DB với `form-server` hay `iam-server`.
- Giá trị trạng thái `4` giữ nguyên trong DB; migration không chạy câu cập nhật trạng thái.
- Cập nhật model snapshot, `Deploy/schema/health-exam-schema.sql` và seed master data.
- Request cũ thiếu trường mới vẫn hợp lệ. Response chỉ được bổ sung field, không xóa hoặc đổi tên field cũ.
- Import cũ tiếp tục chạy; các cột mới có thể để trống. Việc mở rộng template import cho toàn bộ trường mới là hạng mục riêng nếu frontend cần.

## 9. Lỗi và validation

- Mã static option không hợp lệ: trả lỗi validation theo đúng tên field request.
- Mã DB option không tồn tại, sai category, inactive hoặc thuộc tenant khác: trả lỗi validation.
- Phường không thuộc tỉnh: trả lỗi tại `WardCode`.
- Khoảng ngày BHYT sai: trả lỗi tại `InsuranceValidTo`.
- Chọn nguồn “Nguồn khác” nhưng thiếu `PaymentSourceOther`: trả lỗi khi xác nhận.
- Thiếu `ExamReason`: cho phép lưu draft nhưng trả lỗi khi xác nhận.
- Không gọi `form-server` để kiểm tra master data; lỗi hoặc downtime của `form-server` không làm hỏng API master data.

## 10. Kiểm thử chấp nhận

### Master data

- Aggregate endpoint trả đúng static options và các DB options active của tenant hiện tại.
- Option inactive và option của tenant khác không bị lộ.
- Tỉnh/phường tìm theo keyword và ward luôn thuộc tỉnh được yêu cầu.
- Tỉnh sai hoặc thiếu `provinceCode` trả validation error.

### Hồ sơ

- Create/update lưu đúng code và tên snapshot lấy từ backend.
- Đổi tên master data không làm thay đổi hồ sơ đã lưu.
- Request cũ không có field mới vẫn tạo/sửa được hồ sơ.
- Validate quan hệ tỉnh/phường, thời hạn BHYT và nguồn chi trả khác.
- Confirm từ chối khi thiếu `ExamReason` hoặc thiếu nội dung của nguồn khác.

### Trạng thái hủy

- `0 → 4`, `1 → 5`, `2 → 5` thành công và ghi đủ thông tin hủy/audit.
- Hủy hồ sơ `3` bị từ chối.
- Gọi lại cancel trên hồ sơ `4` hoặc `5` không thay đổi loại hủy và không tạo tác dụng phụ lặp.
- Update, portal credential, tạo chỉ định, webhook và reconcile đều chặn/bỏ qua cả `4` và `5`.
- Filter danh sách phân biệt `state=4` và `state=5`.
- Progress trả tổng `Cancelled` tương thích và hai số chi tiết đúng.

## 11. Ngoài phạm vi

- API quản trị CRUD cho `HEX_MasterDataOption`.
- Thay đổi schema hoặc database của `form-server` và `iam-server`.
- Di chuyển các form cố định sang `form-server`.
- Distributed transaction hoặc Saga giữa `health-exam-server` và `form-server`.
- Mở rộng toàn bộ template Excel/import cho các trường mới.
