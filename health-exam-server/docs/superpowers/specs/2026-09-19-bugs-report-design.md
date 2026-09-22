# Thiết kế xử lý bugs ngày 19/09/2026

> **Cập nhật theo yêu cầu đồng bộ:** patient/search giữ nguyên; thiết kế signing dưới đây đã được thay thế bởi signing từ `origin/main` commit `443b266`. Tham chiếu `2026-09-19-section-sign-choose-signer-design.md` và API docs tương ứng. Giữ các phần signing cũ làm lịch sử, không dùng làm chỉ dẫn sửa code mới.

## Trạng thái và phạm vi

Thiết kế và bản tài liệu đã được người dùng xác nhận trong hội thoại. Kế hoạch triển khai được lưu tại `docs/superpowers/plans/2026-09-19-bugs-report.md`; việc duyệt tài liệu chưa cho phép triển khai code hoặc chạy migration lên môi trường thật.

Nguồn yêu cầu: `docs/19092026_bugs_report.md`, flow tham chiếu tại `/Users/nguyenhoanghai1502/Downloads/mh5-flow.html`, và các quyết định trong hội thoại. Các quyết định A/B dưới đây thay thế nội dung mâu thuẫn của PROJ2371, PROJ2372 và PROJ2374 trong báo cáo gốc.

Phạm vi: 12 ticket PROJ2363–PROJ2374. Bàn giao backend, migration nếu cần, kiểm thử hồi quy và hợp đồng tích hợp frontend. Đầu việc UI phải được ghi trong kế hoạch; không coi bug giao diện hoàn tất chỉ vì backend đã sửa. Không viết lại hệ thống ký, không thêm hệ thống phân công phụ tá–bác sĩ.

## 1. Quyết định nghiệp vụ đã chốt

- **A** là tài khoản đăng nhập, người tạo/sở hữu danh mục và người thao tác trên app. Danh tính A phải lấy từ ngữ cảnh xác thực, không tin giá trị client gửi.
- **B** là bác sĩ được chọn để hiển thị trên PDF. A có thể chọn bất kỳ bác sĩ hợp lệ trong danh sách có vai trò Bác sĩ Khám sức khỏe; không cần cấu hình phân công hỗ trợ A–B.
- **Điều chỉnh theo yêu cầu mới:** lưu form không bắt buộc chọn B. Khi chưa chọn, metadata bác sĩ để trống; không tự gán A làm B. Chỉ khi ký (danh mục hoặc kết luận) mới bắt buộc có B hợp lệ, có thể chọn ngay trong bước xác nhận ký.
- A được sửa, xóa, ký và hủy ký danh mục của mình nếu trạng thái cho phép. Tài khoản C không được thao tác thay A chỉ vì chọn cùng B. Việc đổi B không làm đổi chủ sở hữu A.
- Backend kiểm tra quyền trước khi thay đổi dữ liệu hoặc gọi hệ thống ngoài. Frontend chỉ phản ánh quyền đó qua nút/cảnh báo, không phải hàng rào phân quyền duy nhất.
- Lưu riêng người thao tác A, bác sĩ hiển thị B, thời gian thực hiện do người dùng nhập và thời điểm ký/hủy thực tế do hệ thống ghi. Không ghi đè audit A thành B, không dùng thời gian nhập để giả làm thời điểm ký thực tế.
- B được kiểm tra lại ở backend theo danh sách hợp lệ và phạm vi truy cập hiện hành. Không mở rộng truy cập liên cơ sở vì cụm từ “mọi bác sĩ”.
- Sau xóa/hủy ký danh mục: về Chưa khám, bỏ thông tin thực hiện đang hiển thị, khôi phục các giá trị mặc định theo định nghĩa biểu mẫu. Giữ audit lịch sử A/B và thao tác đã thực hiện.
- Nếu hồ sơ đã ký kết luận, không cho sửa/xóa/ký/hủy riêng danh mục cho đến khi kết luận được hủy hợp lệ.

### Tên PDF không phải chủ thể chứng thư

Code hiện dùng `SignedByEmployeeCode` để tra cứu chứng thư, đồng thời dùng thông tin nhân viên của snapshot khi ký PDF. Thiết kế phải tách thông tin hiển thị B khỏi tài khoản thao tác/chủ thể chứng thư.

Không tự lấy chứng thư B chỉ vì A chọn tên B. Giữ cơ chế chọn chứng thư được hệ thống cho phép và dấu vết chủ thể chứng thư thực tế; hiển thị B là thông tin nghiệp vụ riêng. Kiểm thử tích hợp phải kiểm chứng tên trên PDF và chứng thư thực tế. Nếu gateway không hỗ trợ tách hai dữ liệu này, phải báo điểm không tương thích và chốt cách tích hợp trước khi bật chức năng; không âm thầm đổi sang dùng chứng thư B hoặc bỏ kiểm tra chứng thư.

## 2. Cách triển khai được chọn

Sửa theo nhóm nghiệp vụ trên luồng hiện có. Không sửa rời từng ticket vì nhóm quyền/ký dùng chung dữ liệu và quy tắc; không tái thiết kế toàn bộ hệ thống vì vượt phạm vi.

| Thứ tự | Ticket | Mục tiêu |
| --- | --- | --- |
| 1 | 2363–2364 | Kế thừa đúng thông tin NB và nhận diện đúng trường thay đổi |
| 2 | 2365–2367 | Thống nhất ngày tìm kiếm và tìm chuỗi con |
| 3 | 2368–2372, 2374 | Metadata thực hiện, quyền A, bác sĩ B, reset danh mục |
| 4 | 2373 | Đồng bộ kết luận, trạng thái hồ sơ và khóa/mở thao tác |

Nhóm 1 và 2 độc lập với nhóm 3. Nhóm 4 tích hợp trên quy tắc trạng thái của nhóm 3. Chia thành các phần triển khai/kiểm thử riêng trong kế hoạch để tránh một đợt thay đổi quá lớn.

## 3. Nhóm thông tin người bệnh — PROJ2363–2364

Luồng: lấy profile NB → điền form đăng ký → lưu hồ sơ → đối chiếu thay đổi → lấy lại profile/hồ sơ.

- Truyền và lưu đầy đủ mã tỉnh/phường, cùng thông tin hiển thị tương ứng; dùng mã/ID chuẩn để so sánh, không so tên hiển thị với ID.
- Dân tộc và nơi cấp CCCD không đổi phải giữ nguyên, không báo thay đổi do sai mapping giữa request, profile và dữ liệu lưu.
- Thay tỉnh/phường phải được nhận diện đúng, không bị bỏ qua chỉ vì địa chỉ dạng chuỗi chưa đổi.
- Bảo toàn cơ chế phiên bản profile và snapshot hồ sơ hiện có; không sửa ngược thông tin lịch sử một cách ngầm định.

Điểm khảo sát: `PatientRegistrationWriter`, `PatientProfileComparer`, `GetPatientProfile`, `PatientModels`, `CreateExamRecord`, `UpdateExamRecord`, `Patient` và mapping persistence. Có sẵn thay đổi chưa commit cùng migration `20260919031041_AddPatientAddressMasterCodes.cs`; phải rà soát và tái sử dụng phần đúng, không ghi đè hoặc mặc định coi đã hoàn tất.

Nghiệm thu: tạo NB rồi chọn lại giữ đúng tỉnh/phường; chỉ đổi địa chỉ thì chỉ báo trường địa chỉ tương ứng; dân tộc/nơi cấp không đổi không bị báo sai; xác nhận cập nhật tạo/tái sử dụng phiên bản theo quy tắc hiện hành.

## 4. Nhóm tìm kiếm — PROJ2365–2367

### Ngày

`ExamRecordRepository` hiện lấy đầu ngày UTC để lọc `CreatedDate`, trong khi kết quả còn có `ExamDate` của đợt khám. Đây là bằng chứng cần kiểm tra, chưa phải kết luận đầy đủ về mapping frontend.

Hợp đồng đích: cột ngày KSK và bộ lọc ngày phải cùng nghĩa nghiệp vụ; ngày dạng lịch được hiểu theo giờ Việt Nam, không theo timezone máy chủ. Nếu lọc timestamp, chuyển khoảng ngày Việt Nam thành khoảng UTC nửa mở `[đầu ngày, đầu ngày kế tiếp)`. Không chuyển timezone lần hai cho một giá trị vốn đã là ngày thuần túy.

Trước khi viết thay đổi, kế hoạch phải đối chiếu request/response và binding UI để xác định cột đang dùng ngày đăng ký hay ngày đợt khám; không tự thay trường lọc khi chưa có bằng chứng. PROJ2365 và PROJ2367 dùng chung kịch bản hồi quy nhưng kiểm tra cả hai màn hình/đường gọi liên quan.

Nghiệm thu: hồ sơ tạo lúc 01:00 ngày 19/09/2026 giờ Việt Nam nằm trong ngày 19/09, không bị chuyển sang 18/09; kiểm tra sát 00:00, cuối ngày, khoảng nhiều ngày, khoảng không hợp lệ và mọi trạng thái.

### Chuỗi tìm kiếm

`SearchPatientsHandler` hiện chọn trường bằng `PatientSearchKeywordClassifier` cho chế độ auto. Hợp đồng mới: auto OR cả họ tên, SĐT, CCCD và mã NB; chế độ cụ thể chỉ tìm trường tương ứng. Mã NB/CCCD/SĐT khớp chuỗi con, không bắt buộc tiền tố. Giữ giới hạn kết quả, lọc profile active và phạm vi đơn vị hiện có.

Nghiệm thu: chuỗi ở đầu/giữa/cuối, không khớp, rỗng, khoảng trắng và ký tự đặc biệt được xử lý như dữ liệu tìm kiếm. Chỉ dùng ví dụ chuỗi thực sự nằm trong dữ liệu mẫu; không sao chép ví dụ CCCD không khớp từ báo cáo làm kỳ vọng test.

## 5. Nhóm danh mục khám — PROJ2368–2372, 2374

### Dữ liệu và luồng

Tận dụng mô hình trạng thái/snapshot hiện có sau khi xác minh khả năng biểu diễn metadata trước ký. Không tạo hệ thống workflow mới. Metadata cần có chủ sở hữu A, bác sĩ B, thời gian thực hiện và trạng thái; tên trường vật lý và migration được xác định trong kế hoạch từ schema thực tế.

Luồng: thêm → nhập thời gian và chỉ tiêu (B tùy chọn) → lưu thành công → trả metadata/trạng thái → chọn/xác nhận B khi ký → trả trạng thái và tiến độ mới → hủy ký/reset → trả trạng thái chưa khám và giá trị mặc định.

- Metadata phải xuất hiện ngay sau lưu và còn đúng sau tải lại, không chỉ có ở response ký.
- Sửa giữ nguyên giá trị đã lưu; không tự reset khi bắt đầu chỉnh sửa.
- Ký chỉ hợp lệ khi danh mục đã lưu và người thao tác đúng A. Lỗi quyền hoặc trạng thái không được tạo side effect.
- Thiếu B không chặn lưu/sửa form hoặc mở bước chọn bác sĩ khi ký. API ký phải kiểm tra B hợp lệ từ request ký hoặc lựa chọn đã lưu; thiếu/sai B thì từ chối, không đổi trạng thái hay tạo chữ ký. Nếu đã chọn B trước đó, kiểm tra lại tại thời điểm ký.
- Xóa/hủy chỉ tác động danh mục mục tiêu; không xóa nội dung danh mục khác khi lưu lại biểu mẫu HIS.
- Dữ liệu lịch sử thiếu A chỉ được khôi phục từ dấu vết đáng tin; không tự gán người đang đăng nhập. Nếu không xác định được, trả lỗi rõ ràng và không cho nhận quyền sở hữu bằng thao tác ghi.

Điểm khảo sát: `SaveHisFormSection`, `GetHisRecordSection`, `SignExamSection`, `CancelExamSectionSign`, signing models, snapshot domain/persistence và định nghĩa mặc định biểu mẫu. Hiện hủy ký chỉ bỏ snapshot; phải bổ sung reset nội dung thực tế.

### Nhất quán và lỗi

Giữ cơ chế khóa theo hồ sơ hiện có, kiểm tra quyền/trạng thái trong phạm vi chống tranh chấp trước tác động ghi. Reset nội dung HIS phải thành công trước khi công bố hủy/xóa thành công. Không coi transaction DB nội bộ bao phủ được HTTP tới HIS. Nếu HIS thành công nhưng lưu trạng thái local thất bại, trả lỗi, giữ dấu vết phục hồi và cho phép đối chiếu/thử lại an toàn; không giả báo thành công hoặc ghi đè toàn bộ dữ liệu từ bản đọc thất bại.

Nghiệm thu: A lưu/sửa không có B vẫn thành công và reload giữ B trống; ký thiếu/sai B bị từ chối không có side effect; chọn B tại bước ký rồi ký/hủy thành công; C bị chặn ở API; đổi B không đổi owner; dữ liệu mặc định khôi phục đúng; danh mục khác nguyên vẹn; hai thao tác đồng thời không vượt khóa; lỗi HIS không tạo trạng thái thành công giả.

## 6. Nhóm kết luận — PROJ2373

`SignConclusionHandler` hiện ký nối chuỗi các marker, lưu PDF rồi lưu trạng thái ký. Thiết kế giữ luồng này, đồng bộ thêm trạng thái nghiệp vụ hồ sơ và dữ liệu trả về thay vì tạo đường ký mới.

- Chỉ ký khi đủ điều kiện lâm sàng, CLS và kết luận đã lưu theo flow.
- Lưu kết luận không bắt buộc B; ký kết luận phải có B hợp lệ cho bước kết luận. Các bước lâm sàng đã ký giữ nguyên B của từng bước.
- Khi ký và lưu thành công: hồ sơ Đang khám → Đã khám, kết luận Đã ký; cho phép hủy theo quyền hiện hành, khóa sửa/ký lại và khóa ghi lâm sàng/CLS.
- Khi hủy kết luận hợp lệ: kết luận Chưa nhập, xóa nội dung kết luận theo flow, hồ sơ về Đang khám và mở lại hai tab. Không mặc định reset chữ ký/nội dung mọi danh mục lâm sàng.
- API ký, lấy chi tiết, danh sách và điều kiện ký phải phản ánh trạng thái nhất quán. Không dùng nhãn “không đủ điều kiện ký” để thay cho trạng thái “đã ký”.
- Lỗi render/ký/upload/lưu trạng thái không được khiến UI nhận thành công giả. Giữ kiểm thử thử lại và gọi lặp của luồng ký hiện có.

Nghiệm thu: trước ký, sau ký, tải lại, hủy và tải lại; gọi ghi trực tiếp vào hồ sơ đã khóa bị từ chối; lỗi từng tích hợp không chuyển hồ sơ sang Đã khám sai.

## 7. Migration, tích hợp và xác minh

- Chỉ thêm cột/mapping cần cho quy tắc đã chốt; kiểm tra migration địa chỉ hiện có và model snapshot, không thêm migration trùng.
- Không backfill giả A/B hoặc thời gian ký cho dữ liệu cũ. Kiểm thử dữ liệu cũ thiếu metadata, hồ sơ chưa ký và đã ký.
- Frontend phải dùng mã địa chỉ đúng, gửi B/thời gian riêng, hiển thị metadata và quyền từ backend, làm mới trạng thái sau thao tác và phân biệt “đã ký” với “chưa đủ điều kiện”.
- Dùng hạ tầng test hiện có: unit cho đối chiếu/quyền/trạng thái, integration cho query DB và persistence, kiểm thử tích hợp HIS/PDF và nghiệm thu UI cho các ticket tương ứng. Test phải chứng minh lỗi trước sửa khi có thể tái hiện.
- Trước sửa symbol: GitNexus impact, báo HIGH/CRITICAL; UNKNOWN cần xác minh bổ sung. Trước commit: detect_changes đầy đủ và kiểm tra diff; không coi kết quả partial/truncated là đạt.
- Không chạy migration, ghi dữ liệu thật, gọi ký thật hoặc deploy trong giai đoạn thiết kế/kế hoạch.

## 8. Điều kiện chuyển sang kế hoạch

Người dùng rà soát bản tài liệu này. Sau khi duyệt, dùng writing-plans để lập các đầu việc cụ thể theo nhóm, file/symbol, kiểm thử và điều kiện nghiệm thu. Các kiểm tra mapping ngày, khả năng tách tên PDF/chứng thư và schema metadata phải là bước xác minh có kết quả trước khi triển khai phần phụ thuộc; chúng không phải giấy phép tự đổi nghiệp vụ đã chốt.
