# Tài Khoản & Phân Quyền (Account)

- Xác thực: Hệ thống sử dụng JSON Web Token (JWT) để xác thực các yêu cầu API an toàn.
- Các vai trò người dùng (Roles):
  1. User (Khách hàng thông thường): Đăng bài, like, comment, follow, nhắn tin, tìm kiếm sản phẩm, quản lý giỏ hàng, đặt hàng và theo dõi đơn hàng của mình.
  2. ShopOwner (Chủ cửa hàng): Toàn bộ quyền của User, cộng thêm quyền tạo và quản lý cửa hàng, đăng sản phẩm, cập nhật tồn kho, xử lý đơn hàng và theo dõi doanh thu cửa hàng.
  3. Shipper (Người giao hàng): Toàn bộ quyền của User, cộng thêm quyền cập nhật hồ sơ giao hàng, nhận đơn hàng chờ giao, thực hiện lấy hàng, giao hàng và theo dõi thu nhập giao hàng.
  4. Admin (Quản trị viên hệ thống): Quyền hạn cao nhất, quản lý người dùng, bài viết, cửa hàng, xem toàn bộ đơn hàng và thống kê doanh thu toàn sàn.
