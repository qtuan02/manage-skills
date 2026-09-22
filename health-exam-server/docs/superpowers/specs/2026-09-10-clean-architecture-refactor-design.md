# Thiết kế refactor Health Exam Server theo Clean Architecture

**Ngày:** 2026-09-10
**Trạng thái:** Đã thống nhất thiết kế, chưa triển khai

## 1. Mục tiêu

Refactor toàn bộ dự án sang Clean Architecture để tách business rules, orchestration nghiệp vụ và chi tiết công nghệ đang bị trộn trong các service hiện tại.

Kiến trúc đích gồm bốn project:

- `HealthExam.Domain`
- `HealthExam.Application`
- `HealthExam.Infrastructure`
- `HealthExam.API`

Sau khi migration hoàn tất, các project `HealthExam.Core`, `HealthExam.Server` và `HealthExam.Utils` sẽ bị xóa.

## 2. Ràng buộc bắt buộc

- Giữ nguyên toàn bộ API contract: route, HTTP method, headers, authentication, request/response JSON, HTTP status, error code, payload, trace ID và Swagger contract.
- Giữ nguyên database schema, tên bảng/cột, enum numeric value, migration history và dữ liệu hiện hữu.
- Giữ nguyên business behavior, transaction boundary, concurrency behavior, idempotency, retry và timestamp semantics.
- Domain và Application chỉ dùng BCL/.NET; không tham chiếu EF Core, Npgsql, Newtonsoft, EPPlus, ASP.NET, HTTP client, cache, hosting hoặc DI framework.
- Refactor theo từng vertical slice và chỉ build/test local trong giai đoạn này; chưa có yêu cầu deploy từng slice.
- PostgreSQL thật là bắt buộc cho Infrastructure integration tests liên quan đến hành vi provider-specific.
- Không xây framework nội bộ hoặc cấu trúc folder nặng tính giáo khoa nếu chưa có nhu cầu thực tế.

## 3. Dependency rule

```text
HealthExam.Domain
    ^
HealthExam.Application
    ^
HealthExam.Infrastructure

HealthExam.API Controllers/Middleware -> HealthExam.Application
HealthExam.API Program.cs             -> HealthExam.Application + HealthExam.Infrastructure
```

- Domain không tham chiếu project nào.
- Application chỉ tham chiếu Domain.
- Infrastructure tham chiếu Application và Domain để hiện thực các interface.
- Controller và middleware nghiệp vụ trong API chỉ gọi Application.
- `Program.cs` là composition root duy nhất được phép biết Infrastructure để nối interface với implementation.
- Infrastructure không tham chiếu API.

Entity, value object và DTO là concrete type. Chỉ hành vi cần đảo dependency hoặc cần thay thế trong test mới dùng interface; không ép mọi class thành interface.

## 4. Tổ chức project

```text
HealthExam.Domain/
  Common/
  ExamRecords/
  ExamSessions/
  Imports/
  Webhooks/
  Paraclinical/

HealthExam.Application/
  Common/
  ExamRecords/
  ExamSessions/
  Imports/
  Webhooks/
  Integrations/

HealthExam.Infrastructure/
  Persistence/
    Configurations/
    Repositories/
    Migrations/
  Integrations/
    FormServer/
    HisEmr/
    Ris/
  Excel/
  Caching/
  BackgroundJobs/

HealthExam.API/
  Controllers/
  Contracts/
  Middlewares/
```

Trong Application, command/query, handler, result và interface liên quan được đặt gần feature sử dụng chúng. Không tạo các cây folder `InputPorts`, `OutputPorts`, `Adapters` hoặc nhiều tầng phân loại nhỏ.

## 5. Domain layer

Domain giữ business rules và invariant; Application giữ orchestration của use case.

### 5.1 Ranh giới nghiệp vụ

- `ExamSession`: rule đóng/mở lại đợt, trạng thái hợp lệ và điều kiện không cho thao tác.
- `ExamRecord`: rule xác nhận đăng ký, hủy đăng ký/hủy khám và cập nhật trạng thái từ webhook hoặc reconciliation.
- `ParaclinicalOrder` và item: state machine, idempotency, điều kiện hủy và các rule phục vụ ký kết luận.
- `ImportBatch`: vòng đời `Pending -> Committing -> Completed/Discarded` và các chuyển trạng thái hợp lệ.
- `WebhookInbox` và `IntegrationOutbox`: chỉ chứa rule vòng đời như retry/dead-letter; locking, dequeue và SQL không thuộc Domain.
- Catalog như Organization, ExamPackage và MasterData chủ yếu là entity dữ liệu; không thêm behavior khi chưa có rule thực tế.

### 5.2 Nguyên tắc mô hình

- Chuyển state machine và invariant hiện đang nằm trong service vào method/rule thuần của Domain.
- Domain không biết numeric API error code. Rule trả failure có tên nghiệp vụ, ví dụ `SessionAlreadyClosed`, `RecordCannotBeCancelled`, `InvalidTransition`.
- Không tạo value object cho mọi `Guid` hoặc `string`; chỉ tạo khi giá trị có validation hoặc behavior dùng lại.
- Không thêm domain-event framework trong đợt refactor này. Audit và outbox được Application handler gọi rõ ràng trong transaction tương ứng.
- Domain entity được EF Core map bằng Fluent Configuration trong Infrastructure; Domain không chứa EF/JSON attributes và không cần persistence model trùng lặp.

## 6. Application layer

Mỗi thao tác là một handler độc lập. Ví dụ:

```text
Application/ExamRecords/
  CreateExamRecord.cs
  UpdateExamRecord.cs
  ConfirmExamRecord.cs
  CancelExamRecord.cs
  GetExamRecord.cs
  ListExamRecords.cs
  IExamRecordRepository.cs
```

Một file use case có thể chứa command/query, handler, result model và kiểu nhỏ chỉ được use case đó sử dụng.

### 6.1 Luồng command

1. Handler nhận command chứa input cùng `ActorId` và `DivisionId` thuần .NET.
2. Handler tải aggregate qua repository chuyên biệt.
3. Handler gọi business rule trong Domain.
4. Handler ghi aggregate, audit và outbox cần thiết.
5. Handler commit transaction.
6. Handler trả `ApplicationResult<T>` hoặc typed failure.

### 6.2 Interface và dữ liệu

- Không expose `IQueryable` khỏi Infrastructure.
- Command handler làm việc với Domain aggregate.
- Query handler dùng read repository trả projection do Application định nghĩa.
- Repository được thiết kế theo aggregate/use case, không dùng generic repository cho mọi entity.
- External integrations có interface theo nhu cầu thực tế, ví dụ `IFormServerClient`, `IHisEmrClient`, `IRisClient`, `IExamWorkbookReader`, cache interface chuyên biệt và `IClock`.
- Model đi qua interface là kiểu thuần .NET. Infrastructure tự xử lý HTTP, JSON, Excel và vendor contract.
- Không dùng mediator hoặc dispatch framework. Controller inject trực tiếp interface của handler cần gọi.
- Application không tham chiếu DI framework; composition root đăng ký handler.

### 6.3 Validation

- API/Application xử lý validation về hình thức input và dữ liệu cần thiết cho use case.
- Domain xử lý invariant, state transition và business rule.

## 7. Infrastructure layer

### 7.1 Persistence

- `HealthExamDbContext`, Fluent Configurations và toàn bộ migrations hiện tại được chuyển sang Infrastructure.
- Không sinh migration thay đổi schema trong đợt refactor.
- Command repository materialize và lưu Domain aggregate.
- Read repository dùng EF projection trực tiếp sang Application query result.
- PostgreSQL-specific behavior được giấu sau interface có tên theo ý nghĩa: record locking, queue batch claiming bằng `SKIP LOCKED`, sequence allocation, savepoint và bulk import.
- Expected unique/concurrency outcomes được chuyển thành kết quả trung lập do interface định nghĩa; handler ánh xạ chúng thành typed failure.
- Unexpected technical exceptions tiếp tục đi ra đường lỗi 5000.

### 7.2 Tích hợp ngoài

- HTTP adapter sở hữu URL, headers, timeout, authentication, JSON serialization và vendor envelope.
- Adapter ánh xạ dữ liệu ngoài sang model thuần của Application.
- Newtonsoft chỉ tồn tại trong Infrastructure/API; EPPlus chỉ tồn tại trong `Infrastructure/Excel`.
- Cache implementation nằm trong Infrastructure; Application không biết `IMemoryCache`.

### 7.3 Background jobs

- `BackgroundService`, lịch chạy, retry scheduling và host lifecycle nằm trong Infrastructure.
- Worker chỉ thực hiện vòng lặp kỹ thuật và gọi Application handler như process webhook batch, process outbox batch hoặc reconcile progress.
- Business processing của webhook, outbox và reconciliation nằm trong Domain/Application.
- Operational logging nằm tại API/Infrastructure boundaries. Không tạo logging abstraction riêng trong Domain/Application; audit nghiệp vụ vẫn được ghi qua interface trong transaction.

## 8. API layer

- Giữ nguyên routes, HTTP methods, query/body fields, headers, status codes, Swagger và response envelope `{ ErrorCode, Message, Data, TraceID }`.
- Request/response DTO hiện hữu được chuyển vào `API/Contracts` và giữ serialization behavior.
- Controller chỉ map request thành command/query, gọi một Application handler và map result thành response.
- Controller không truy cập repository, DbContext, HTTP client hoặc chứa business rule.
- Failure mapper tập trung ánh xạ typed failure sang numeric `ErrorCode`, message, payload và HTTP status hiện tại.
- Middleware authentication, division, webhook HMAC, vendor callback và trace ID tiếp tục thuộc API; thứ tự pipeline được giữ nguyên.
- `ActorId`, `DivisionId` và credential cần chuyển tiếp được lấy từ request đã xác thực rồi đưa vào command dưới dạng kiểu thuần. Application không biết `HttpContext`.
- Raw request body dùng cho webhook signature được xử lý tại API boundary.
- Health checks, Swagger, JWT và Serilog tiếp tục thuộc API.

## 9. Result và error flow

Luồng lỗi dự kiến:

```text
Domain rule failure
  -> Application typed failure
  -> API ErrorCode + HTTP status + envelope

Expected persistence/integration result
  -> Application typed failure
  -> API contract hiện tại

Unexpected exception
  -> Exception middleware
  -> ErrorCode 5000
```

Infrastructure không để `DbUpdateException`, `PostgresException`, HTTP exception hoặc EPPlus exception lọt vào các luồng lỗi dự kiến. Failure payload qua Application chỉ dùng kiểu thuần; API chịu trách nhiệm serialize đúng contract hiện tại.

## 10. Transaction và concurrency

- Transaction được mở rõ ràng trong handler cần atomicity; không thêm generic transaction middleware.
- Aggregate update, audit và outbox commit trong cùng transaction khi hành vi hiện tại yêu cầu như vậy.
- Repository chuyên biệt kiểm soát transaction/connection cần cho row lock và queue claim.
- Giữ nguyên tính chất không rollback của PostgreSQL sequence khi cấp mã.
- Giữ nguyên savepoint và cleanup tracking của import/duplicate race; chi tiết EF nằm trong Infrastructure.
- Không giữ database transaction qua HTTP call nếu luồng hiện tại không làm vậy.
- Retry chỉ áp dụng tại worker hoặc integration operation đã có idempotency tương ứng.

## 11. Testing

Phạm vi test của refactor giới hạn ở bốn nhóm:

1. **Domain tests:** toàn bộ state transition, invariant, idempotency và domain failure; không mock framework hoặc database.
2. **Application tests:** fake repository/integration interface để kiểm tra handler điều phối transaction, audit, outbox và typed result.
3. **Infrastructure integration tests:** PostgreSQL thật, bắt buộc phủ `FOR UPDATE`, `SKIP LOCKED`, sequence, savepoint, unique constraint, retry/dead-letter và transaction rollback. EF InMemory không được dùng để chứng minh các hành vi này.
4. **API contract tests:** khởi chạy API và kiểm tra route, authentication, middleware order, request/response JSON, HTTP status, error code, payload và Swagger không đổi.

Không bổ sung các nhóm test chuyên biệt khác trong scope hiện tại.

## 12. Chiến lược migration

Áp dụng phương án foundation trước, sau đó migrate theo vertical slice. Vì chưa deploy từng phần, không duy trì hai DbContext hoặc compatibility architecture phức tạp.

### 12.1 Foundation

- Tạo Domain, Application và Infrastructure.
- Di chuyển entity/enums theo hướng cơ học, chưa đổi behavior.
- Di chuyển DbContext, configurations và migrations.
- Tạo `ApplicationResult`, failure types và API failure mapper.

### 12.2 Thứ tự vertical slice

1. Catalog và Master Data: Organization, ExamPackage, MasterData, ParaclinicalCatalog.
2. Exam Session: CRUD, close và reopen.
3. Exam Record: list/get/create/update/confirm/cancel, record-code allocation, duplicate handling, audit, portal credential verification và session progress.
4. Import: upload/parse/validate/commit/discard, batch lifecycle, idempotency, bulk write, savepoint và stale-batch recovery.
5. Paraclinical và vendor integration: order, item state machine, conclusion, RIS, vendor callback, integration outbox và worker.
6. Form-server workflows: form draft, webhook ingest/process, metrics, reconciliation và workers.
7. HIS EMR: form definition, exam process/sign workflow, cache và HTTP client.
8. Cleanup: xóa implementation cũ và ba project cũ; chạy đủ bốn nhóm test.

### 12.3 Quy trình cho mỗi slice

1. Khóa hành vi chính bằng test tối thiểu thuộc bốn nhóm đã duyệt.
2. Chuyển business rule sang Domain.
3. Tạo command/query, handler, result và interface chuyên biệt trong Application.
4. Viết Infrastructure implementation.
5. Chuyển controller hoặc worker sang handler mới.
6. Chạy test của slice và test contract liên quan.
7. Xóa implementation cũ của slice.

## 13. Tiêu chí hoàn thành

- Solution chỉ còn Domain, Application, Infrastructure, API và Tests.
- Domain/Application không có package hoặc project reference vi phạm dependency rule.
- Controller và business middleware không gọi Infrastructure trực tiếp.
- Không còn EF Core, Npgsql, HTTP, Newtonsoft, EPPlus, cache hoặc hosting type trong Domain/Application.
- Không còn `IQueryable` trong public interface của Application.
- Business rules đã được chuyển vào Domain; handler Application chỉ orchestration.
- Mọi endpoint và worker hiện hữu chạy qua handler mới.
- Database schema và API contract không thay đổi.
- Bốn nhóm test đã duyệt đều pass, bao gồm Infrastructure integration tests trên PostgreSQL thật.
- `HealthExam.Core`, `HealthExam.Server` và `HealthExam.Utils` đã bị xóa khỏi solution và filesystem.

## 14. Ngoài phạm vi

- Thêm endpoint hoặc business feature mới.
- Thay đổi API contract hoặc database schema.
- Deploy production, rollout hoặc compatibility bridge giữa hai kiến trúc.
- Thêm mediator, event bus/domain-event framework, generic repository hoặc logging abstraction riêng.
- Mở rộng test ngoài bốn nhóm đã thống nhất.
