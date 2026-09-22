  
PROJ2363:  
**Flow:**

Bước 1: Chọn 1 hồ sơ có sẵn hoặc tạo 1 hồ sơ NB để có hồ sơ có sẵn

Bước 2: Tìm kiếm và chọn lại thông tin của NB đó

Bước 2: Chọn NB đó trên Danh sách tìm kiếm thông tin NB

**Expect:**

- Có kế thừa dữ liệu Phường + Tỉnh của thông tin NB

**Actual:**

- Không kế thừa dữ liệu Phường + Tỉnh của thông tin NB &gt;&gt; Để trống dữ liệu
- Timeline clip lỗi:
  - Tạo hồ sơ mới: 0:00 - 0:19s
  - Tìm kiếm + Thêm hồ sơ KSK mới với thông tin NB cũ: 0:55s - 1:20s 

  
  
PROJ2364:  
**Flow:**

Bước 1: Chọn 1 hồ sơ có sẵn hoặc tạo 1 hồ sơ NB để có hồ sơ có sẵn

Bước 2: Xem lại nội dung "Dân tộc + Nơi cấp CCCD" (nhớ dữ liệu cũ)

Bước 3: Tìm kiếm và chọn lại thông tin của NB đó

Bước 4: Chọn NB đó trên Danh sách tìm kiếm thông tin NB &gt;&gt; hệ thống hiển thị thông tin NB có đủ "Dân tộc + Nơi cấp CCCD"

Bước 5: Nhập nội dung KSK

- Không thay đổi Dân tộc + Nơi cấp CCCD
- Thay đổi Phường + Tỉnh (dữ liệu mất ở BUG: [PROJ-2363](https://jira.mdsco.vn/browse/PROJ-2363 "[Đăng ký Hồ sơ KSK] Lỗi không kế thừa dữ liệu "Tỉnh + Phường" khi chọn lại Hồ sơ NB có sẵn"))

Bước 6: Chọn "Lưu hồ sơ"

**Expect:**

- Hệ thống kiểm tra thì thấy các dữ liệu:
  - Thay đổi: Phường + Tỉnh
  - Không thay đổi: Dân tộc + Nơi cấp CCCD

**Actual:**

- Hệ thống kiểm tra thì thấy các dữ liệu:
  - Thay đổi: Dân tộc + Nơi cấp CCCD
  - Không thay đổi: Phường + Tỉnh
- Timeline clip lỗi:
  - Tạo hồ sơ mới: 0:00 - 0:19s
  - Tìm kiếm + Thêm hồ sơ KSK mới với thông tin NB cũ: 0:55s - 1:20s

  
PROJ2365:  
**Flow:**

Bước 1: Chọn 1 NB có sẵn hoặc tạo 1 hồ sơ KSK mới

Bước 2: Nhập nội dung KSK

Bước 3: Chọn "Lưu hồ sơ" &gt;&gt; Vào 1:00 - 1:50 ngày 19/09

**Expect:**

- Hệ thống hiển thị hồ sơ KSK như sau:
  - Cột ngày KSK: 19/09/2026
  - Tự động hiển thị hồ sơ KSK khi tìm kiếm có khoảng thời gian ngày 19/09/2026

**Actual:**

- Hệ thống hiển thị hồ sơ KSK như sau:
  - Cột ngày KSK: 19/09/2026
  - Không tự động hiển thị hồ sơ KSK khi tìm kiếm theo ngày 19/09/2026
  - Chỉ có khi tìm kiếm theo ngày 18/09/2026
- Timeline clip lỗi:
  - Tạo hồ sơ mới: 0:00 - 0:19s
  - Tìm kiếm hồ sơ mới đăng ký theo ngày đăng ký hôm nay: 0:20s - 0:40s  
    
  

PROJ2366:  
Flow:

Bước 1: Có sẵn hồ sơ NB với các thông tin như sau:

- Họ và tên: Đỗ Đông Quân
- SĐT: 0985230227
- Mã NB: HEX-PHASE1-DEFAULT-0042
- CCCD: 079201006605

Bước 2: Thực hiện tìm kiếm theo các loại như:

- Tự nhận diện
- SĐT
- CCCD
- Họ và tên
- Mã NB

Expect:

- Hệ thống tiến hành dùng giá trị được nhập để tìm kiếm như sau:
  - Tự nhận diện: Hiển thị tất cả các thông tin NB có chứa giá trị được nhập ở cả 4 dữ liệu: Họ tên, SĐT, CCCD, Mã NB
  - Mã NB/CCCD/SĐT: Hiển thị tất cả các thông tin NB có chứa giá trị được nhập

Actual:

- Hệ thống tiến hành kiểm tra và hiển thị như sau:
  - Tự nhận diện:
    - Chỉ nhận diện được theo Họ và tên
    - Đối với Mã NB/CCCD/SĐT: phải nhập theo từng giá trị đầu tiên
  - Mã NB/CCCD/SĐT: phải nhập theo từng giá trị đầu tiên
  - Ví dụ:
    - Mã NB:
      - Đúng: Chỉ cần cần nhập "0042" hoặc "PHASE1" &gt;&gt; Có hiển thị
      - Sai: Bắt buộc phải nhập "HEX" mới hiển thị
    - SĐT:
      - Đúng: Chỉ cần nhập "985" &gt;&gt; Có hiển thị
      - Sai: Bắt buộc phải nhập "098__" thì mới hiển thị
    - CCCD:
      - Đúng: Chỉ cần nhập "227" &gt;&gt; Có hiển thị
      - Sai: Bắt buojc phải nhập "07920__" thì mới hiển thị

   
PROJ2367:  
**Flow:**

Bước 1: Đã đăng ký 1 hồ sơ KSK vào 1:00 - 1:50 19/09/2026

Bước 2: Tìm kiếm các hồ sơ ngày 19/09/2026 - Tất cả các trạng thái

**Expect:**

- Hệ thống hiển thị hồ sơ KSK như sau:
  - Cột ngày KSK: 19/09/2026
  - Tự động hiển thị hồ sơ KSK khi tìm kiếm có khoảng thời gian ngày 19/09/2026

**Actual:**

- Hệ thống hiển thị hồ sơ KSK như sau:
  - Cột ngày KSK: 19/09/2026
  - Không tự động hiển thị hồ sơ KSK khi tìm kiếm theo ngày 19/09/2026
  - Chỉ có khi tìm kiếm theo ngày 18/09/2026
- Timeline clip lỗi:
  - Tạo hồ sơ mới: 0:00 - 0:19s
  - Tìm kiếm hồ sơ mới đăng ký theo ngày đăng ký hôm nay: 0:20s - 0:40s

  
PROJ2368  
**Flow:**

Bước 1: Đã đăng ký 1 hồ sơ KSK

Bước 2: Chọn hồ sơ KSK để trả KQ

Bước 3: Chọn 1 danh mục chưa khám

Bước 4: Chọn [Thêm] &gt;&gt; Hệ thống enable khu vực nhập liệu của Danh mục Khám lâm sàng

Bước 5: Chọn [Lưu]

Bước 6: Chọn [Ký số] &gt;&gt; Hệ thống hiển thị modal xác nhận

Bước 6: Chọn [Xác nhận] &gt;&gt; Hệ thống hoàn thành ký xác nhận thông tin của Danh mục Khám lâm sàng

**Expect:**

- Sau khi [Lưu]&gt;&gt;Hệ thống hiển thị thông tin thực hiện của danh mục khám (dưới tiêu đề danh mục khám gồm):
  - Người thực hiện: Tài khoản vừa thao tác lưu
  - Người xác nhận: Tài khoản vừa thao tác lưu
  - Thời gian: Thời gian thực tế khi thao tác lưu
  - Ký số: Hiển thị "Chưa ký"
- Sau khi [Ký số] thành công &gt;&gt; Hệ thống hiển thị thông tin thực hiện của danh mục khám (dưới tiêu đề danh mục khám gồm):
  - Người thực hiện: Tài khoản vừa thao tác ký số
  - Người xác nhận: Tài khoản vừa thao tác ký số
  - Thời gian: Thời gian thực tế khi thao tác xác nhận ký số
  - Ký số: Hiển thị "Đã ký"

**Actual:**

- Sau khi [Lưu]&gt;&gt;Hệ thống hiển thị thông tin thực hiện của danh mục khám (dưới tiêu đề danh mục khám gồm):
  - Người thực hiện: Không có
  - Người xác nhận: Không có
  - Thời gian: Không có
  - Ký số: Không có
- Sau khi [Ký số] thành công &gt;&gt; Hệ thống hiển thị thông tin thực hiện của danh mục khám (dưới tiêu đề danh mục khám gồm):
  - Người thực hiện: Không có
  - Người xác nhận: Không có
  - Thời gian: Không có
  - Ký số: Không có

PROJ2369:  
**Flow:**

Bước 1: Chọn 1 NB có hồ sơ KSK đang khám

Bước 2: BS A chọn 1 danh mục "Khám thể lực" chưa khám

Bước 3: BS A chọn [Thêm] &gt;&gt; Nhập nội dung &gt;&gt; Chọn [Lưu] &gt;&gt; Hệ thống lưu thông tin Danh mục "Khám thể lực":

- Người thực hiện: BS A
- Người xác nhận: BS A
- Thời gian: Thời gian thực tế khi thao tác lưu
- Ký số: Hiển thị "Chưa ký"

Bước 4.1: BS A chọn [Xóa] danh mục "Khám thể lực" 

Bước 4.2: BS B chọn [Xóa] danh mục "Khám thể lực"

**Expect:**

Quy tắc [Xóa] dữ liệu Danh mục khám:

- User thao tác Xóa = User thao tác Tạo/Người thực hiện:
  - Xóa thông tin thực hiện của danh mục khám tương ứng
  - Trả tất cả chỉ tiêu của danh mục khám tương ứng về dạng mặc định (giữ nguyên các chỉ tiêu nếu có giá trị mặc định/mặc định chọn)
  - Chuyển dữ liệu của danh mục khám tương ứng về "Chưa khám"
- User thao tác Xóa &gt;&lt; User thao tác Tạo/Người thực hiện:
  - Hệ thống cảnh báo "Không thể xóa dữ liệu của người khác"
  - Hệ thống không thực hiện xóa dữ liệu nào cả

Actual:

Hệ thống mặc định thực hiện xóa không quan tâm User thực hiện:

- Xóa trắng dữ liệu chỉ tiêu của Danh mục khám

 

 PROJ2370  
**Flow:**

Bước 1: Chọn 1 NB có hồ sơ KSK đang khám

Bước 2: BS A chọn 1 danh mục "Khám thể lực" chưa khám

Bước 3: BS A chọn [Thêm] &gt;&gt; Nhập nội dung &gt;&gt; Chọn [Lưu] &gt;&gt; Hệ thống lưu thông tin Danh mục "Khám thể lực":

- Người thực hiện: BS A
- Người xác nhận: BS A
- Thời gian: Thời gian thực tế khi thao tác lưu
- Ký số: Hiển thị "Chưa ký"

Bước 4.1: BS A chọn [Sửa] danh mục "Khám thể lực" 

Bước 4.2: BS B chọn [Sửa] danh mục "Khám thể lực"

**Expect:**

Quy tắc [Sửa] dữ liệu Danh mục khám:

- User thao tác Sửa = User thao tác Tạo/Người thực hiện:
  - Enable khu vực nhập liệu chỉ tiêu của Danh mục "Khám thể lực"
  - Giữ nguyên tất cả các giá trị của chỉ tiêu tại Danh mục "Khám thể lực" đã lưu trước khi sửa
- User thao tác Sửa &gt;&lt; User thao tác Tạo/Người thực hiện:
  - Hệ thống cảnh báo "Không thể sửa dữ liệu của người khác"
  - Hệ thống không enable khu vực nhập liệu chỉ tiêu của Danh mục "Khám thể lực"

**Actual:**

Hệ thống mặc định cho phép sửa mà không quan tâm User thực hiện:

- Enable khu vực nhập liệu chỉ tiêu của Danh mục "Khám thể lực"

 

 PROJ2371:  
**Flow:**

Bước 1: Chọn 1 NB có hồ sơ KSK đang khám

Bước 2: BS A chọn 1 danh mục "Khám thể lực" chưa khám

Bước 3: BS A chọn [Thêm] &gt;&gt; Nhập nội dung &gt;&gt; Chọn [Lưu] &gt;&gt; Hệ thống lưu thông tin Danh mục "Khám thể lực":

- Người thực hiện: BS A
- Người xác nhận: BS A
- Thời gian: Thời gian thực tế khi thao tác lưu
- Ký số: Hiển thị "Chưa ký"

Bước 4.1: BS A chọn [Ký số] danh mục "Khám thể lực" 

Bước 4.2: BS B chọn [Ký số] danh mục "Khám thể lực"

**Expect:**

Quy tắc [Ký số] dữ liệu Danh mục khám:

- User thao tác Ký số = Người xác nhận:
  - Hệ thống hiển thị modal xác nhận Ký số
- User thao tác Ký số  &gt;&lt; Người xác nhận:
  - Hệ thống cảnh báo "Bạn không phải là người xác nhận danh mục khám!"

**Actual:**

Hệ thống mặc định cho phép ký số mà không quan tâm User thực hiện:

- Hệ thống hiển thị modal xác nhận Ký số

 

 PROJ2372:  
**Flow:**

Bước 1: Chọn 1 NB có hồ sơ KSK đang khám

Bước 2: BS A chọn 1 danh mục "Khám thể lực" chưa khám

Bước 3: BS A chọn [Thêm] &gt;&gt; Nhập nội dung &gt;&gt; Chọn [Lưu] &gt;&gt; Hệ thống lưu thông tin Danh mục "Khám thể lực":

- Người thực hiện: BS A
- Người xác nhận: BS A
- Thời gian: Thời gian thực tế khi thao tác lưu
- Ký số: Hiển thị "Chưa ký"

Bước 4: BS A ký số danh mục "Khám thể lực" &gt;&gt; Hệ thống hiển thị thông tin thực hiện của danh mục khám (dưới tiêu đề danh mục khám gồm):

- Người thực hiện: BS A
- Người xác nhận: BS A
- Thời gian: Thời gian thực tế khi thao tác xác nhận ký số
- Ký số: Hiển thị "Đã ký"

Bước 5.1: BS A chọn [Ký số hủy] danh mục "Khám thể lực" 

Bước 5.2: BS B chọn [Ký số hủy] danh mục "Khám thể lực"

**Expect:**

Quy tắc [Ký số hủy] dữ liệu Danh mục khám:

- User thao tác Ký số hủy = Người xác nhận:
  - Hệ thống hiển thị modal xác nhận Ký số hủy
- User thao tác Ký số hủy  &gt;&lt; Người xác nhận:
  - Hệ thống cảnh báo "Không thể hủy dữ liệu đã ký của người khác!"

**Actual:**

Hệ thống mặc định cho phép ký số hủy mà không quan tâm User thực hiện:

- Hệ thống hiển thị modal xác nhận Ký số hủy

 

 PROJ2373:  
**Flow:**

Bước 1: Chọn 1 NB có hồ sơ KSK đang khám

Bước 2: BS A chọn 1 danh mục "Khám thể lực" chưa khám

Bước 3: BS A chọn [Thêm] &gt;&gt; Nhập nội dung &gt;&gt; Chọn [Lưu] &gt;&gt; Hệ thống lưu thông tin Danh mục "Khám thể lực":

- Người thực hiện: BS A
- Người xác nhận: BS A
- Thời gian: Thời gian thực tế khi thao tác lưu
- Ký số: Hiển thị "Chưa ký"

Bước 4: BS A ký số danh mục "Khám thể lực" &gt;&gt; Hệ thống hiển thị thông tin thực hiện của danh mục khám (dưới tiêu đề danh mục khám gồm):

- Người thực hiện: BS A
- Người xác nhận: BS A
- Thời gian: Thời gian thực tế khi thao tác xác nhận ký số
- Ký số: Hiển thị "Đã ký"

Bước 5: Làm lần lượt cho tất cả các Danh mục Khám lâm sàng

Bước 6: Thao tác phần Kết luận

Bước 7: Chọn [Ký số kết luận] + xác nhận

**Expect:**

Hệ thống chuyển hồ sơ bao gồm:

- Trạng thái hồ sơ KSK: Từ "Đang khám" &gt;&gt; "Đã khám"
- Khu vực thao tác Kết luận:
  - Hiển thị nút "Ký số hủy"
  - Disable nút "Chỉnh sửa" + "Ký số"

**Actual:**

Hệ thống chuyển hồ sơ bao gồm:

- Trạng thái hồ sơ KSK: Giữ nguyên trạng thái "Đang khám"
- Khu vực thao tác Kết luận:
  - Không hiển thị nút "Ký số hủy"
  - Hiển thị nút "Chỉnh sửa"
  - Disable nút "Ký số"
  - Trạng thái khu vực kết luận: Không đủ điều kiện ký số

 

 PROJ2374:  
Flow: 

Bước 1: Chọn 1 NB có hồ sơ KSK đang khám

Bước 2: BS A chọn 1 danh mục "Khám thể lực" chưa khám

Bước 3: BS A chọn [Thêm]

Bước 4: BS A có thể nhập 2 trường mặc định:

- Người xác nhận: Danh sách BS có vai trò "Bác sĩ Khám sức khỏe" &gt;&gt; Chọn BS B
- Thời gian thực hiện: định dạng hh:MM dd/mm/yyyy (mặc định hiển thị thời gian hiện tại khi thêm mới)

Bước 5: Nhập nội dung &gt;&gt; Chọn [Lưu] &gt;&gt; Hệ thống lưu thông tin Danh mục "Khám thể lực":

- Người thực hiện: BS A
- Người xác nhận: BS B
- Thời gian:
  - Thời gian mặc định khi thêm mới: Nếu không chỉnh
  - Thời gian đã chỉnh (nếu có)
- Ký số: Hiển thị "Chưa ký"

Bước 6: BS A thao tác Ký số:

- Hệ thống sẽ lưu 2 thông tin vào dữ liệu Ký số của Khu vực Khám lâm sàng:
  - Người ký = Người xác nhận
  - Thời gian ký = Thời gian thực hiện/Thời gian  
    
    
    
  Flow state machine của khám sức khỏe nằm tại: Users/nguyenhoanghai1502/Downloads/mh5-flow.html

