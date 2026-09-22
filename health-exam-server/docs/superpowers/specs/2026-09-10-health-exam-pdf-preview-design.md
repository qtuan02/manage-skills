# Health-exam PDF preview

## Mục tiêu

Thêm khả năng xem trước PDF cho biểu mẫu đăng ký KSK trong
`health-exam-server`. PDF phải phản ánh dữ liệu đã lưu trong HIS EMR của hồ
sơ, không lấy các giá trị chưa được lưu từ phía trình duyệt.

Luồng preview chỉ render và trả file PDF. Nó không submit form, không tạo
luồng ký và không thay đổi trạng thái hồ sơ.

## Phạm vi

### Trong phạm vi

- Thêm một endpoint preview có phạm vi theo `recordId`.
- Đọc `HisEmrDataID` từ `ExamRecord` thuộc đúng `DivisionID` của request.
- Gọi HIS `M03F10010/VEMR` với `IsJson=false`.
- Nhận binary PDF từ HIS và trả trực tiếp cho client với content type
  `application/pdf`.
- Giữ nguyên cơ chế xác thực, credential forwarding, trace ID và division
  forwarding đang có của `HisEmrHttpClient`.
- Unit/integration tests cho success và các lỗi chính.

### Ngoài phạm vi

- Không preview dữ liệu chưa save.
- Không gọi `M02F01500/SubmitEMR` hoặc `SignEMR`.
- Không tạo hoặc cập nhật sign workflow.
- Không thêm cơ chế cache PDF.
- Không thay đổi HIS `VEMR` hoặc Report Server.
- Không triển khai UI/FE trong task backend này; FE chỉ cần dùng endpoint trả
  blob PDF.

## API contract

### Endpoint

```http
GET /v1/exam-records/{recordId}/registration-form/preview
```

Endpoint kế thừa các middleware hiện có của `health-exam-server`, bao gồm
employee authentication và `X-Division-Id` validation.

### Thành công

- HTTP `200`.
- `Content-Type: application/pdf`.
- Body là PDF bytes do HIS render.
- Content-Disposition nên là `inline`, với tên file ổn định theo
  `RecordCode` nếu framework hiện tại yêu cầu tên file.

### Lỗi

| Trường hợp | Kết quả |
|---|---|
| `recordId` không tồn tại trong tenant hiện tại | Lỗi `NotFound` theo envelope chuẩn của health-exam |
| Hồ sơ chưa có `HisEmrDataID` | Lỗi `InvalidState`, thông báo form chưa được lưu lên HIS |
| HIS từ chối xác thực/quyền | Chuyển tiếp mã lỗi hiện có của `HisEmrHttpClient` |
| HIS timeout hoặc không kết nối được | Chuyển thành `HisTimeout`/`HisBadGateway` theo adapter hiện có |
| HIS trả HTTP lỗi hoặc body không phải PDF | `HisBadGateway`, không trả bytes lỗi cho FE |

Không trả `ResultData<byte[]>` JSON cho success. Đây là file endpoint giống
cách HIS controller hiện trả `File(..., "application/pdf")`.

## Luồng dữ liệu

```text
FE
 │ GET /v1/exam-records/{recordId}/registration-form/preview
 ▼
ExamRecordRegistrationFormController
 │ validate record + tenant, lấy preview từ service
 ▼
RegistrationFormValueService
 │ đọc ExamRecord.HisEmrDataID
 │ không có ID → InvalidState
 ▼
IHisEmrApi / HisEmrHttpClient
 │ GET /api/M03F10010/VEMR
 │ EMRDataID={HisEmrDataID}&IsJson=false
 │ forward HIS credential, X-Trace-Id, X-Division-Id
 ▼
HIS M03F10010/VEMR
 │ đọc M03_EMRData + M03_EMRDataDetail
 │ gọi Report Server GenReport
 ▼
PDF bytes
```

Preview lấy đúng `HisEmrDataID` đã được ghi vào `ExamRecord` sau luồng save
form. Không dựng lại payload từ request preview và không gọi `REMR` rồi tự
render ở health-exam.

## Thiết kế thành phần

### `ExamRecordRegistrationFormController`

Thêm action `GET .../registration-form/preview`. Action gọi service, nhận
`byte[]` và trả file PDF. Controller không tự truy cập repository hoặc gọi HIS.

### `IRegistrationFormValueService` và `RegistrationFormValueService`

Thêm một method preview nhận `recordId`:

1. Load `ExamRecord` với điều kiện `RecordID` và `DivisionID`.
2. Nếu không tồn tại, ném `NotFound`.
3. Nếu `HisEmrDataID` null hoặc empty, ném `InvalidState`.
4. Gọi adapter HIS để lấy PDF bytes.
5. Trả bytes cho controller.

Service không cập nhật `ExamRecord`, kể cả khi HIS preview thành công hoặc thất
bại.

### `IHisEmrApi` và `HisEmrHttpClient`

Thêm method chuyên biệt cho binary PDF, ví dụ `RenderFormPdfAsync(Guid
emrDataId, CancellationToken ct)`.

Không tái sử dụng `SendAsync<T>` hiện tại nếu method đó luôn deserialize
`HisEnvelope`. Method binary mới cần:

- Dùng route `api/M03F10010/VEMR`.
- Gắn `EMRDataID` và `IsJson=false` bằng query string.
- Dùng chung `ValidateOptions`, credential HIS và các header forwarding.
- Đọc `ReadAsByteArrayAsync` khi response thành công.
- Kiểm tra body không rỗng và content type/định dạng PDF hợp lệ ở mức cần
  thiết.
- Khi HIS trả lỗi JSON, cố gắng lấy message theo cùng quy tắc hiện có rồi ném
  `HealthExamException` tương ứng.
- Không retry tự động: preview là GET nhưng render report có thể nặng; việc
  retry sẽ tăng tải Report Server và không cần thiết cho contract đầu tiên.

## Bảo mật và dữ liệu

- Mọi request phải đi qua truy vấn có `DivisionID`, không cho phép đoán
  `recordId` để xem PDF tenant khác.
- Credential HIS được lấy từ request context như các lời gọi HIS hiện tại;
  không nhận credential từ query string hoặc request body.
- Không log PDF bytes, nội dung EMR, thông tin bệnh nhân hoặc URL file ảnh.
- Log operation, `recordId`, `HisEmrDataID`, trace ID và thời gian xử lý ở mức
  cần thiết cho điều tra lỗi; tránh log dữ liệu nhạy cảm.
- Endpoint chỉ render dữ liệu đã tồn tại trong HIS. Việc có thể xem preview
  tuân theo auth policy hiện có của health-exam-server.

## Tính nhất quán với save form

Frontend hiện lưu các section bằng `isDraft=true`, sau đó
`health-exam-server` ghi lại `HisEmrDataID`. Preview gọi HIS bằng ID này nên
phản ánh snapshot mới nhất đã save thành công.

Nếu save section thất bại, preview không được tự động lấy giá trị cũ từ request
hoặc từ memory của FE; nó chỉ render snapshot cuối cùng đã lưu trên HIS.

## Kiểm thử

### Adapter HIS

- Khi HIS trả `200` với `application/pdf`, adapter trả đúng bytes.
- Request có đúng route, `EMRDataID` và `IsJson=false`.
- Request forward đúng HIS credential, `X-Trace-Id` và `X-Division-Id`.
- HIS trả timeout, connection error, HTTP error hoặc body rỗng được ánh xạ
  đúng error code hiện có.
- Body JSON lỗi của HIS không bị trả nhầm như PDF.

### Service/controller

- Hồ sơ có `HisEmrDataID` trả `200` và content type PDF.
- Hồ sơ không tồn tại trả `NotFound`.
- Hồ sơ chưa save form trả `InvalidState` và không gọi HIS.
- Hồ sơ khác tenant không được preview.
- Preview không thay đổi `HisEmrDataID`, sync status hoặc các field khác của
  `ExamRecord`.
- Không có lời gọi `SubmitEMR`, `SignEMR` hoặc sign-server trong test preview.

### Acceptance criteria

1. Sau khi save form thành công, gọi endpoint preview bằng `recordId` trả được
   PDF mở được bằng browser/PDF viewer.
2. Nội dung PDF phản ánh snapshot đã lưu trên HIS.
3. Gọi preview không tạo sign workflow và không làm hồ sơ chuyển trạng thái.
4. Hồ sơ chưa từng save form nhận được lỗi rõ ràng thay vì PDF rỗng hoặc lỗi
   HIS khó hiểu.
5. Các test hiện có của health-exam-server vẫn pass.

## Quyết định

Chọn proxy PDF qua `health-exam-server` thay vì cho FE gọi thẳng HIS. Cách này
giữ credential HIS ở backend, giữ kiểm soát tenant/record ownership ở một chỗ
và tạo contract ổn định cho FE nếu route HIS thay đổi sau này.
