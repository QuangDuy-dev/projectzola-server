# Tính Năng Mua Sắm & Đơn Hàng (Shopping)

- Tìm kiếm sản phẩm: Người dùng có thể tìm kiếm sản phẩm theo từ khóa (tên, mô tả), lọc theo danh mục (Category), lọc theo cửa hàng (Shop), lọc theo khoảng giá (minPrice, maxPrice), và sắp xếp theo giá tăng/giảm, tên, hoặc mới nhất. Tối đa 50 kết quả mỗi trang.
- Tìm kiếm cửa hàng: Người dùng có thể tìm kiếm các cửa hàng đang hoạt động theo tên hoặc mô tả, kèm theo tổng số lượng sản phẩm đang bán của cửa hàng.
- Giỏ hàng (Cart):
  - Khách hàng có thể thêm sản phẩm vào giỏ hàng, cập nhật số lượng, xóa từng sản phẩm hoặc dọn sạch giỏ hàng.
  - Thao tác thêm vào giỏ hàng KHÔNG giữ chỗ hay trừ tồn kho trước.
  - Khi thêm sản phẩm đã có, hệ thống tự động cộng dồn số lượng nhưng không vượt quá tồn kho hiện có của sản phẩm.
  - Giỏ hàng gom nhóm các sản phẩm theo từng Shop và tính tổng tiền giỏ hàng.
- Đặt hàng & Thanh toán (Checkout):
  - Khi thực hiện thanh toán (Checkout), hệ thống tự động tách giỏ hàng thành các Đơn hàng (Order) riêng biệt cho từng Shop (Multi-shop checkout).
  - Giá sản phẩm được tính theo giá thực tế có thẩm quyền lưu trong cơ sở dữ liệu (Authoritative DB Price), không phụ thuộc vào giá từ client.
  - Tồn kho của từng sản phẩm được trừ trực tiếp và nguyên tử khi đặt hàng thành công.
  - Giỏ hàng được tự động dọn sạch sau khi đơn hàng được tạo.
- Quy tắc hủy đơn hàng:
  - Khách hàng chỉ có thể hủy đơn hàng khi đơn còn ở trạng thái Chờ xử lý (Pending).
  - Khi đơn hàng bị hủy thành công, số lượng tồn kho của các sản phẩm sẽ được tự động hoàn trả lại kho đúng 1 lần.
  - Đơn hàng đã ở trạng thái Đang giao (Shipping) hoặc Đã giao thành công (Delivered) TUYỆT ĐỐI không thể hủy.
