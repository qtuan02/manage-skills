# `GET /v1/auth/me` — thông tin nhân viên đang đăng nhập

Backend chuyển tiếp Bearer token sang HIS `api/Auth/Info` và trả **nguyên** những gì HIS
trả. Không có field nào do health-exam-server tự bịa.

## Header

```http
Authorization: Bearer <token>
X-Division-Id: <division-id>
```

## Response

```json
{
  "ErrorCode": 0,
  "Message": "",
  "Data": {
    "AccountID": 77,
    "EmployeeID": 4210,
    "EmployeeCode": "NV001",
    "DivisionID": "DHTESTING",
    "UserName": "Bác sĩ EMR",
    "PhoneNumber": "0901234567",
    "DepartmentID": 12,
    "DepartmentName": "Khoa Khám bệnh",
    "IsChangePassword": false,
    "Permissions": null
  },
  "TraceID": "c37c1f3f2f6a4ea8"
}
```

| Trường | Kiểu | Ghi chú |
| --- | --- | --- |
| `AccountID` | `number \| null` | Khoá tài khoản `SAM_Account` bên HIS |
| `EmployeeID` | `number \| null` | Khoá nhân viên; fallback claim `EmployeeID` của JWT nếu HIS không gửi |
| `EmployeeCode` | `string \| null` | Mã nhân viên |
| `DivisionID` | `string \| null` | Đơn vị HIS cấp token |
| `UserName` | `string \| null` | **Tên hiển thị** của tài khoản ("Bác sĩ EMR"), không phải login. Fallback claim `UserName` của JWT |
| `PhoneNumber` | `string \| null` | `iAM_Employee.MobileNo`; tài khoản Admin không có nhân viên thì `""` → trả `null` |
| `DepartmentID` | `number \| null` | Khoa của nhân viên |
| `DepartmentName` | `string \| null` | Tên khoa |
| `IsChangePassword` | `boolean` | Tài khoản bị yêu cầu đổi mật khẩu; HIS không gửi → `false` |
| `Permissions` | `Record<string, number> \| null` | HIS hiện luôn trả `null`; giữ nguyên để FE không phải đoán |

## Thay đổi so với contract cũ (18/09/2026) — **breaking**

- `Username` → **`UserName`** (đúng tên HIS).
- **Bỏ** `FullName`, `Title`: HIS `Auth/Info` không có hai field này, trước đây luôn `null`.
  FE đang dựng tên hiển thị bằng `FullName?.trim() || Username` → đổi thành `UserName`.
- Thêm `AccountID`, `DivisionID`, `PhoneNumber`, `DepartmentID`, `DepartmentName`,
  `IsChangePassword`, `Permissions`.

## Lỗi

Giữ nguyên: `401`/`4010` token hết hạn hoặc HIS từ chối; `403`/`4030` token không phải nhân
viên; `5022` HIS chưa bật hoặc không kết nối được; `5023` HIS quá thời gian chờ.
