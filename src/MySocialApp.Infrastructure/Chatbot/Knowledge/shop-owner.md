# Dành Cho Chủ Cửa Hàng (Shop Owner)

- Quản lý cửa hàng & sản phẩm:
  - Người dùng có vai trò ShopOwner có thể tạo và chỉnh sửa thông tin cửa hàng.
  - Tạo mới, cập nhật sản phẩm kèm giá, mô tả, danh mục và số lượng tồn kho (Stock). Tồn kho được kiểm soát chặt chẽ bằng ràng buộc không âm và Concurrency Token.
  - Đăng tải hình ảnh sản phẩm (tự động tối ưu sang WebP).
- Quy trình xử lý đơn hàng của Shop:
  1. Xác nhận đơn (Confirm): Chuyển đơn từ Chờ xử lý (Pending) sang Đã xác nhận (Confirmed).
  2. Chuẩn bị hàng (Prepare): Chuyển đơn sang Đang chuẩn bị (Preparing).
  3. Sẵn sàng giao (Ready for pickup): Chuyển đơn sang Chờ lấy hàng (WaitingForPickup) để shipper có thể nhìn thấy và nhận giao.
- Hủy đơn hàng: Chủ shop có thể hủy đơn trước khi đơn hàng chuyển sang trạng thái Đang giao (Shipping). Hủy đơn sẽ hoàn trả tồn kho tự động.
- Doanh thu của Shop (Revenue):
  - Doanh thu của cửa hàng chỉ được tính dựa trên các đơn hàng ĐÃ GIAO THÀNH CÔNG (Delivered). Các đơn đang xử lý hoặc bị hủy không được tính.
  - Khoảng thời gian tính doanh thu dựa trên thời điểm giao hàng thực tế (DeliveredAt).
  - Thống kê doanh thu bao gồm: Tổng doanh thu (Gross Merchandise Value), số đơn đã giao thành công và tổng số lượng sản phẩm đã bán.
