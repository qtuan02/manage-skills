# Tìm kiếm và tái sử dụng hồ sơ người bệnh

**Ngày:** 2026-09-16  
**Phạm vi:** `health-exam-server`  
**Trạng thái:** design đã được chốt trong brainstorming

## 1. Mục tiêu

Cho phép nhân viên tiếp nhận tìm và tái sử dụng thông tin cá nhân của người bệnh
khi tạo đợt khám mới, đồng thời bảo toàn đúng thông tin đã dùng ở các đợt khám
trước.

Thông tin cá nhân có thể thay đổi giữa các đợt khám. Vì vậy hệ thống không sửa
tại chỗ một hồ sơ đã được `HEX_ExamRecord` tham chiếu. Thay đổi tạo một phiên
bản `HEX_Patient` mới; người dùng quyết định phiên bản mới có trở thành hồ sơ
mặc định cho những lần tiếp theo hay không.

Thiết kế này thay thế quy tắc Patient canonical được cập nhật tại chỗ trong
`2026-09-11-registration-3nf-design.md`. Các quyết định khác của thiết kế 3NF,
đặc biệt các bảng BHYT, nghề nghiệp và thân nhân, vẫn giữ nguyên.

## 2. Phạm vi

### 2.1 Trong phạm vi

- Tìm hồ sơ đang active theo CCCD trong đúng `DivisionID`.
- Xác minh người bệnh bằng đồng thời CCCD, họ tên và ngày sinh.
- Điền trước thông tin cá nhân từ hồ sơ được chọn.
- Phát hiện thay đổi trên các trường cá nhân.
- Cho phép người dùng chọn có đặt thông tin mới làm hồ sơ active hay không.
- Giữ nguyên dữ liệu cá nhân của các đợt khám lịch sử.
- Backfill trạng thái active cho dữ liệu hiện có.

### 2.2 Ngoài phạm vi

- API hoặc màn hình tra cứu các phiên bản inactive.
- Tìm kiếm theo số điện thoại, mã người bệnh, mã HIS hoặc tìm kiếm gần đúng.
- Tự động gộp các hồ sơ nghi ngờ trùng nhau.
- Thay đổi `his-server`.
- Thay đổi nghiệp vụ version của BHYT, nghề nghiệp hoặc thân nhân.
- Sử dụng git worktree.

## 3. Quy tắc nhận diện và tìm kiếm

### 3.1 Phạm vi tenant

Mọi thao tác tìm kiếm, xác minh và chuyển active phải nằm trong `DivisionID`
của request hiện tại. Không nhận `DivisionID` tùy ý từ client để truy cập hồ sơ
của đơn vị khác.

### 3.2 Tìm kiếm

API tìm kiếm nhận CCCD đã được trim và chỉ trả hồ sơ có:

```text
DivisionID = current division
IdentityNumber = normalized input
IsActive = true
```

Không có kết quả được biểu diễn bằng danh sách rỗng để luồng đăng ký có thể tạo
hồ sơ mới. Trạng thái dữ liệu có nhiều hơn một hồ sơ active cho cùng CCCD là lỗi
xung đột, không được tự chọn một hồ sơ.

Kết quả chứa thông tin cá nhân cần thiết để điền trước form. Nhân viên tiếp nhận
hỏi lại người bệnh họ tên và ngày sinh; chỉ cho chọn lại hồ sơ khi đồng thời
khớp cả CCCD, họ tên và ngày sinh.

Họ tên được so sánh sau khi:

- trim đầu và cuối;
- gộp các khoảng trắng liên tiếp thành một khoảng trắng;
- so sánh không phân biệt chữ hoa và chữ thường;
- vẫn phân biệt dấu tiếng Việt.

Ngày sinh và CCCD phải khớp chính xác sau khi CCCD được trim. Không cần lưu thêm
cột tên chuẩn hóa vì truy vấn đã thu hẹp theo CCCD trước.

## 4. Mô hình phiên bản Patient

Tiếp tục dùng `HEX_Patient`; không tạo bảng snapshot riêng. Bổ sung:

- `IsActive`: phiên bản mặc định cho các lần khám tiếp theo;
- `PreviousPatientRefID`: nullable FK tự tham chiếu tới phiên bản nguồn.

Các trường được version hóa:

- `FullName`, `Dob`, `BirthYear`, `GenderID`;
- `IdentityNumber`, `IdentityIssuedDate`, `IdentityIssuerOptionID`;
- `PhoneNumber`, `Email`, `Address`;
- `EthnicityOptionID`, `BloodAboCode`, `BloodRhCode`.

Các khóa và metadata như `PatientRefID`, `HisPatientID`, `PatientCode`, audit
fields và trạng thái đồng bộ HIS không tham gia phép so sánh thay đổi thông tin
cá nhân.

Một phiên bản `Patient` đã được `ExamRecord` tham chiếu không bị sửa tại chỗ.
`ExamRecord.PatientRefID` tiếp tục là liên kết tới đúng phiên bản thông tin được
dùng tại đợt khám đó.

## 5. Luồng nghiệp vụ

### 5.1 Tạo đợt khám từ hồ sơ hiện có

1. Nhân viên nhập CCCD.
2. Client gọi API tìm hồ sơ active.
3. Nhân viên hỏi lại họ tên và ngày sinh; client chỉ cho chọn hồ sơ nếu đủ ba
   thành phần nhận diện khớp.
4. Client điền trước thông tin cá nhân và cho phép chỉnh sửa.
5. Request tạo đợt khám gửi `PatientRefID` hiện có, thông tin cá nhân cuối cùng và
   lựa chọn cập nhật hồ sơ mặc định khi lựa chọn này đã được hỏi.
6. Server tải lại hồ sơ nguồn trong transaction và kiểm tra đúng tenant, còn
   active và chưa bị thay thế.

### 5.2 Không có thay đổi

Nếu toàn bộ trường cá nhân giống hồ sơ nguồn, không tạo phiên bản mới.
`ExamRecord` mới dùng lại `PatientRefID`.

Nếu request không có `PatientRefID` vì không tìm thấy hồ sơ phù hợp, tạo hồ sơ
mới ở trạng thái active. Unique index vẫn ngăn tạo đồng thời hai hồ sơ active
cùng CCCD trong một tenant.

### 5.3 Có thay đổi nhưng chưa có quyết định

Nếu có trường thay đổi mà request chưa có lựa chọn, server trả:

```text
409 PatientProfileChanged
```

Response chứa danh sách tên trường thay đổi, không cần trả lại dữ liệu nhạy cảm
cũ và mới. Client hỏi người dùng có muốn dùng thông tin mới cho những lần khám
sau hay không rồi gửi lại request với lựa chọn rõ ràng.

### 5.4 Không đặt phiên bản mới làm mặc định

Khi `SetAsActiveProfile = false`:

- tạo phiên bản `Patient` mới với `IsActive = false`;
- đặt `PreviousPatientRefID` bằng hồ sơ nguồn;
- `ExamRecord` mới trỏ tới phiên bản inactive này;
- hồ sơ active hiện tại không đổi.

Phiên bản inactive chỉ phục vụ lịch sử của đợt khám và không xuất hiện trong API
tìm kiếm thông thường.

### 5.5 Đặt phiên bản mới làm mặc định

Khi `SetAsActiveProfile = true`, trong cùng transaction:

1. Chuyển hồ sơ nguồn thành inactive.
2. Tạo phiên bản mới ở trạng thái active và liên kết
   `PreviousPatientRefID` tới hồ sơ nguồn.
3. Cho `ExamRecord` mới trỏ tới phiên bản mới.

Nếu hồ sơ nguồn không còn active khi transaction thực thi, rollback và trả
`409`; client phải tìm lại hồ sơ hiện tại.

### 5.6 Sửa đợt khám đã có

Không sửa trực tiếp `Patient` đang được một hoặc nhiều đợt khám tham chiếu. Nếu
thông tin cá nhân của đợt khám bị sửa, tạo phiên bản riêng và đổi
`ExamRecord.PatientRefID` của đúng đợt khám đó. Việc có đặt phiên bản này làm
active hay không tuân theo cùng lựa chọn ở trên.

## 6. API contract

Tên route cuối cùng tuân theo convention hiện có của `HealthExam.API`; contract
nghiệp vụ tối thiểu là:

```http
GET /patients/search?identityNumber={CCCD}
```

Response chỉ chứa zero hoặc one hồ sơ active trong `DivisionID` hiện tại. DTO
trả `PatientRefID`, các trường cá nhân cần điền form và `IsActive`.

Request tạo/cập nhật đợt khám bổ sung:

- tái sử dụng `PatientRefID` nullable hiện có làm tham chiếu hồ sơ nguồn;
- `SetAsActiveProfile`: nullable boolean; chỉ bắt buộc khi server phát hiện
  thông tin cá nhân thay đổi.

Server là nơi tính diff và quyết định có cần tạo phiên bản mới; không tin cờ
`hasChanged` do client tự tính.

## 7. Ràng buộc dữ liệu và đồng thời

PostgreSQL bảo đảm tối đa một hồ sơ active theo CCCD trong mỗi tenant bằng
unique index có điều kiện trên `(DivisionID, IdentityNumber)` khi
`IsActive = true` và CCCD không rỗng.

Unique index hiện có trên `(DivisionID, HisPatientID)` được đổi thành unique có
điều kiện cho hồ sơ active có `HisPatientID > 0`. Các phiên bản inactive được
phép giữ cùng mã HIS để bảo toàn dữ liệu đã dùng ở đợt khám cũ.

Luồng hạ phiên bản cũ, tạo phiên bản mới và tạo/cập nhật `ExamRecord` chạy trong
cùng transaction. Unique index là lớp bảo vệ cuối cùng cho request đồng thời;
vi phạm được chuyển thành lỗi nghiệp vụ `409`, không trả lỗi database thô.

Hồ sơ không có CCCD vẫn được lưu theo luồng hiện tại nhưng không thể được tìm và
tái sử dụng bằng tính năng này.

## 8. Migration dữ liệu hiện có

Migration thực hiện theo thứ tự:

1. Thêm nullable `PreviousPatientRefID` và `IsActive` với giá trị khởi tạo an
   toàn cho quá trình backfill.
2. Gom hồ sơ theo `(DivisionID, IdentityNumber)` với CCCD không rỗng.
3. Trong mỗi nhóm, chọn hồ sơ gắn với `ExamRecord` có thời điểm khám gần nhất
   làm active.
4. Nếu không có hồ sơ nào từng được dùng, chọn hồ sơ có thời điểm cập nhật gần
   nhất; nếu vẫn bằng nhau, dùng `PatientRefID` làm tie-breaker ổn định.
5. Các hồ sơ còn lại trong nhóm chuyển thành inactive.
6. Hồ sơ có CCCD rỗng không tham gia nhóm tìm kiếm; giữ active để không thay đổi
   hành vi ghi hiện tại nhưng unique index bỏ qua chúng.
7. Thay index HIS hiện tại và tạo các filtered unique index mới sau khi backfill
   hoàn tất.

Migration phải chạy trong transaction và thất bại rõ ràng nếu dữ liệu không thể
xác định trạng thái theo các quy tắc trên; không xóa hoặc gộp hồ sơ.

## 9. Xử lý lỗi

- CCCD rỗng hoặc sai giới hạn định dạng: `400`.
- Không tìm thấy: `200` với danh sách rỗng.
- Không khớp đồng thời CCCD, họ tên và ngày sinh: không cho chọn/tái sử dụng hồ
  sơ; client tiếp tục luồng tạo mới.
- `PatientRefID` nguồn sai tenant, không tồn tại hoặc đã inactive: `409`.
- Có thay đổi nhưng thiếu quyết định: `409 PatientProfileChanged`.
- Xung đột unique do request đồng thời: rollback và trả `409`.

Không log đầy đủ CCCD hoặc toàn bộ payload thông tin cá nhân trong thông báo lỗi.

## 10. Kiểm thử

### 10.1 Application/domain

- Chuẩn hóa hoa/thường và khoảng trắng của họ tên nhưng vẫn phân biệt dấu.
- Chỉ xác minh thành công khi CCCD, họ tên và ngày sinh cùng khớp.
- Diff đúng trên toàn bộ trường cá nhân đã chốt và bỏ qua metadata.
- Không thay đổi thì dùng lại `PatientRefID`.
- Có thay đổi nhưng thiếu quyết định thì trả `PatientProfileChanged`.
- Chọn không cập nhật mặc định thì tạo phiên bản inactive.
- Chọn cập nhật mặc định thì tạo phiên bản active mới và hạ phiên bản cũ.
- Sửa đợt khám không làm thay đổi dữ liệu của đợt khám khác.

### 10.2 Persistence/API

- Tìm đúng hồ sơ active theo CCCD và `DivisionID`.
- Không trả hồ sơ inactive hoặc hồ sơ của tenant khác.
- Không tìm thấy trả danh sách rỗng.
- Migration chọn hồ sơ có đợt khám gần nhất; fallback theo thời điểm cập nhật.
- Đợt khám cũ vẫn đọc đúng phiên bản `Patient` đã tham chiếu.
- Hai request đồng thời không tạo được hai hồ sơ active cùng CCCD.
- Transaction rollback đầy đủ khi đổi active hoặc tạo `ExamRecord` thất bại.

## 11. Tiêu chí hoàn thành

- Nhân viên có thể tìm và điền lại hồ sơ active bằng CCCD.
- Hồ sơ chỉ được tái sử dụng sau khi họ tên và ngày sinh cùng khớp.
- Người dùng được hỏi trước khi thông tin thay đổi trở thành mặc định.
- Mỗi CCCD trong một `DivisionID` có tối đa một hồ sơ active.
- Thông tin cá nhân của các đợt khám cũ không thay đổi khi tạo hoặc chọn hồ sơ
  mặc định mới.
- Không có thay đổi nào trong `his-server`.
