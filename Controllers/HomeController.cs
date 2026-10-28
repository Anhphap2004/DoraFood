using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.ComponentModel.DataAnnotations;

namespace DoraFood.Controllers
{
    public class HomeController : Controller
    {
        private readonly DoraFoodContext _context;

        public HomeController(DoraFoodContext context)
        {
            _context = context;
        }
        public class ContactInput
        {
            [Display(Name = "Họ và tên")]
            [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
            [StringLength(150,
                ErrorMessage = "Họ và tên tối đa 150 ký tự.")]
            public string FullName { get; set; } = string.Empty;

            [Display(Name = "Email")]
            [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
            [StringLength(254,
                ErrorMessage = "Email tối đa 254 ký tự.")]
            public string? Email { get; set; }

            [Display(Name = "Số điện thoại")]
            [Phone(ErrorMessage = "Số điện thoại không đúng định dạng.")]
            [StringLength(20,
                ErrorMessage = "Số điện thoại tối đa 20 ký tự.")]
            public string? PhoneNumber { get; set; }

            [Display(Name = "Chủ đề")]
            [Required(ErrorMessage = "Vui lòng nhập chủ đề.")]
            [StringLength(250,
                ErrorMessage = "Chủ đề tối đa 250 ký tự.")]
            public string Subject { get; set; } = string.Empty;

            [Display(Name = "Lời nhắn")]
            [Required(ErrorMessage = "Vui lòng nhập lời nhắn.")]
            [StringLength(5000,
                ErrorMessage = "Lời nhắn tối đa 5.000 ký tự.")]
            public string Body { get; set; } = string.Empty;
        }

        [HttpGet("/contact", Name = "DoraFoodContact")]
        [HttpGet("/contact.html")]
        [HttpGet("/Home/contact")]
        public IActionResult Contact()
        {
            return View(new ContactInput());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Contact(ContactInput input)
        {
            if (string.IsNullOrWhiteSpace(input.Email)
                && string.IsNullOrWhiteSpace(input.PhoneNumber))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Vui lòng nhập email hoặc số điện thoại để chúng tôi liên hệ lại.");
            }

            if (!ModelState.IsValid)
            {
                return View(input);
            }

            var message = new ContactMessage
            {
                FullName = input.FullName.Trim(),

                Email = string.IsNullOrWhiteSpace(input.Email)
                    ? null
                    : input.Email.Trim(),

                PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber)
                    ? null
                    : input.PhoneNumber.Trim(),

                Subject = input.Subject.Trim(),

                Body = input.Body.Trim(),

                Status = "NEW",

                UserId = null,

                AssignedTo = null,

                CreatedAtUtc = DateTime.UtcNow
            };

            _context.Set<ContactMessage>().Add(message);

            await _context.SaveChangesAsync(
                HttpContext.RequestAborted);

            TempData["ContactSuccess"] =
                "DoraFood đã nhận được lời nhắn. Cảm ơn bạn đã liên hệ!";

            return RedirectToAction(nameof(Contact));
        }

        public IActionResult Index()
        {
            return View();
        }


        [HttpGet("/gioi-thieu", Name = "DoraFoodAbout")]
        [HttpGet("/about")]
        [HttpGet("/gioithieu")]
        [HttpGet("/Home/about")]
        public async Task<IActionResult> About()
        {
            var page = await _context.Set<DoraFood.Models.Page>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    p => p.Slug == "gioi-thieu" && p.IsPublished,
                    HttpContext.RequestAborted);

            if (page == null)
            {
                return NotFound(
                    "Trang giới thiệu chưa có nội dung được xuất bản.");
            }

            ViewData["Title"] = page.Title;

            return View(page);
        }



        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(
            Duration = 0,
            Location = ResponseCacheLocation.None,
            NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id
                    ?? HttpContext.TraceIdentifier
            });
        }
    }
}