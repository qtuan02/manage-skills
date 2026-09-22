# Đồng bộ code mới từ `main` vào Clean Architecture

## Trạng thái

Đã được duyệt trong phiên brainstorming ngày 2026-09-12. Đây là thiết kế cho code phase; chưa thực hiện port code.

## Bối cảnh

Branch hiện tại `feat-refactor-clean-architecture` đã chuyển hệ thống sang bốn lớp:

- `HealthExam.Domain`
- `HealthExam.Application`
- `HealthExam.Infrastructure`
- `HealthExam.API`

Branch `main` đang chứa các thay đổi mới hơn nhưng phần lớn vẫn tổ chức theo `HealthExam.Core` và `HealthExam.Server`. Hai branch có điểm tách tại commit `3e51a34` và đã lệch đáng kể. Vì vậy merge nguyên xi `main` có thể kéo lại layout legacy, làm mất ranh giới Clean Architecture và tạo xung đột trên phần lớn feature.

Mục tiêu là đồng bộ behavior, API contract và schema artifact mới từ `main` vào branch refactor; không đồng bộ nguyên trạng cấu trúc file legacy.

## Mục tiêu

- Đưa toàn bộ behavior/code cần thiết mới hơn từ `main` vào branch Clean Architecture.
- Giữ branch hiện tại làm nguồn chuẩn cho dependency direction, composition root và public handler boundary.
- Giữ route, HTTP method, request/response contract, error mapping và behavior hiện hữu trừ capability mới được lấy từ `main`.
- Đưa các migration/backfill/verify SQL từ `main` sang đúng vị trí, giữ nguyên migration ID và schema semantics.
- Ưu tiên code phase trước; tài liệu, README, Dockerfile, seed/deploy phụ trợ chỉ xử lý sau nếu cần.

## Ngoài phạm vi phase này

- Không merge toàn bộ `main` vào branch hiện tại.
- Không replay nguyên xi các commit feature viết cho `HealthExam.Core`/`HealthExam.Server`.
- Không regenerate migration bằng EF snapshot khác.
- Không chạy lại migration, backfill hoặc verify SQL trên database.
- Không cập nhật `README`, Dockerfile, seed/deploy phụ trợ nếu không phải điều kiện để code compile/chạy.
- Không sửa hoặc commit `node_modules/`, `package.json`, `pnpm-lock.yaml`.

## Nguyên tắc nguồn

| Nội dung | Nguồn chuẩn |
|---|---|
| Domain/Application/Infrastructure/API boundary | Branch hiện tại |
| Behavior, API capability và bug fix mới | `main` |
| Route/contract đang tồn tại | Branch hiện tại, trừ endpoint mới từ `main` |
| Migration ID, operation, table/column và SQL semantics | `main` |
| Composition root và dependency registration | Branch hiện tại |
| Test helper/harness | Branch hiện tại |

`main` chỉ là nguồn tham chiếu behavior. Khi file trùng logic, giữ file ở layer hiện tại và port phần behavior cần thiết vào đó.

## Quy trình tích hợp

### 1. Tạo integration branch và lập inventory

Tạo branch integration từ `feat-refactor-clean-architecture`; không thay đổi branch làm việc gốc trong lúc port.

Dùng merge-base `3e51a34` để lập bảng:

```text
main commit -> capability/behavior -> source files -> target slice -> status
```

Mỗi mục được đánh dấu một trong ba trạng thái:

- đã có tương đương: chỉ audit/parity, không port trùng;
- chưa có: cần port;
- khác behavior: port phần behavior mới vào implementation hiện tại.

Các file untracked hiện tại được giữ nguyên và không đưa vào commit tích hợp.

### 2. Port registration 3NF và persistence

Đây là nhóm nền vì các flow registration và HIS admission phụ thuộc vào nó.

- Port các patient model/aggregate và quan hệ employment, insurance, relative vào Domain theo model hiện tại; loại bỏ phụ thuộc EF khỏi inner layer.
- Bổ sung EF mapping, DbContext model và repository query vào Infrastructure.
- Chép các migration 3NF, migration registration place, migration drop legacy columns và SQL backfill/verify từ `main` sang thư mục hiện tại.
- Giữ nguyên migration ID, operation, tên bảng/cột và semantics. Chỉ đổi namespace/path cần thiết để migration compile trong Infrastructure.
- Không chạy lại migration/backfill/verify SQL.

### 3. Port registration behavior

Đưa behavior của `PatientService` và `Registration3NfConsistencyService` vào các use case registration hiện hữu, chủ yếu là `CreateExamRecord`, `UpdateExamRecord` và flow lưu registration form.

Chỉ thêm repository port hoặc collaborator khi nhiều use case thật sự cần chung behavior. Không tạo lại generic service/mediator chỉ để giữ tên hoặc shape của legacy service.

Giữ các invariant quan trọng:

- đồng bộ patient và master-data reference trước khi tạo admission;
- lưu snapshot/subject/place theo model normalized;
- không làm thay đổi actor, division, transaction và idempotency semantics.

### 4. Port HIS admission và behavior phụ thuộc

Port `HisPatientService`, `HisAdmissionService` và các fix liên quan vào Application integration port và Infrastructure HIS adapter hiện hữu.

Giữ các behavior mới của `main`, gồm:

- đồng bộ normalized patient trước HIS admission;
- tạo hoặc khôi phục admission khi patient link thiếu;
- giữ liên kết admission trên exam record;
- không đưa vendor wire model, HTTP request hoặc EF row vào Application.

### 5. Port form/PDF và capability còn thiếu

Sau khi registration data flow ổn định, đối chiếu và port những capability `main` mà branch hiện tại chưa có:

- save/hydration HIS form section;
- ICD-10 catalog và cache;
- employee departments;
- các fix PDF render label/text;
- các fix HIS form, cấu hình và fallback còn thiếu.

Endpoint mới dùng controller hiện tại và concrete Application handler. Controller không được gọi Infrastructure trực tiếp ngoài composition root.

### 6. Port test code cần thiết

Chuyển các assertion behavior cần thiết từ test của `main` sang test harness hiện tại:

- Domain/Application: normalized patient, registration consistency và admission decision;
- Infrastructure: mapping, repository query và adapter contract;
- API: route, payload, error mapping và composition root.

Không port các test chỉ nhằm xác nhận migration/backfill/verify SQL đã chạy, vì các artifact này được tin cậy và không retest trong phase này.

## Quy tắc xử lý xung đột

- Cùng route nhưng khác implementation: giữ contract hiện tại, lấy behavior mới của `main`.
- Cùng domain nhưng khác model: giữ model/domain boundary hiện tại, chuyển logic vào Domain/Application.
- Cùng persistence nhưng khác path: giữ Infrastructure path hiện tại, port operation và schema semantics của `main`.
- Cùng test nhưng khác helper: giữ test harness hiện tại, chuyển assertion quan trọng.
- Không có tương đương: thêm vertical slice nhỏ nhất vào layer phù hợp.
- Nếu một bug có nhiều caller, sửa tại boundary chung thay vì vá từng controller.

## Gate kiểm tra code

Sau mỗi slice:

1. `dotnet build HealthExamServer.sln`.
2. Chạy test trực tiếp của slice vừa port.
3. Chạy architecture/dependency test để đảm bảo không có dependency ngược.
4. Kiểm tra composition root resolve được handler/adapter mới.

Cuối code phase:

- build solution thành công;
- test Domain/Application/API/Infrastructure liên quan đạt;
- không có dependency mới từ inner layer tới EF, ASP.NET, HTTP vendor hoặc legacy project;
- API contract và error mapping không bị đổi ngoài capability mới;
- migration/backfill/verify SQL có mặt đúng path và giữ nguyên semantics, nhưng không được chạy lại;
- diff còn lại với `main` được giải thích trong inventory, không còn feature code mới chưa phân loại.

## Rollback và an toàn

- Mọi thay đổi code thực hiện trên integration branch.
- Mỗi vertical slice có commit riêng để có thể revert độc lập.
- Không dùng destructive Git command trên branch làm việc.
- Nếu phát hiện 3NF hoặc HIS behavior vượt quá một slice, dừng slice đó, cập nhật inventory và tách thành một slice nhỏ hơn; không kéo legacy service vào để “cho chạy nhanh”.

## Kết quả bàn giao

Kết quả của phase này là một integration branch chứa code parity với `main` trong kiến trúc Clean Architecture, kèm migration/backfill/verify SQL được port nguyên semantics nhưng chưa thực thi lại. README, Dockerfile, seed/deploy và tài liệu sẽ là phase tiếp theo nếu cần.
