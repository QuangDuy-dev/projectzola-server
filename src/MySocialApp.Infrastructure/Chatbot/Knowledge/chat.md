# Tính Năng Trò Chuyện & Nhắn Tin (Chat)

- Cuộc trò chuyện (Conversations): Hệ thống hỗ trợ tạo cuộc trò chuyện trực tiếp giữa hai người dùng (Direct 1-on-1) hoặc nhóm chat nhiều thành viên (Group Conversation).
- Nhắn tin thời gian thực:
  - Tin nhắn văn bản được lưu trữ bền vững trong cơ sở dữ liệu và được phát sóng ngay tức thì đến các thiết bị của người nhận qua ASP.NET Core SignalR (ChatHub).
  - Hỗ trợ gửi tin nhắn với Idempotency Key để tránh gửi trùng lặp tin nhắn khi mạng chập chờn.
- Trạng thái tin nhắn:
  - Hỗ trợ đánh dấu tin nhắn đã đọc (Read receipts) và hiển thị số lượng tin nhắn chưa đọc trong từng cuộc trò chuyện.
- Đa thiết bị & Trạng thái hiện diện:
  - Người dùng có thể đăng nhập trên nhiều thiết bị cùng lúc; SignalR định tuyến tin nhắn đến đúng nhóm người dùng `user:{userId}` và nhóm hội thoại `conversation:{conversationId}`.
