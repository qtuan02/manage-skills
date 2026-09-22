# Thiết kế hoàn thiện Pragmatic Clean Architecture cho Health Exam Server

**Ngày:** 2026-09-11  
**Trạng thái:** Đã thống nhất thiết kế, chờ duyệt tài liệu  
**Thiết kế nền:** `docs/superpowers/specs/2026-09-10-clean-architecture-refactor-design.md`

## 1. Bối cảnh

Đợt refactor trước đã tạo đúng bốn layer `Domain`, `Application`, `Infrastructure` và `API`, loại bỏ các project legacy, cô lập phần lớn framework và thiết lập dependency direction đi vào trong. Tuy nhiên, kết quả audit sau triển khai cho thấy một số boundary vẫn chưa đạt thiết kế đã duyệt:

- Domain chứa numeric API error code và vendor-oriented failure.
- Application port của HIS còn lộ HTTP route, method, header và raw JSON.
- Một số state transition và invariant vẫn được thực hiện trực tiếp trong Application.
- State quan trọng của Domain còn public setter nên có thể bỏ qua domain behavior.
- API middleware tham chiếu Infrastructure ngoài composition root.
- Application context còn khái niệm request header.
- Clock implementation và việc lấy thời gian chưa được cô lập nhất quán.
- Architecture tests chưa phát hiện được các vi phạm trên.
- Mỗi use case đang có cả handler interface và handler implementation dù chỉ có một cách triển khai.

Tài liệu này bổ sung cho thiết kế nền, không thay thế các ràng buộc tương thích và quyết định vẫn còn hiệu lực trong tài liệu đó.

## 2. Mục tiêu

- Domain thực sự sở hữu business rule, invariant, idempotency và state transition.
- Application chỉ điều phối một hành động ứng dụng qua concrete handler cho từng use case.
- Interface chỉ tồn tại tại boundary cần dependency inversion hoặc fake trong test.
- Infrastructure sở hữu toàn bộ chi tiết PostgreSQL, HTTP, vendor wire contract, cache, clock và host lifecycle.
- API chỉ gọi Application; `Program.cs` là nơi duy nhất trong API được biết Infrastructure.
- Giữ nguyên API contract, database schema, migration history và observable behavior.
- Bổ sung test và architecture gate đủ để ngăn các boundary vừa sửa bị tái vi phạm.

## 3. Ngoài phạm vi

- Không thêm endpoint, business feature hoặc workflow mới.
- Không thay đổi route, authentication, request/response JSON, HTTP status, error code, payload, trace ID hoặc Swagger contract.
- Không thay đổi database schema, table/column, enum numeric value hoặc migration hiện có.
- Không thêm mediator, generic repository, domain-event framework, transaction middleware hoặc logging abstraction cho core.
- Không tạo cây folder giáo khoa như `InputPorts`, `OutputPorts` hoặc `Adapters`.
- Không deploy; chỉ thay đổi code và kiểm thử local.

## 4. Kiến trúc đích

```text
HealthExam.Domain
    ^
HealthExam.Application
    ^
HealthExam.Infrastructure

HealthExam.API controllers/middleware -> HealthExam.Application
HealthExam.API Program.cs              -> HealthExam.Application + HealthExam.Infrastructure
```

### 4.1 Domain

Domain sở hữu:

- State transition và điều kiện chuyển trạng thái.
- Invariant của aggregate/entity.
- Idempotency có ý nghĩa nghiệp vụ.
- Failure có tên theo ngôn ngữ nghiệp vụ.

Domain không sở hữu:

- Numeric API error code, HTTP status hoặc response payload.
- Transaction, repository, audit/outbox orchestration.
- Database locking, queue claiming hoặc retry scheduling.
- Vendor route, HTTP verb, header, raw JSON hoặc vendor error envelope.

Các state quan trọng không còn public setter. EF Core map chúng bằng Fluent Configuration và có thể sử dụng private setter/backing field phù hợp. Mọi thay đổi trạng thái hợp lệ phải đi qua domain behavior có tên rõ ràng.

### 4.2 Application

Mỗi use case có một concrete handler riêng. API và worker inject trực tiếp concrete handler thay vì interface một-implementation.

Ví dụ:

```text
ConfirmExamRecordController
  -> ConfirmExamRecordHandler
      -> IExamRecordRepository
      -> IAuditRepository
      -> IUnitOfWork
      -> IClock
```

Application giữ interface cho các dependency cần đảo chiều hoặc thay thế:

- Repository/read repository chuyên biệt.
- Transaction/unit of work.
- Audit và outbox persistence.
- Clock.
- Cache abstraction khi use case cần cache semantics.
- External integration port theo operation/nghiệp vụ.

Application không tạo `I*Handler` mặc định. Ngoại lệ chỉ được chấp nhận khi có từ hai implementation thực sự hoặc boundary thay thế có lý do cụ thể được ghi nhận trong code/design.

Handler chịu trách nhiệm:

1. Validate input ở mức use case.
2. Tải aggregate/projection qua port chuyên biệt.
3. Gọi domain behavior.
4. Điều phối integration, audit và outbox.
5. Commit hoặc rollback transaction đúng boundary hiện tại.
6. Trả typed `ApplicationResult`.

Handler không được gán trực tiếp state được Domain bảo vệ, dựng HTTP request, phân tích vendor JSON hoặc truy cập request header.

### 4.3 Infrastructure

Infrastructure hiện thực các port do Application cần và sở hữu:

- EF Core, Npgsql, PostgreSQL SQL và transaction implementation.
- `FOR UPDATE`, `SKIP LOCKED`, sequence, savepoint và unique/concurrency translation.
- HTTP client, base URL, route, method, authentication header, timeout và serialization.
- Vendor request/response/envelope mapping.
- Cache implementation.
- `SystemClock` dùng thời gian hệ thống.
- Background service, polling và retry scheduling.

Expected technical outcomes được chuyển thành result trung lập trước khi đi vào Application. Unexpected exception tiếp tục đi theo lỗi hệ thống hiện tại.

### 4.4 API

Controller và middleware nghiệp vụ chỉ tham chiếu Application và các type thuộc API. `Program.cs` là composition root duy nhất được tham chiếu Infrastructure để đăng ký implementation.

API chịu trách nhiệm:

- HTTP request/response mapping.
- Authentication, authorization và middleware pipeline.
- Lấy actor, division, credential và trace từ request rồi truyền vào command/query dưới dạng dữ liệu rõ nghĩa.
- Ánh xạ semantic failure sang numeric error code, HTTP status, message và payload hiện hữu.
- Giữ nguyên Swagger và serialization contract.

Vendor callback options dùng trong middleware phải là API-owned configuration hoặc abstraction trung lập; middleware không inject Infrastructure options trực tiếp.

## 5. Domain state và invariant

### 5.1 Aggregate cần siết state

- `ExamSession`: close, reopen và các trạng thái không cho phép thao tác.
- `ExamRecord`: confirm, cancel, webhook transition và reconciliation transition.
- `ImportBatch`: `Pending -> Committing -> Completed/Discarded`, stale claim và idempotent completion.
- `ParaclinicalOrder`/item: order lifecycle, result completion, cancel và conclusion eligibility.
- `WebhookInbox`: receive, process, retry và dead-letter lifecycle có ý nghĩa nghiệp vụ.
- `IntegrationOutbox`: pending, processing, succeeded, retry và dead-letter lifecycle.

Mỗi behavior trả kết quả thuần hoặc semantic failure. Application không lặp lại transition table; nó chỉ chuyển failure thành `ApplicationResult`.

### 5.2 Persistence compatibility

Việc đóng setter không được làm thay đổi schema hoặc migration. Infrastructure configuration phải tiếp tục materialize được entity từ dữ liệu hiện hữu. Nếu EF cần constructor, private setter hoặc backing field, lựa chọn nhẹ nhất được dùng và được khóa bằng PostgreSQL integration test liên quan.

## 6. Error flow

```text
Domain behavior
  -> Domain result / semantic domain failure
  -> ApplicationResult
  -> API failure mapper
  -> HTTP status + numeric ErrorCode + payload hiện tại
```

- Domain failure dùng tên nghiệp vụ như `SessionAlreadyClosed`, `InvalidRecordTransition` hoặc `ImportBatchExpired`.
- Application có thể bổ sung application-specific failure cho authorization, orchestration hoặc external operation outcome.
- Infrastructure chuyển lỗi dự kiến của PostgreSQL/vendor thành persistence/integration outcome trung lập.
- API là nơi duy nhất sở hữu mapping sang numeric contract.
- Unexpected exception tiếp tục được exception middleware ánh xạ thành error code `5000` theo contract hiện tại.

## 7. Integration ports

Port không được mang hình dạng transport chung như `RelativePath`, HTTP `Method`, raw request/response JSON hoặc arbitrary header lookup.

HIS/FormServer/RIS ports được thiết kế theo operation thực tế, ví dụ:

```csharp
Task<HisResult<FormDefinition>> GetActiveFormDefinitionAsync(
    string templateCode,
    IntegrationCredential credential,
    RequestMetadata metadata,
    CancellationToken cancellationToken);
```

Tên và granularity cuối cùng bám theo từng use case hiện hữu; không tạo một client khổng lồ hoặc một method `SendAsync` tổng quát. Model đi qua boundary là kiểu .NET có ý nghĩa với Application. Infrastructure tự xử lý URL, verb, query string, header, serialization và vendor response shape.

Credential vẫn có thể đi qua command/port khi đó là dữ liệu cần thiết của use case, nhưng Application không được biết tên HTTP header chứa credential.

## 8. Request context và clock

- Loại `GetRequestHeader(string name)` khỏi Application context.
- Controller/API adapter truyền rõ `ActorId`, `ActorKind`, `DivisionId`, `TraceId`, scope và credential cần thiết vào command/query.
- Không tạo abstraction request chung mô phỏng `HttpContext` trong Application.
- `IClock` tiếp tục thuộc Application vì handler/domain operation cần khái niệm thời gian.
- Concrete `SystemClock` chuyển sang Infrastructure.
- Handler bắt buộc nhận `IClock` qua constructor nếu dùng thời gian; không dùng `new SystemClock()` hoặc `DateTime.UtcNow` trực tiếp trong Application.
- API/Infrastructure có thể dùng system time trực tiếp cho thuần transport/operational timestamps nếu không ảnh hưởng business behavior.

## 9. Architecture enforcement

Architecture tests phải kiểm tra tối thiểu:

- Domain không có package/project reference và không chứa framework/vendor/API concepts.
- Application chỉ tham chiếu Domain và không chứa framework/transport types.
- Không có public Application interface theo mẫu handler một-implementation.
- Application interfaces không expose `IQueryable`, expression hoặc persistence-bound type.
- API source ngoài `Program.cs` không import namespace Infrastructure.
- Infrastructure không tham chiếu API.
- State property được bảo vệ không có public setter.
- Application không gán trực tiếp các state property đó.
- HIS/FormServer/RIS port không chứa route, HTTP method, raw JSON hoặc arbitrary request header contract.

Các rules nên kiểm tra semantic structure bằng reflection hoặc source inspection có scope rõ; tránh test dựa trên từ khóa quá rộng gây false positive.

## 10. Testing

Chỉ duy trì bốn nhóm test đã duyệt.

### 10.1 Domain tests

- Phủ toàn bộ state transition hợp lệ và không hợp lệ.
- Phủ invariant, idempotency và semantic failure.
- Chứng minh caller không thể thay đổi state ngoài domain behavior.
- Không mock framework, database hoặc network.

### 10.2 Application tests

- Khởi tạo concrete handler với fake repository/integration ports.
- Kiểm tra handler gọi domain behavior và điều phối transaction đúng thứ tự.
- Kiểm tra audit/outbox cùng transaction khi contract yêu cầu.
- Kiểm tra rollback/không commit trên expected failure.
- Kiểm tra clock được sử dụng deterministic.
- Kiểm tra typed result và integration outcome mapping.

### 10.3 Infrastructure integration tests

Chạy trên PostgreSQL thật qua `HEALTHEXAM_TEST_DB`; thiếu cấu hình phải làm suite thất bại thay vì skip. Bắt buộc phủ:

- `FOR UPDATE`.
- `SKIP LOCKED`.
- Sequence semantics.
- Savepoint behavior.
- Unique constraint/concurrency translation.
- Retry/dead-letter persistence.
- Transaction rollback.
- EF materialization sau khi đóng public state setter.

EF InMemory không được dùng để chứng minh các hành vi provider-specific này.

### 10.4 API contract tests

Khởi chạy API và khóa:

- Route và HTTP method.
- Authentication/authorization.
- Middleware order.
- Request/response JSON và response envelope.
- HTTP status, numeric error code, message và payload.
- Trace ID và Swagger contract.
- Vendor callback authentication sau khi options không còn thuộc Infrastructure.

## 11. Trình tự triển khai theo vertical slice

### Slice 1: Foundation và architecture gates

- Chuyển `I*Handler` thành concrete handler.
- Điều chỉnh DI, controller, worker và application tests.
- Thêm architecture tests cho handler, dependency, transport port và state mutation boundaries.

### Slice 2: Shared error, context và clock boundaries

- Thay Domain `HealthExamException` bằng semantic failures.
- Hoàn thiện Application/API failure mapping mà không đổi contract.
- Loại request-header access khỏi Application context.
- Chuyển `SystemClock` ra Infrastructure và bắt buộc inject clock.
- Loại Infrastructure options khỏi API middleware.

### Slice 3: Exam Session và Exam Record

- Chuyển toàn bộ close/reopen/confirm/cancel/webhook/reconciliation transitions vào Domain.
- Đóng state setter và xóa transition/precondition duplication khỏi handler.
- Giữ nguyên audit, outbox, transaction và timestamp behavior.

### Slice 4: Import

- Chuyển lifecycle và invariant của `ImportBatch` vào Domain.
- Giữ nguyên claim, resume, savepoint, sequence, unique constraint và rollback semantics trong Infrastructure.

### Slice 5: Paraclinical, webhook và outbox

- Gom item/order, inbox và outbox state machine vào Domain.
- Application chỉ điều phối processing, audit và persistence.
- Infrastructure tiếp tục sở hữu locking, dequeue, polling và retry scheduling.

### Slice 6: HIS, FormServer và RIS

- Thay generic HTTP-shaped port bằng operation/use-case ports chuyên biệt.
- Di chuyển route, method, header, raw JSON và vendor mapping hoàn toàn vào Infrastructure.
- Giữ nguyên cache, timeout, authentication và observable failure behavior.

### Slice 7: Cleanup và full verification

- Xóa interface, adapter và mapping cũ không còn được sử dụng.
- Chạy build và đủ bốn nhóm test.
- PostgreSQL suite và API contract suite là mandatory completion gates.

## 12. Quy trình cho mỗi slice

1. Viết hoặc điều chỉnh test khóa behavior hiện tại và boundary đích.
2. Chạy test để xác nhận test mới phát hiện implementation chưa đạt.
3. Thực hiện thay đổi nhỏ nhất để đạt boundary.
4. Chạy test của slice và các contract test liên quan.
5. Xóa implementation/interface cũ của slice.
6. Build solution và kiểm tra dependency rule trước khi chuyển slice tiếp theo.

## 13. Tiêu chí hoàn thành

- Không còn handler interface một-implementation; một use case vẫn có đúng một concrete handler.
- Domain không chứa numeric API error code, HTTP hoặc vendor-specific error contract.
- State quan trọng không có public setter và chỉ thay đổi qua domain behavior.
- Application không gán trực tiếp protected state hoặc chứa transition table thuộc Domain.
- Application không chứa HTTP route/method/header, raw vendor JSON hoặc request-header accessor.
- `SystemClock` nằm ngoài Application và mọi business time trong handler dùng injected `IClock`.
- API ngoài `Program.cs` không tham chiếu Infrastructure.
- Repository và integration interfaces vẫn chuyên biệt; không có `IQueryable` hoặc generic repository.
- API contract, database schema, migration history và observable behavior không đổi.
- Domain, Application, PostgreSQL Infrastructure và API contract tests đều pass.

## 14. Rủi ro và kiểm soát

- **Đổi error flow làm lệch API contract:** khóa numeric error/status/payload bằng API contract tests trước khi di chuyển exception.
- **Đóng setter làm EF không materialize được:** điều chỉnh Fluent Configuration và chạy PostgreSQL materialization tests cho từng aggregate.
- **Chuyển transition làm lệch idempotency:** viết transition matrix tests trước khi thay handler logic.
- **Tách vendor JSON làm lệch mapping:** dùng characterization tests tại Infrastructure seam và contract tests ở API.
- **Chuyển concrete handler làm hỏng DI:** thêm composition-root tests xác nhận toàn bộ handler resolve được.
- **Refactor quá rộng:** thực hiện theo slice, không đổi schema/contract và không trộn feature mới vào remediation.
