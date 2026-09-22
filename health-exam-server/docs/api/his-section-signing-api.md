# Tài Liệu Tích Hợp API Ký Số Từng Phần (HIS Section Digital Signing)

> ⚠️ **ĐÃ NGỪNG DÙNG (Task 11, 2026-09-18)** — Toàn bộ endpoint/route mô tả trong tài liệu này (`.../his-form/processes/{processId}/sections/{sectionKey}/submit|sign|sign/cancel`, luồng ký qua SWT của his-server) đã bị xoá khỏi `health-exam-server`. Luồng ký hiện tại xem tại
> [`conclusion-pdf-signing-guide.md`](./conclusion-pdf-signing-guide.md).

Tài liệu đặc tả kỹ thuật dành cho Frontend tích hợp tính năng ký số từng phần (Section Digital Signing) theo quy trình EMR của HIS.

---

## 1. Nguyên Tắc & Quy Ước Chung

- **Base URL:** `/v1/exam-records/{recordId}/his-form/processes/{processId}/sections`
- **Xác thực (Authentication):** Mọi request bắt buộc truyền token nhân viên HIS trong header:
  ```http
  Authorization: Bearer <his_employee_token>
  ```
- **Quy tắc về `sectionKey`:**
  - **Frontend TUYỆT ĐỐI KHÔNG tự tạo hoặc hardcode `sectionKey`**.
  - `sectionKey` là chuỗi định danh duy nhất của từng phần khám/nhóm dịch vụ, được backend trả về từ API danh sách (`GET /sections`) hoặc từ trường `itemGroupID` trong chi tiết process form.
  - URL encoding: Vì `sectionKey` có thể chứa ký tự đặc biệt, Frontend cần encode URL khi đưa vào path parameter (`encodeURIComponent(sectionKey)`).

---

## 2. Mô Tả Luồng Trạng Thái (State Machine)

Mỗi section có một vòng đời độc lập theo các trạng thái:

```
[Draft] (Nháp)
   │
   ▼ (Gửi duyệt / Submit)
[InProcessing] (Chờ ký)
   │
   ├───────────────► (Ký số / Sign) ───────────────► [Signed] (Đã ký)
   │                                                    │
   └───────────────► (Hủy / Cancel) ◄───────────────────┘
                           │
                           ▼
                       [Canceled] (Đã hủy)
```

- **`Draft`**: Phần khám mới tạo hoặc chưa gửi ký số.
  - `canSubmit = true`, `canSign = false`, `canCancel = false`.
- **`InProcessing`**: Đã gửi duyệt, đang chờ bác sĩ ký số.
  - `canSubmit = false`, `canSign = true`, `canCancel = true`.
- **`Signed`**: Bác sĩ đã ký số thành công.
  - `canSubmit = false`, `canSign = false`, `canCancel = true`.
- **`Canceled`**: Đã bị hủy trình ký hoặc hủy chữ ký.
  - `canSubmit = false`, `canSign = false`, `canCancel = false`.

> **Lưu ý Frontend:** Luôn căn cứ vào 3 cờ `canSubmit`, `canSign`, `canCancel` từ response để điều khiển trạng thái ẩn/hiện/disable của các nút bấm trên giao diện.

---

## 3. Danh Sách Endpoints

### 3.1. Lấy danh sách trạng thái ký số các section trong form

Lấy toàn bộ các section thuộc form khám kèm thông tin trạng thái ký số và quyền thao tác hiện tại của tài khoản đăng nhập.

- **Method:** `GET`
- **Path:** `/v1/exam-records/{recordId}/his-form/processes/{processId}/sections`
- **Headers:**
  - `Authorization: Bearer <his_employee_token>`

#### Response Thành Công (`200 OK`)
```json
{
  "code": 2000,
  "message": "Success",
  "data": [
    {
      "sectionKey": "KHAM_NOI",
      "sectionName": "Khám Nội Khoa",
      "status": "Draft",
      "currentStep": 1,
      "currentStepName": "Soạn thảo",
      "canSubmit": true,
      "canSign": false,
      "canCancel": false,
      "signedByEmployeeID": null,
      "signedAt": null,
      "cancelReason": null
    },
    {
      "sectionKey": "KHAM_NGOAI",
      "sectionName": "Khám Ngoại Khoa",
      "status": "InProcessing",
      "currentStep": 2,
      "currentStepName": "Chờ bác sĩ chuyên khoa ký",
      "canSubmit": false,
      "canSign": true,
      "canCancel": true,
      "signedByEmployeeID": null,
      "signedAt": null,
      "cancelReason": null
    },
    {
      "sectionKey": "KHAM_MAT",
      "sectionName": "Khám Mắt",
      "status": "Signed",
      "currentStep": 3,
      "currentStepName": "Đã hoàn thành ký số",
      "canSubmit": false,
      "canSign": false,
      "canCancel": true,
      "signedByEmployeeID": 1024,
      "signedAt": "2026-09-16T08:30:00Z",
      "cancelReason": null
    }
  ]
}
```

---

### 3.2. Trình ký một section (Submit)

Chuyển trạng thái phần khám từ `Draft` sang `InProcessing` để chờ ký.

- **Method:** `POST`
- **Path:** `/v1/exam-records/{recordId}/his-form/processes/{processId}/sections/{sectionKey}/submit`
- **Headers:**
  - `Authorization: Bearer <his_employee_token>`
- **Request Body:** Không có (Body rỗng).

#### Response Thành Công (`200 OK`)
Trả về thông tin trạng thái mới nhất của section sau khi submit:
```json
{
  "code": 2000,
  "message": "Success",
  "data": {
    "sectionKey": "KHAM_NOI",
    "sectionName": "Khám Nội Khoa",
    "status": "InProcessing",
    "currentStep": 2,
    "currentStepName": "Chờ ký",
    "canSubmit": false,
    "canSign": true,
    "canCancel": true,
    "signedByEmployeeID": null,
    "signedAt": null,
    "cancelReason": null
  }
}
```

---

### 3.3. Ký số một section (Sign)

> Đường ký mục khám hiện hành (không qua process HIS) là `POST /v1/exam-records/{recordId}/sections/{itemGroupId}/sign` — xem `conclusion-pdf-signing-guide.md` §1 (body tùy chọn chọn Người xác nhận, PROJ-2374).

Bác sĩ chuyên khoa thực hiện ký số xác nhận phần khám. Yêu cầu section đang ở trạng thái `InProcessing`.

- **Method:** `POST`
- **Path:** `/v1/exam-records/{recordId}/his-form/processes/{processId}/sections/{sectionKey}/sign`
- **Headers:**
  - `Authorization: Bearer <his_employee_token>`
- **Request Body:** Không có (Body rỗng).

#### Response Thành Công (`200 OK`)
Trả về thông tin trạng thái mới nhất của section sau khi ký:
```json
{
  "code": 2000,
  "message": "Success",
  "data": {
    "sectionKey": "KHAM_NOI",
    "sectionName": "Khám Nội Khoa",
    "status": "Signed",
    "currentStep": 3,
    "currentStepName": "Đã ký số",
    "canSubmit": false,
    "canSign": false,
    "canCancel": true,
    "signedByEmployeeID": 1024,
    "signedAt": "2026-09-16T08:45:12Z",
    "cancelReason": null
  }
}
```

---

### 3.4. Hủy trình ký / Hủy chữ ký một section (Cancel)

Hủy quá trình ký (khi đang ở `InProcessing`) hoặc hủy kết quả đã ký (khi đang ở `Signed`). Bắt buộc phải cung cấp lý do hủy.

- **Method:** `POST`
- **Path:** `/v1/exam-records/{recordId}/his-form/processes/{processId}/sections/{sectionKey}/sign/cancel`
- **Headers:**
  - `Authorization: Bearer <his_employee_token>`
  - `Content-Type: application/json`
- **Request Body:**
```json
{
  "reason": "Bác sĩ cần sửa lại thông tin chẩn đoán cận thị"
}
```

#### Bảng Tham Số Request Body
| Trường | Kiểu | Bắt buộc | Mô tả |
| :--- | :--- | :--- | :--- |
| `reason` | `string` | **Có** | Lý do hủy ký (tối thiểu 1 ký tự không khoảng trắng). |

#### Response Thành Công (`200 OK`)
Trả về thông tin trạng thái mới nhất của section sau khi hủy:
```json
{
  "code": 2000,
  "message": "Success",
  "data": {
    "sectionKey": "KHAM_NOI",
    "sectionName": "Khám Nội Khoa",
    "status": "Canceled",
    "currentStep": 0,
    "currentStepName": "Đã hủy",
    "canSubmit": false,
    "canSign": false,
    "canCancel": false,
    "signedByEmployeeID": null,
    "signedAt": null,
    "cancelReason": "Bác sĩ cần sửa lại thông tin chẩn đoán cận thị"
  }
}
```

---

## 4. Chi Tiết Các Trường Dữ Liệu (`HisSectionSigningItem`)

| Tên trường | Kiểu dữ liệu | Nullable | Ý nghĩa & Quy ước hiển thị |
| :--- | :--- | :---: | :--- |
| `sectionKey` | `string` | Không | Mã định danh section do hệ thống trả về. Dùng làm path param cho các thao tác ký/hủy. |
| `sectionName` | `string` | Không | Tên hiển thị của phần khám (ví dụ: *"Khám Thể Lực"*, *"Khám Mắt"*). |
| `status` | `string` | Không | Trạng thái hiện tại: `Draft` \| `InProcessing` \| `Signed` \| `Canceled`. |
| `currentStep` | `number` | Có | Bước hiện tại trong quy trình ký HIS EMR. |
| `currentStepName` | `string` | Có | Tên mô tả bước hiện tại từ hệ thống HIS. |
| `canSubmit` | `boolean` | Không | `true`: Được phép bấm nút **Trình ký (Submit)**. |
| `canSign` | `boolean` | Không | `true`: Được phép bấm nút **Ký số (Sign)**. |
| `canCancel` | `boolean` | Không | `true`: Được phép bấm nút **Hủy ký (Cancel)**. |
| `signedByEmployeeID` | `number` | Có | ID nhân viên/bác sĩ đã thực hiện ký số. |
| `signedAt` | `string` (ISO 8601) | Có | Thời điểm ký số hoàn tất (UTC). |
| `cancelReason` | `string` | Có | Lý do hủy ký nếu section đang ở trạng thái `Canceled`. |

---

## 5. Danh Sách Mã Lỗi Thường Gặp

Khi gọi API thất bại, hệ thống trả về HTTP status tương ứng kèm cấu trúc JSON lỗi:

```json
{
  "code": 4090,
  "message": "Section is not in a submittable state."
}
```

| HTTP Status | Error Code | Mô tả nguyên nhân | Hướng xử lý cho Frontend |
| :---: | :---: | :--- | :--- |
| **400** | `4001` | Tham số không hợp lệ (ví dụ: lý do hủy `reason` để trống hoặc chỉ có khoảng trắng; `sectionKey` rỗng). | Validate form input trước khi gửi request. |
| **401** | `4010` | Thiếu hoặc token xác thực nhân viên HIS không hợp lệ / hết hạn. | Điều hướng người dùng đăng nhập lại hoặc làm mới token. |
| **403** | `4030` | Không có quyền thao tác (token không phải nhân viên hoặc ID nhân viên không khớp với tài khoản được ký). | Thông báo người dùng không đủ quyền thực hiện hành động này. |
| **404** | `4040` | Không tìm thấy hồ sơ (`recordId`), quy trình khám (`processId`) hoặc phần khám (`sectionKey`). | Kiểm tra lại URL params hoặc tải lại trang để lấy danh sách mới nhất. |
| **409** | `4090` | Xung đột trạng thái (Precondition failed): Cố gắng submit khi `canSubmit = false`, ký khi `canSign = false` hoặc hủy khi `canCancel = false`. | Báo lỗi thao tác không hợp lệ với trạng thái hiện tại, tự động gọi lại `GET /sections` để đồng bộ lại UI. |
| **502** | `5020` | Lỗi kết nối hoặc phản hồi không hợp lệ từ máy chủ HIS EMR cấp dưới. | Thông báo lỗi hệ thống HIS và gợi ý người dùng thử lại sau ít phút. |
| **503** | `5030` | Tính năng ký số từng phần chưa được bật cấu hình (`HIS_EMR_SECTION_SIGNING_ENABLED=false`). | Ẩn toàn bộ khối chức năng ký số từng phần trên giao diện. |

---

## 6. Ký Kết Luận Hồ Sơ & Xem Trước PDF Ký Số (Conclusion Signing & Signed PDF Preview)

### 6.1. Kiểm tra điều kiện ký kết luận (Conclusion Eligibility)
Kiểm tra xem hồ sơ đã đủ điều kiện để ký kết luận hay chưa.
- **Điều kiện A (Khám lâm sàng HIS):** Toàn bộ các chuyên khoa lâm sàng trên HIS (ngoại trừ Kết luận `M02F01512`) đều phải ở trạng thái `Signed`.
- **Điều kiện B (Cận lâm sàng CLS):** Toàn bộ các chỉ định cận lâm sàng của hồ sơ đều đã có kết quả (`Done`) hoặc đã được hủy.

- **Method:** `GET`
- **Path:** `/v1/exam-records/{recordId}/conclusion-eligibility`
- **Headers:** `Authorization: Bearer <his_employee_token>`

#### Response Ví dụ (`200 OK`):
```json
{
  "code": 2000,
  "message": "Success",
  "data": {
    "profileID": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "recordCode": "HS2026090001",
    "submissionID": null,
    "canSignConclusion": true,
    "conditions": [
      {
        "code": "A",
        "label": "Hoàn thành khám lâm sàng",
        "satisfied": true,
        "detail": "Đã ký 5/5 chuyên khoa lâm sàng trên HIS",
        "source": "his-server"
      },
      {
        "code": "B",
        "label": "Đủ kết quả cận lâm sàng",
        "satisfied": true,
        "detail": "Tất cả chỉ định cận lâm sàng đã có kết quả hoặc đã hủy",
        "source": "health-exam-server"
      }
    ]
  }
}
```

### 6.2. Ký kết luận hồ sơ (Conclusion Sign)
Thực hiện ký số phần Kết luận (`M02F01512`) trực tiếp trên HIS workflow.
- **Yêu cầu:** Frontend **không** cần truyền bất kỳ tham số hay section/actor nào trong body (backend tự động resolve từ token nhân viên và live HIS workflow).
- **Idempotent:** Nếu kết luận đã được ký (`Signed`), API trả về thành công ngay mà không thực hiện thay đổi lặp lại.

- **Method:** `POST`
- **Path:** `/v1/exam-records/{recordId}/conclusion/sign`
- **Headers:**
  - `Authorization: Bearer <his_employee_token>`
  - `Content-Type: application/json`
- **Request Body:**
```json
{}
```

#### Response Thành Công (`200 OK`):
```json
{
  "code": 2000,
  "message": "Success",
  "data": {
    "profileID": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "processID": "9a6cb6e7-142f-4886-9a5c-5b23bfa40f4e",
    "sectionKey": "M02F01512",
    "status": "Signed",
    "signedByEmployeeID": 1001,
    "signedAt": "2026-09-16T15:30:00Z",
    "conditions": [
      {
        "code": "A",
        "label": "Hoàn thành khám lâm sàng",
        "satisfied": true,
        "detail": "Đã ký 5/5 chuyên khoa lâm sàng trên HIS",
        "source": "his-server"
      },
      {
        "code": "B",
        "label": "Đủ kết quả cận lâm sàng",
        "satisfied": true,
        "detail": "Tất cả chỉ định cận lâm sàng đã có kết quả hoặc đã hủy",
        "source": "health-exam-server"
      }
    ],
    "steps": [
      {
        "step": "SUBMIT",
        "succeeded": true,
        "detail": "Section M02F01512 chuyển sang InProcessing"
      },
      {
        "step": "SIGN",
        "succeeded": true,
        "detail": "Ký số thành công section M02F01512"
      }
    ]
  }
}
```

### 6.3. Xem trước và tải PDF biểu mẫu (Registration Form PDF Preview)
Endpoint này trả về file PDF luồng binary (`application/pdf`) cho frontend hiển thị hoặc cho người dùng tải về.
- **Trước khi ký kết luận:** Trả về file PDF tổng hợp các phần đã ký trên HIS (`VEMRs`). Nếu chưa có phần nào ký, tự động fallback về PDF bản nháp (`VEMR`).
- **Sau khi ký kết luận:** Luôn trả về file PDF đã ký số hoàn chỉnh từ HIS (`VEMRs`), **tuyệt đối không bao giờ fallback về bản nháp**.

- **Method:** `GET`
- **Path:** `/v1/exam-records/{recordId}/registration-form/preview`
- **Headers:** `Authorization: Bearer <his_employee_token>`
- **Response:** `200 OK`, `Content-Type: application/pdf` (binary stream).

