# Dành Cho Người Giao Hàng (Shipper)

- Hồ sơ Shipper: Người dùng có vai trò Shipper có thể cập nhật thông tin hồ sơ vận chuyển (phương tiện, biển số, giấy phép lái xe).
- Tìm & Nhận đơn giao (Claim Order):
  - Shipper có thể xem danh sách các đơn hàng đang chờ lấy hàng (WaitingForPickup) chưa có người nhận.
  - Nhận đơn (Claim): Khi shipper nhận đơn thành công, hệ thống gán ShipperId vào đơn hàng và phát thông báo tức thời đến người mua và chủ shop. Nếu hai shipper cùng nhận một đơn, cơ chế kiểm soát đồng thời (Concurrency Token) đảm bảo chỉ người đầu tiên nhận được đơn, người thứ hai sẽ nhận thông báo đơn đã có người nhận.
- Quy trình giao hàng:
  1. Lấy hàng (Pickup): Shipper nhận hàng từ Shop và chuyển trạng thái đơn hàng từ Chờ lấy hàng (WaitingForPickup) sang Đang giao (Shipping).
  2. Giao hàng thành công (Deliver): Shipper giao đơn đến tay người mua và chuyển đơn sang Đã giao (Delivered). Hệ thống tự động ghi nhận thời điểm giao hàng (DeliveredAt).
- Thu nhập của Shipper (Shipper Revenue):
  - Thu nhập của Shipper được tính bằng tổng phí giao hàng (ShippingFee) của các đơn hàng do shipper đó giao thành công (Delivered) trong khoảng thời gian đã chọn.
