using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Logging;
namespace DoraFood.Controllers
{
    public class BlogController : Controller
    {
        private readonly DoraFoodContext _context;
        private readonly ILogger<BlogController> _logger;

        public BlogController(
            DoraFoodContext context,
            ILogger<BlogController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Dữ liệu sẽ truyền sang View danh sách bài viết.
        public class BlogIndexViewModel
        {
            public List<Post> Posts { get; set; } = new();

            public List<PostCategory> Categories { get; set; } = new();

            public long? SelectedCategoryId { get; set; }

            public int CurrentPage { get; set; }

            public int TotalPages { get; set; }

            public int TotalPosts { get; set; }
        }
        // Dữ liệu được phép nhận từ form.
        public class CommentInput
        {
            public long PostId { get; set; }

            [Display(Name = "Bình luận")]
            [Required(ErrorMessage = "Vui lòng nhập bình luận.")]
            [StringLength(2000,
                ErrorMessage = "Bình luận tối đa 2.000 ký tự.")]
            public string Body { get; set; } = string.Empty;
        }

        // Dữ liệu công khai để hiển thị bình luận.
        public class CommentItem
        {
            public long Id { get; set; }

            public string UserName { get; set; } = string.Empty;

            public string Body { get; set; } = string.Empty;

            public DateTime CreatedAtUtc { get; set; }
        }
        // GET: /Blog
        [HttpGet("/bai-viet")]
        [HttpGet("/blog", Name = "DoraFoodBlog")]
        [HttpGet("/Blog/index")]
        public async Task<IActionResult> Index(
            long? categoryId,
            int page = 1)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(
                    "Danh mục hoặc số trang không hợp lệ.");
            }

            const int pageSize = 6;

            var cancellationToken =
                HttpContext.RequestAborted;

            // Lấy danh mục đang hoạt động.
            var categories = await _context.Set<PostCategory>()
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .ThenBy(c => c.Id)
                .ToListAsync(cancellationToken);

            // Kiểm tra danh mục được chọn.
            if (categoryId.HasValue
                && !categories.Any(c => c.Id == categoryId.Value))
            {
                return NotFound(
                    "Danh mục không tồn tại hoặc đã ngừng hiển thị.");
            }

            var now = DateTime.UtcNow;

            // Chỉ lấy bài đã xuất bản và đã đến thời điểm đăng.
            var query = _context.Set<Post>()
                .AsNoTracking()
                .Where(p =>
                    p.Status == "PUBLISHED"
                    && p.PublishedAtUtc.HasValue
                    && p.PublishedAtUtc.Value <= now
                    && p.Category.IsActive);

            // Lọc bài thuộc trực tiếp danh mục được chọn.
            if (categoryId.HasValue)
            {
                query = query.Where(
                    p => p.CategoryId == categoryId.Value);
            }

            var totalPosts = await query.CountAsync(
                cancellationToken);

            var totalPages = (int)Math.Ceiling(
                totalPosts / (double)pageSize);

            // Giới hạn số trang trong khoảng hợp lệ.
            page = Math.Clamp(
                page,
                1,
                Math.Max(1, totalPages));

            // Đọc bài viết kèm danh mục và ảnh đại diện.
            var posts = await query
                .Include(p => p.Category)
                .Include(p => p.Cover)
                .OrderByDescending(p => p.PublishedAtUtc)
                .ThenByDescending(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            var model = new BlogIndexViewModel
            {
                Posts = posts,
                Categories = categories,
                SelectedCategoryId = categoryId,
                CurrentPage = page,
                TotalPages = totalPages,
                TotalPosts = totalPosts
            };

            return View(model);
        }

        // GET: /Blog/Details?slug=ten-bai-viet
        [HttpGet]
        public async Task<IActionResult> Details(string? slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return NotFound("Không tìm thấy bài viết.");
            }

            slug = slug.Trim();

            if (slug.Length > 250)
            {
                return NotFound("Không tìm thấy bài viết.");
            }

            var now = DateTime.UtcNow;

            var post = await _context.Set<Post>()
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.Cover)
                .FirstOrDefaultAsync(
                    p => p.Slug == slug
                        && p.Status == "PUBLISHED"
                        && p.PublishedAtUtc.HasValue
                        && p.PublishedAtUtc.Value <= now
                        && p.Category.IsActive,
                    HttpContext.RequestAborted);

            if (post == null)
            {
                return NotFound("Không tìm thấy bài viết.");
            }

            await PrepareCommentDataAsync(post);

            return View(post);
        }
        // POST: /Blog/AddComment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddComment(CommentInput input)
        {
            if (input.PostId <= 0)
            {
                return NotFound("Không tìm thấy bài viết.");
            }

            var cancellationToken = HttpContext.RequestAborted;
            var now = DateTime.UtcNow;

            // Chỉ cho bình luận vào bài đang được công khai.
            var post = await _context.Set<Post>()
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.Cover)
                .FirstOrDefaultAsync(
                    p => p.Id == input.PostId
                        && p.Status == "PUBLISHED"
                        && p.PublishedAtUtc.HasValue
                        && p.PublishedAtUtc.Value <= now
                        && p.Category.IsActive,
                    cancellationToken);

            if (post == null)
            {
                return NotFound("Không tìm thấy bài viết.");
            }

            // Đọc danh tính từ cookie, không nhận UserId từ form.
            if (User.Identity?.IsAuthenticated != true
                || !long.TryParse(
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var userId)
                || !Guid.TryParse(
                    User.FindFirstValue("SecurityStamp"),
                    out var securityStamp))
            {
                return await RedirectToCommentLoginAsync(post.Slug);
            }

            // Kiểm tra tài khoản hiện tại trong database.
            var account = await _context.Set<DoraFood.Models.User>()
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    u.IsActive,
                    u.SecurityStamp
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (account == null
                || !account.IsActive
                || account.SecurityStamp != securityStamp)
            {
                return await RedirectToCommentLoginAsync(post.Slug);
            }

            // Nội dung không hợp lệ: trả lại trang chi tiết cùng lỗi.
            if (!ModelState.IsValid)
            {
                await PrepareCommentDataAsync(post, input);

                return View("Details", post);
            }

            var comment = new PostComment
            {
                PostId = post.Id,
                UserId = userId,

                // Bước này làm bình luận trực tiếp, chưa làm trả lời.
                ParentId = null,

                Body = input.Body.Trim(),
                Status = "PENDING",
                ModeratedBy = null,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.Set<PostComment>().Add(comment);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(
                    ex,
                    "Không thể lưu bình luận cho bài viết {PostId}.",
                    post.Id);

                ModelState.AddModelError(
                    string.Empty,
                    "Chưa thể gửi bình luận lúc này. Vui lòng thử lại sau.");

                await PrepareCommentDataAsync(post, input);

                return View("Details", post);
            }

            TempData["CommentSuccess"] =
                "Đã gửi bình luận! Nội dung sẽ hiển thị sau khi được duyệt.";

            return RedirectToAction(
                nameof(Details),
                new { slug = post.Slug });
        }

        // Chuẩn bị dữ liệu bình luận cho View Details.
        private async Task PrepareCommentDataAsync(
            Post post,
            CommentInput? input = null)
        {
            var cancellationToken = HttpContext.RequestAborted;

            var query = _context.Set<PostComment>()
                .AsNoTracking()
                .Where(c =>
                    c.PostId == post.Id
                    && c.Status == "APPROVED"
                    && c.ParentId == null);

            var totalComments = await query.CountAsync(
                cancellationToken);

            // Lấy tối đa 50 bình luận trực tiếp mới nhất.
            var comments = await query
                .OrderByDescending(c => c.CreatedAtUtc)
                .ThenByDescending(c => c.Id)
                .Take(50)
                .Select(c => new CommentItem
                {
                    Id = c.Id,
                    UserName = c.User.UserName,
                    Body = c.Body,
                    CreatedAtUtc = c.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);

            ViewData["Title"] = post.Title;
            ViewData["Comments"] = comments;
            ViewData["CommentCount"] = totalComments;

            ViewData["CommentInput"] = input ?? new CommentInput
            {
                PostId = post.Id
            };
        }

        // Yêu cầu đăng nhập rồi quay lại đúng bài viết.
        private async Task<IActionResult> RedirectToCommentLoginAsync(
            string slug)
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            var returnUrl = Url.Action(
                nameof(Details),
                "Blog",
                new { slug });

            return RedirectToAction(
                "Login",
                "Account",
                new { returnUrl });
        }
    }
}