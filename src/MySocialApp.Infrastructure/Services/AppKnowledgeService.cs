using System.Collections.Concurrent;
using MySocialApp.Application.Interfaces;

namespace MySocialApp.Infrastructure.Services;

public class AppKnowledgeService : IAppKnowledgeService
{
    private static readonly ConcurrentDictionary<string, string> KnowledgeCache = new();
    private static bool _initialized = false;
    private static readonly object InitLock = new();

    public AppKnowledgeService()
    {
        EnsureInitialized();
    }

    public string RouteCategory(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return "General";

        var normalized = userMessage.ToLowerInvariant();

        // 1. Shipper keywords
        if (normalized.Contains("shipper") || normalized.Contains("giao hàng") ||
            normalized.Contains("nhận đơn") || normalized.Contains("đơn giao") ||
            normalized.Contains("lấy hàng") || normalized.Contains("vận chuyển") ||
            normalized.Contains("thu nhập giao"))
        {
            return "Shipper";
        }

        // 2. ShopOwner keywords
        if (normalized.Contains("chủ shop") || normalized.Contains("chủ cửa hàng") ||
            normalized.Contains("shop owner") || normalized.Contains("đơn của shop") ||
            normalized.Contains("xác nhận đơn") || normalized.Contains("chuẩn bị hàng") ||
            normalized.Contains("sẵn sàng giao") || normalized.Contains("doanh thu shop") ||
            normalized.Contains("quản lý sản phẩm") || normalized.Contains("tồn kho"))
        {
            return "ShopOwner";
        }

        // 3. Shopping keywords
        if (normalized.Contains("giỏ hàng") || normalized.Contains("mua hàng") ||
            normalized.Contains("checkout") || normalized.Contains("thanh toán") ||
            normalized.Contains("đặt hàng") || normalized.Contains("sản phẩm") ||
            normalized.Contains("tìm sản phẩm") || normalized.Contains("tìm shop") ||
            normalized.Contains("hủy đơn") || normalized.Contains("huỷ đơn") ||
            normalized.Contains("đơn hàng") || normalized.Contains("danh mục"))
        {
            return "Shopping";
        }

        // 4. Social keywords
        if (normalized.Contains("bài viết") || normalized.Contains("đăng bài") ||
            normalized.Contains("post") || normalized.Contains("video") ||
            normalized.Contains("short video") || normalized.Contains("follow") ||
            normalized.Contains("theo dõi") || normalized.Contains("like") ||
            normalized.Contains("thích") || normalized.Contains("dislike") ||
            normalized.Contains("bình luận") || normalized.Contains("comment") ||
            normalized.Contains("bảng tin") || normalized.Contains("feed"))
        {
            return "Social";
        }

        // 5. Chat keywords
        if (normalized.Contains("tin nhắn") || normalized.Contains("nhắn tin") ||
            normalized.Contains("trò chuyện") || normalized.Contains("chat") ||
            normalized.Contains("cuộc trò chuyện") || normalized.Contains("conversation") ||
            normalized.Contains("đã đọc") || normalized.Contains("chưa đọc"))
        {
            return "Chat";
        }

        // 6. Account keywords
        if (normalized.Contains("tài khoản") || normalized.Contains("đăng nhập") ||
            normalized.Contains("đăng ký") || normalized.Contains("vai trò") ||
            normalized.Contains("role") || normalized.Contains("mật khẩu") ||
            normalized.Contains("admin") || normalized.Contains("jwt"))
        {
            return "Account";
        }

        return "General";
    }

    public Task<string> GetRelevantKnowledgeAsync(string userMessage)
    {
        var category = RouteCategory(userMessage);

        var generalText = KnowledgeCache.TryGetValue("general", out var gen) ? gen : string.Empty;
        var categoryKey = category.ToLowerInvariant();

        if (categoryKey != "general" && KnowledgeCache.TryGetValue(categoryKey, out var catText))
        {
            return Task.FromResult($"{catText}\n\n{generalText}".Trim());
        }

        return Task.FromResult(generalText);
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (InitLock)
        {
            if (_initialized) return;

            // Attempt to load from candidate directories
            var candidatePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Chatbot", "Knowledge"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "MySocialApp.Infrastructure", "Chatbot", "Knowledge")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "MySocialApp.Infrastructure", "Chatbot", "Knowledge")),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "MySocialApp.Infrastructure", "Chatbot", "Knowledge"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "src", "MySocialApp.Infrastructure", "Chatbot", "Knowledge"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "src", "MySocialApp.Infrastructure", "Chatbot", "Knowledge"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "src", "MySocialApp.Infrastructure", "Chatbot", "Knowledge"),
                Path.Combine(Directory.GetCurrentDirectory(), "Chatbot", "Knowledge")
            };

            string? foundDir = null;
            foreach (var path in candidatePaths)
            {
                if (Directory.Exists(path))
                {
                    foundDir = path;
                    break;
                }
            }

            if (foundDir != null)
            {
                foreach (var file in Directory.GetFiles(foundDir, "*.md"))
                {
                    var rawName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    var content = File.ReadAllText(file);
                    KnowledgeCache[rawName] = content;
                    var stripped = rawName.Replace("-", "").Replace("_", "");
                    KnowledgeCache[stripped] = content;
                }
            }

            // Fallback default knowledge if files are missing or incomplete
            EnsureFallback("general", "# Giới Thiệu Chung Về MySocialApp\n- Nền tảng mạng xã hội, nhắn tin và thương mại điện tử.\n- Trợ lý AI chỉ tư vấn, không tự ý thực hiện hành động trên tài khoản.");
            EnsureFallback("social", "# Mạng Xã Hội (Social)\n- Đăng bài viết, ảnh (tối đa 10), video ngắn (<= 60s).\n- Tương tác: Like, Dislike, Comment, Follow/Unfollow.\n- Feed: Social Home Feed và Short Video Feed.");
            EnsureFallback("shopping", "# Mua Sắm (Shopping)\n- Tìm kiếm sản phẩm, lọc danh mục, giá, shop.\n- Giỏ hàng không giữ tồn kho; checkout phân tách đơn riêng từng shop.\n- Hủy đơn: chỉ hủy được khi đơn ở trạng thái Pending, hoàn trả tồn kho đúng 1 lần.");
            EnsureFallback("chat", "# Trò Chuyện (Chat)\n- Chat 1-1 và chat nhóm thời gian thực qua SignalR.\n- Đánh dấu đã đọc, thông báo tin nhắn mới tức thì.");
            EnsureFallback("shopowner", "# Chủ Cửa Hàng (ShopOwner)\n- Quản lý sản phẩm và tồn kho.\n- Xử lý đơn: Confirm -> Prepare -> ReadyForPickup.\n- Doanh thu chỉ tính trên các đơn đã giao thành công (Delivered).");
            EnsureFallback("shop-owner", KnowledgeCache.TryGetValue("shopowner", out var so) ? so : "# Chủ Cửa Hàng (ShopOwner)");
            EnsureFallback("shipper", "# Người Giao Hàng (Shipper)\n- Nhận đơn chờ lấy (WaitingForPickup).\n- Giao hàng: Pickup -> Shipping -> Delivered.\n- Thu nhập Shipper tính trên phí vận chuyển (ShippingFee) của các đơn đã giao thành công.");
            EnsureFallback("account", "# Tài Khoản (Account)\n- Xác thực bằng JWT token.\n- Các vai trò: User, ShopOwner, Shipper, Admin.");

            _initialized = true;
        }
    }

    private static void EnsureFallback(string key, string fallbackContent)
    {
        if (!KnowledgeCache.ContainsKey(key))
        {
            KnowledgeCache[key] = fallbackContent;
        }
    }
}
