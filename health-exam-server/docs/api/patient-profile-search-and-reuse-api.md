# API tìm kiếm và tái sử dụng hồ sơ người bệnh

Tài liệu dành cho frontend tích hợp luồng tìm kiếm hồ sơ cá nhân theo CCCD,
điền lại thông tin khi đăng ký khám và xử lý trường hợp thông tin người bệnh đã
thay đổi.

## 0. Trả lời nhanh cho frontend: các API dùng lúc nào?

Có năm API liên quan, nhưng chúng không được gọi liên tiếp trong mọi trường hợp:

| API | Khi nào gọi? | Có ghi dữ liệu không? |
| --- | --- | --- |
| `GET /v1/patients/search` | Khi người dùng gõ từ khoá (CCCD, mã BN, tên, SĐT) ở ô tìm kiếm; trả danh sách tóm tắt | Không |
| `GET /v1/patients/{patientRefId}` | Khi người dùng chọn một dòng trong danh sách tìm kiếm để điền vào form đăng ký | Không |
| `POST /v1/patients/match` | Khi bấm **Lưu** trên form (đăng ký mới hoặc sửa); backend so 13 trường với hồ sơ active cùng CCCD | Không |
| `POST /v1/exam-records` | Sau match, khi bấm **Đăng ký** để tạo một hồ sơ khám mới | Có |
| `PUT /v1/exam-records/{recordId}` | Sau match, khi bấm **Lưu** trên một hồ sơ khám đã tồn tại | Có |

Vì vậy, với màn hình **Đăng ký mới**, luồng thông thường là:

```text
Gõ từ khoá -> SEARCH (danh sách tóm tắt)
  -> chọn một dòng -> GET /v1/patients/{patientRefId} -> điền form
  -> người dùng sửa/xác nhận
  -> bấm Lưu -> MATCH
       -> Matched=true  : gọi POST/PUT với PatientRefID
       -> Matched=false : cảnh báo ChangedFields; nếu người dùng vẫn lưu
                          thì gọi POST/PUT với PatientRefID + SetAsActiveProfile
       -> PatientRefID=null : người bệnh mới, POST không gửi PatientRefID
```

`PUT` không được gọi trong luồng đăng ký mới. `PUT` chỉ dùng khi frontend đã có
`recordId` của một hồ sơ khám và đang sửa hồ sơ đó.

Search không tạo hồ sơ khám, không giữ chỗ và không thay thế POST. Nó chỉ giúp
frontend biết có hồ sơ cá nhân cũ để điền lại hay không.

## 1. Nguyên tắc

- Tìm kiếm theo CCCD/CMND, mã người bệnh, họ tên, số điện thoại hoặc để backend tự
  nhận diện; kết quả là danh sách tóm tắt, luôn tối đa 20 dòng, mới cập nhật lên đầu.
  Không có từ khoá thì trả 20 hồ sơ mới cập nhật nhất.
- Hồ sơ đầy đủ chỉ lấy qua `GET /v1/patients/{patientRefId}`.
- Backend chỉ trả hồ sơ đang active trong `DivisionID` hiện tại.
- Backend chỉ xác nhận match khi toàn bộ 13 trường thông tin cá nhân khớp hồ sơ
  active; frontend không tự viết lại quy tắc so sánh.
- Mỗi hồ sơ khám giữ nguyên phiên bản thông tin người bệnh đã sử dụng tại thời
  điểm đăng ký.
- `ProfileLineageID` và `VersionNumber` chỉ được lưu trong database, chưa được
  trả qua API.
- Frontend không có API xem các phiên bản inactive trong phạm vi hiện tại.

## 2. Header chung

```http
Authorization: Bearer <token>
X-Division-Id: <division-id>
```

`DivisionID` được lấy từ request context. Frontend không truyền tenant trong
query hoặc body để tìm hồ sơ của đơn vị khác.

Response JSON dùng PascalCase:

```json
{
  "ErrorCode": 0,
  "Message": "",
  "Data": {},
  "TraceID": "trace-id"
}
```

## 3. Tìm hồ sơ active

```http
GET /v1/patients/search?type=auto&keyword=079123456789
```

### Query parameters

| Trường | Kiểu | Bắt buộc | Ý nghĩa |
| --- | --- | --- | --- |
| `keyword` | `string` | Không | Từ khoá; backend tự trim hai đầu. **Bỏ trống → trả 20 hồ sơ mới cập nhật nhất** (`type` bị bỏ qua) |
| `type` | `string` | Không | `identity` \| `code` \| `name` \| `phone` \| `auto` (mặc định `auto`) |

### Cách khớp từng loại

| `type` | Trường so sánh | Quy tắc |
| --- | --- | --- |
| `identity` | `IdentityNumber` | Bắt đầu bằng `keyword` |
| `phone` | `PhoneNumber` | Bắt đầu bằng `keyword` |
| `code` | `PatientCode` | Bắt đầu bằng `keyword`, không phân biệt hoa/thường |
| `name` | `FullName` | Chứa `keyword`, không phân biệt hoa/thường, **có phân biệt dấu** |
| `auto` | tự chọn | Xem bên dưới |

Quy tắc `auto`:

- Toàn số, 9 hoặc 12 ký tự → tìm như `identity`.
- Toàn số, 10 ký tự bắt đầu bằng `0` → tìm như `phone`.
- Toàn số, độ dài khác (đang gõ dở) → tìm đồng thời `identity`, `phone`, `code` và gộp.
- Có chữ cái → tìm đồng thời `name` và `code` và gộp (mã BN không có format cố định).

Backend chỉ trả hồ sơ active trong `DivisionID` hiện tại, **luôn tối đa 20 dòng**, sắp xếp
theo `ModifiedDate` giảm dần (mới cập nhật lên đầu). Không truyền `keyword` thì đây chính là
"20 người bệnh mới nhất" để hiển thị mặc định khi mở màn hình. Nếu danh sách đầy 20 dòng,
frontend nên gợi ý gõ thêm để thu hẹp; không có phân trang.

### Có kết quả

```json
{
  "ErrorCode": 0,
  "Message": "",
  "Data": [
    {
      "PatientRefID": "a768f42f-d8fa-4b06-98e3-25385b4b2521",
      "PatientCode": "BN000125001",
      "FullName": "Nguyễn Văn An",
      "BirthYear": 1990,
      "GenderID": 1,
      "IdentityNumber": "079123456789"
    }
  ],
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

`BirthYear` lấy từ `BirthYear` của hồ sơ, nếu trống thì lấy năm của `Dob`; có thể `null`
nếu cả hai đều trống.

### Không tìm thấy

Đây không phải lỗi. API trả `Data: []`; frontend tiếp tục luồng tạo người bệnh mới và
không gửi `PatientRefID`.

### Lỗi đầu vào

HTTP `400`, mã nghiệp vụ `4001` khi `type` không thuộc 5 giá trị
trên:

```json
{
  "ErrorCode": 4001,
  "Message": "Loại tìm kiếm không hợp lệ. Chấp nhận: identity, code, name, phone, auto.",
  "Data": null,
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

## 3b. Lấy hồ sơ đầy đủ để điền form

```http
GET /v1/patients/{patientRefId}
```

Gọi khi người dùng chọn một dòng trong kết quả search. Backend chỉ trả hồ sơ **active**
trong `DivisionID` hiện tại.

```json
{
  "ErrorCode": 0,
  "Message": "",
  "Data": {
    "PatientRefID": "a768f42f-d8fa-4b06-98e3-25385b4b2521",
    "DivisionID": "D01",
    "HisPatientID": 125001,
    "PatientCode": "BN000125001",
    "FullName": "Nguyễn Văn An",
    "Dob": "1990-05-20",
    "BirthYear": 1990,
    "GenderID": 1,
    "IdentityNumber": "079123456789",
    "IdentityIssuedDate": "2021-06-15",
    "IdentityIssuerOptionID": "31a75517-80ea-4b89-983c-19d26dc87efe",
    "PhoneNumber": "0901234567",
    "Email": "an@example.com",
    "Address": "12 Nguyễn Huệ, TP.HCM",
    "EthnicityOptionID": "0b0b108d-5dab-462e-a12f-94f57d667ff7",
    "BloodAboCode": "A",
    "BloodRhCode": "+",
    "IsActive": true,

    "IdentityIssuerCode": "CCS",
    "IdentityIssuerName": "Cục Cảnh sát QLHC về TTXH",
    "EthnicityCode": "KINH",
    "EthnicityName": "Kinh",

    "InsuranceRefID": "0f1c4a1e-6a4f-4d8e-9a2b-7c1d2e3f4a5b",
    "InsuranceNumber": "DN4790000001",
    "InsuranceObjectCode": "HT",
    "InsuranceObjectName": "Hưu trí",
    "InsuranceValidFrom": "2026-01-01",
    "InsuranceValidTo": "2026-12-31",
    "RegistrationPlaceCode": "79001",
    "RegistrationPlaceName": "BV Quận 1",

    "EmploymentRefID": "5a6b7c8d-9e0f-4a1b-8c2d-3e4f5a6b7c8d",
    "OccupationCode": "GV",
    "OccupationName": "Giáo viên",
    "StaffCode": "NV01",
    "OrgDeptName": "Phòng Hành chính",
    "JobTitle": "Chuyên viên",

    "RelativeRefID": "9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a",
    "RelativeRelationshipCode": "SPOUSE",
    "RelativeRelationshipName": "Vợ-chồng",
    "RelativeFullName": "Trần Thị B",
    "RelativeIdentityNumber": "079000000001",
    "RelativePhoneNumber": "0912345678"
  },
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

Từ 18/09/2026 response có thêm 24 trường (từ `IdentityIssuerCode` trở đi). Chúng **trùng tên
với `ExamRecordItem`** của `GET /v1/exam-records/{id}` nên frontend dùng lại cùng một mapping
sang form (`ethnicityCode`, `identityIssuerCode`, `insuranceSubject` ← `InsuranceObjectCode`,
`registrationPlaceCode`, `occupationCode`, `relativeRelationship` ← `RelativeRelationshipCode`, …).
Quy ước giá trị: chuỗi rỗng `""` = không có; `*RefID` `null` = không có dòng tương ứng.

Dòng BHYT / nghề nghiệp / thân nhân trả về là dòng **của lần đăng ký gần nhất** của phiên bản
hồ sơ này (một phiên bản có thể tích luỹ nhiều dòng qua nhiều đợt khám). Lần đăng ký gần nhất
không khai BHYT thì `InsuranceRefID: null` — không mượn BHYT của đợt cũ hơn. Phiên bản chưa có
đợt khám nào thì lấy dòng active mới nhất.

Lưu ý: `POST /v1/patients/match` vẫn so sánh `IdentityIssuerOptionID`/`EthnicityOptionID`
theo UUID; frontend chưa có nguồn để gửi ngược OptionID nên hai trường đó vẫn có thể báo
"khác" — ngoài phạm vi thay đổi này.

Frontend lưu response này làm `sourceProfile` (xem mục 4) và copy sang `formValues`.

Không tìm thấy (đã inactive vì có phiên bản mới hơn, hoặc khác đơn vị): HTTP `404`, mã
`4040`, `Message: "Không tìm thấy hồ sơ người bệnh."`. Frontend nên search lại để lấy
phiên bản active hiện tại.

## 4. Kiểm tra toàn bộ thông tin trước khi sử dụng lại

Frontend có thể gọi API này trực tiếp từ trang tạo mới, không bắt buộc gọi
search trước. Backend lấy `IdentityNumber` trong body để tự tìm hồ sơ active
đúng `DivisionID`, sau đó so sánh toàn bộ thông tin cá nhân:

```http
POST /v1/patients/match
Content-Type: application/json
```

```json
{
  "FullName": "Nguyễn Văn An",
  "Dob": "1990-05-20",
  "BirthYear": 1990,
  "GenderID": 1,
  "IdentityNumber": "079123456789",
  "IdentityIssuedDate": "2021-06-15",
  "IdentityIssuerOptionID": "31a75517-80ea-4b89-983c-19d26dc87efe",
  "PhoneNumber": "0901234567",
  "Email": "an@example.com",
  "Address": "12 Nguyễn Huệ, TP.HCM",
  "EthnicityOptionID": "0b0b108d-5dab-462e-a12f-94f57d667ff7",
  "BloodAboCode": "A",
  "BloodRhCode": "+"
}
```

### Khớp toàn bộ thông tin

HTTP `200`:

```json
{
  "ErrorCode": 0,
  "Message": "",
  "Data": {
    "PatientRefID": "a768f42f-d8fa-4b06-98e3-25385b4b2521",
    "Matched": true,
    "ChangedFields": []
  },
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

Frontend giữ `PatientRefID`, tự động tái sử dụng hồ sơ và tiếp tục gọi
`POST /v1/exam-records`.

### Có thông tin khác hồ sơ active

Không match vẫn là HTTP `200`, vì đây là kết quả kiểm tra chứ không phải lỗi:

```json
{
  "ErrorCode": 0,
  "Message": "",
  "Data": {
    "PatientRefID": "a768f42f-d8fa-4b06-98e3-25385b4b2521",
    "Matched": false,
    "ChangedFields": ["Address", "PhoneNumber"]
  },
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

Frontend cảnh báo người dùng và cho chọn:

- Dùng thông tin đang lưu: điền lại dữ liệu từ `GET /v1/patients/{patientRefId}`.
- Dùng thông tin vừa nhập cho lần khám này: POST hồ sơ khám với
  `PatientRefID` và `SetAsActiveProfile=false`.
- Dùng thông tin vừa nhập và đặt làm mặc định: POST hồ sơ khám với
  `PatientRefID` và `SetAsActiveProfile=true`.

Không bỏ `PatientRefID` để tạo một hồ sơ active thứ hai cùng CCCD.

### Không có hồ sơ active cùng CCCD

Backend trả HTTP `200`; frontend tiếp tục luồng tạo người bệnh mới:

```json
{
  "ErrorCode": 0,
  "Message": "",
  "Data": {
    "PatientRefID": null,
    "Matched": false,
    "ChangedFields": []
  },
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

Nếu `IdentityNumber` trống, backend trả HTTP `400`, mã `4001`.

Quy tắc chuẩn hoá giống hệt lúc tạo/cập nhật hồ sơ: họ tên bỏ khoảng trắng dư
và không phân biệt hoa/thường nhưng vẫn phân biệt dấu tiếng Việt; email và mã
nhóm máu không phân biệt hoa/thường; các chuỗi còn lại trim rồi so sánh có phân
biệt hoa/thường; ngày, số và ID phải bằng chính xác.

### Trạng thái frontend sau search

Frontend nên giữ riêng hai loại dữ liệu:

```ts
type RegistrationState = {
  searchedIdentityNumber: string;
  selectedPatientRefId: string | null;
  sourceProfile: PatientProfile | null;
  formValues: Record<string, unknown>;
};
```

- `sourceProfile`: dữ liệu backend trả về, dùng để so sánh thay đổi.
- `formValues`: bản người dùng đang sửa và sẽ gửi trong POST/PUT.
- `selectedPatientRefId`: lấy từ dòng được chọn trong search, từ response của
  `GET /v1/patients/{patientRefId}`, hoặc từ match; chỉ tự động tái sử dụng khi
  match trả `Matched=true`.

Không sửa trực tiếp `sourceProfile`; nếu sửa chung một object thì frontend sẽ
không còn dữ liệu gốc để giải thích trường nào đã thay đổi.

## 5. Tạo hồ sơ khám bằng hồ sơ người bệnh đã chọn

```http
POST /v1/exam-records
Content-Type: application/json
```

Frontend điền dữ liệu từ `GET /v1/patients/{patientRefId}` vào form, cho phép người
nhập chỉnh sửa, sau đó gửi toàn bộ thông tin cuối cùng cùng `PatientRefID`.

```json
{
  "PatientRefID": "a768f42f-d8fa-4b06-98e3-25385b4b2521",
  "FullName": "Nguyễn Văn An",
  "Dob": "1990-05-20",
  "BirthYear": 1990,
  "GenderID": 1,
  "IdentityNumber": "079123456789",
  "PhoneNumber": "0901234567",
  "Email": "an@example.com",
  "Address": "12 Nguyễn Huệ, TP.HCM",
  "VariantCode": "DTK_03",
  "SetAsActiveProfile": null
}
```

Các field hành chính khác của `ExamRecordWriteRequest` vẫn gửi như luồng đăng
ký hiện tại. Hai field mới frontend cần quan tâm là:

| Trường | Kiểu | Ý nghĩa |
| --- | --- | --- |
| `PatientRefID` | `UUID?` | ID hồ sơ active lấy từ API search; bỏ trống khi tạo người bệnh mới |
| `SetAsActiveProfile` | `boolean?` | Quyết định khi dữ liệu gửi lên khác hồ sơ nguồn |

### Không thay đổi thông tin

Nếu dữ liệu cá nhân giống hồ sơ nguồn:

- Backend tái sử dụng chính `PatientRefID`.
- Không tạo phiên bản mới.
- `SetAsActiveProfile` có thể để `null`.

Đây vẫn là một lệnh tạo **hồ sơ khám mới**. Chỉ hồ sơ cá nhân được tái sử dụng;
không phải frontend đang mở lại hồ sơ khám cũ.

### Có thay đổi nhưng chưa có quyết định

Nếu frontend gửi `PatientRefID`, dữ liệu cá nhân đã thay đổi và
`SetAsActiveProfile = null`, backend không ghi dữ liệu và trả HTTP `409`, mã
`4096`:

```json
{
  "ErrorCode": 4096,
  "Message": "Thông tin người bệnh đã thay đổi",
  "Data": {
    "ChangedFields": [
      "Address",
      "PhoneNumber"
    ]
  },
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

Frontend dùng `ChangedFields` để hiển thị hộp thoại:

> Thông tin người bệnh đã thay đổi. Có sử dụng thông tin mới làm hồ sơ mặc định
> cho những lần khám tiếp theo không?

Sau khi người dùng chọn, gửi lại nguyên request với một trong hai giá trị:

- `SetAsActiveProfile = true`: tạo phiên bản mới và đặt làm hồ sơ active. Những
  lần tìm kiếm tiếp theo trả thông tin mới.
- `SetAsActiveProfile = false`: tạo phiên bản mới chỉ cho hồ sơ khám hiện tại.
  Hồ sơ active cũ vẫn là kết quả của những lần tìm kiếm tiếp theo.

Quan trọng: lần gửi lại phải dùng đúng HTTP method của thao tác ban đầu:

- Đang đăng ký mới: gửi lại `POST /v1/exam-records`.
- Đang sửa hồ sơ: gửi lại `PUT /v1/exam-records/{recordId}`.

Không chuyển từ POST sang PUT chỉ vì backend trả `4096`. Lần POST nhận `4096`
đã rollback và chưa tạo `recordId`.

### Các trường được kiểm tra thay đổi

`ChangedFields` có thể chứa:

- `FullName`
- `Dob`
- `BirthYear`
- `GenderID`
- `IdentityNumber`
- `IdentityIssuedDate`
- `IdentityIssuerOptionID`
- `PhoneNumber`
- `Email`
- `Address`
- `EthnicityOptionID`
- `BloodAboCode`
- `BloodRhCode`

Thông tin BHYT, nghề nghiệp và thân nhân được lưu theo cơ chế riêng; chúng không
quyết định việc tạo phiên bản thông tin cá nhân.

## 6. Cập nhật hồ sơ khám hiện có

Luồng cập nhật sử dụng cùng hai field và cùng quy tắc:

```http
PUT /v1/exam-records/{recordId}
Content-Type: application/json
```

```json
{
  "PatientRefID": "a768f42f-d8fa-4b06-98e3-25385b4b2521",
  "FullName": "Nguyễn Văn An",
  "Dob": "1990-05-20",
  "BirthYear": 1990,
  "GenderID": 1,
  "IdentityNumber": "079123456789",
  "PhoneNumber": "0912345678",
  "VariantCode": "DTK_03",
  "SetAsActiveProfile": false
}
```

Nếu tạo phiên bản mới, response `ExamRecordItem.PatientRefID` là ID phiên bản
vừa được gắn với hồ sơ khám. Frontend phải cập nhật state local bằng response,
không tiếp tục giữ `PatientRefID` cũ.

## 7. Xử lý cạnh tranh

Hai nhân viên có thể cùng mở một hồ sơ active. Khi một người đã thay đổi hồ sơ
active trước, request còn lại có thể nhận HTTP `409`, mã `4090`:

```json
{
  "ErrorCode": 4090,
  "Message": "Hồ sơ người bệnh đã được cập nhật, vui lòng tìm lại.",
  "Data": null,
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

Frontend phải:

1. Đóng hộp thoại xác nhận cũ.
2. Gọi lại `GET /v1/patients/search` bằng CCCD hiện tại.
3. Hiển thị hồ sơ active mới nhất.
4. Gọi lại API match bằng dữ liệu người dùng đang nhập.
5. Cho người dùng quyết định rồi gửi lại request.

Không tự động retry request ghi với `PatientRefID` cũ.

## 8. Luồng frontend đề xuất

```text
Gõ từ khoá -> GET /v1/patients/search (danh sách tóm tắt)
  -> chọn một dòng -> GET /v1/patients/{patientRefId} -> điền form
  -> người dùng sửa/xác nhận
  -> POST /v1/patients/match
       -> PatientRefID=null: tạo mới
       -> Matched=true: POST/PUT với PatientRefID và SetAsActiveProfile=null
       -> Matched=false: cảnh báo bằng ChangedFields
            -> dùng dữ liệu cũ: GET /v1/patients/{patientRefId} rồi POST/PUT
            -> dùng dữ liệu mới lần này: POST/PUT với PatientRefID, false
            -> đặt dữ liệu mới active: POST/PUT với PatientRefID, true
  -> 4096: dữ liệu đổi sau preflight, hỏi lại rồi gửi lại true/false
  -> 4090: gọi match lại
```

### 8.1. Màn hình đăng ký mới

```text
1. Người dùng gõ từ khoá tìm kiếm; frontend gọi GET /v1/patients/search và hiển
   thị danh sách tóm tắt (hoặc bỏ qua nếu là người bệnh mới).
2. Người dùng chọn một dòng; frontend gọi GET /v1/patients/{patientRefId} và
   điền dữ liệu vào form. Người dùng chỉnh sửa nếu cần.
3. Bấm Lưu: frontend gọi POST /v1/patients/match với toàn bộ 13 trường cá nhân.
4a. PatientRefID=null: POST hồ sơ khám mới, không gửi PatientRefID.
4b. Có PatientRefID và Matched=true: POST hồ sơ khám với ID đó.
4c. Có PatientRefID và Matched=false: cảnh báo ChangedFields và hỏi dùng dữ
    liệu cũ, dữ liệu mới cho lần này, hay đặt dữ liệu mới làm active.
5. POST thành công:
    - Điều hướng sang chi tiết hồ sơ bằng RecordID response.
    - Cập nhật PatientRefID local bằng PatientRefID response.
6. POST trả 4096:
    - Hiển thị ChangedFields.
    - Hỏi có đặt thông tin mới làm mặc định không.
    - Gửi lại chính POST cũ với true hoặc false.
7. POST trả 4090:
    - Không retry tự động.
    - Search lại và xác minh lại.
```

### 8.2. Màn hình sửa hồ sơ khám

```text
1. Frontend đã có recordId và dữ liệu hồ sơ hiện tại.
2. Người dùng sửa thông tin rồi bấm Lưu.
3. Gọi PUT /v1/exam-records/{recordId} với PatientRefID hiện tại.
4. PUT thành công: thay toàn bộ state local bằng response.
5. PUT trả 4096: hỏi người dùng rồi gửi lại PUT với true/false.
6. PUT trả 4090: search lại, xác minh lại và cho người dùng lưu lại.
```

### 8.3. Bảng quyết định nhanh

| Tình huống | `PatientRefID` | `SetAsActiveProfile` | API ghi |
| --- | --- | --- | --- |
| Search không có kết quả | `null` | `null` | POST |
| Search có kết quả, đã chọn dòng và GET, không sửa thông tin cá nhân | ID từ GET | `null` | POST |
| Có sửa, chưa hỏi người dùng | ID từ GET | `null` | POST/PUT; backend trả `4096` |
| Người dùng chọn cập nhật mặc định | ID nguồn | `true` | Gửi lại cùng POST/PUT |
| Người dùng chỉ dùng thay đổi cho lần khám này | ID nguồn | `false` | Gửi lại cùng POST/PUT |
| Sửa hồ sơ khám đã tồn tại | ID đang gắn với record | tùy thay đổi | PUT |

Ví dụ xử lý response:

```ts
type ApiEnvelope<T> = {
  ErrorCode: number;
  Message: string;
  Data: T;
  TraceID: string;
};

type ProfileChangedPayload = {
  ChangedFields: string[];
};

async function saveExamRecord(
  body: Record<string, unknown>,
  token: string,
  divisionId: string,
) {
  const response = await fetch("/v1/exam-records", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
      "X-Division-Id": divisionId,
    },
    body: JSON.stringify(body),
  });

  const result = await response.json() as ApiEnvelope<unknown>;

  if (result.ErrorCode === 4096) {
    const changed = result.Data as ProfileChangedPayload;
    return { kind: "ask-active-profile", changedFields: changed.ChangedFields };
  }

  if (result.ErrorCode === 4090) {
    return { kind: "reload-patient", message: result.Message };
  }

  if (!response.ok) throw new Error(result.Message);
  return { kind: "saved", record: result.Data };
}
```

## 9. Bảng mã lỗi frontend cần xử lý

| HTTP | `ErrorCode` | Trường hợp | Hành động frontend |
| --- | --- | --- | --- |
| 200 | `0` | Thành công hoặc search không có dữ liệu | Tiếp tục luồng |
| 400 | `4001` | `type` không hợp lệ, thiếu CCCD khi match, hoặc request không hợp lệ | Hiển thị validation |
| 404 | `4040` | Record không tồn tại | Thông báo và quay lại danh sách |
| 404 | `4040` | `GET /v1/patients/{patientRefId}` không thấy hồ sơ active | Search lại |
| 409 | `4096` | Thông tin cá nhân thay đổi, cần quyết định | Hiện hộp thoại chọn active |
| 409 | `4090` | Hồ sơ nguồn hết hiệu lực hoặc vừa bị cập nhật | Search và xác minh lại |
| 409 | `4093` | Người bệnh đã có hồ sơ trong cùng đợt khám | Không tạo trùng |

## 10. Ngoài phạm vi hiện tại

- Không có API danh sách lịch sử phiên bản.
- Không tìm kiếm theo ngày sinh, mã HIS, hoặc tìm gần đúng (chỉ prefix/contains
  chính xác theo từng loại, xem mục 3).
- Không trả `ProfileLineageID` hoặc `VersionNumber`.
- Không cho frontend chọn một phiên bản inactive.
