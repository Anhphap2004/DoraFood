using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Areas.Admin.ViewModels;
using DoraFood.Data;
using DoraFood.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "ADMIN,MANAGER")]
[Route("admin/feedback")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminFeedbackController : AdminModuleController
{
    private readonly IDataProtector _protector;

    public AdminFeedbackController(DoraFoodContext db, IDataProtectionProvider protection) : base(db)
    {
        _protector = protection.CreateProtector("DoraFood.AdminFeedback.Snapshot.v1");
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken,
        string kind = "comments", string? keyword = null, string? status = null, int pageNumber = 1)
    {
        if (!ModelState.IsValid || kind is not ("comments" or "reviews") || keyword?.Length > 200)
            return BadRequest("Bộ lọc nội dung không hợp lệ.");
        keyword = keyword?.Trim();
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
        if (status is not (null or "APPROVED" or "REJECTED" or "PENDING"))
            return BadRequest("Trạng thái nội dung không hợp lệ.");

        var query = FeedbackRows(kind);
        if (status is not null) query = query.Where(row => row.Status == status);
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(row => row.Target.Contains(keyword) || row.Author.Contains(keyword)
                || (row.Body != null && row.Body.Contains(keyword)));

        var total = await query.CountAsync(cancellationToken);
        var pages = Math.Max(1, (int)Math.Ceiling(total / 10d));
        pageNumber = Math.Clamp(pageNumber, 1, pages);
        var rows = await query.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id)
            .Skip((pageNumber - 1) * 10).Take(10).ToListAsync(cancellationToken);
        return View(new FeedbackIndexModel(rows, kind, keyword, status, total,
            new ManagementPager("AdminFeedback", pageNumber, pages, new Dictionary<string, string>
            { ["kind"] = kind, ["keyword"] = keyword ?? "", ["status"] = status ?? "" })));
    }

    [HttpGet("{kind}/{id:long}")]
    public async Task<IActionResult> Details(string kind, long id, CancellationToken cancellationToken)
    {
        if (kind is not ("comments" or "reviews")) return NotFound();
        var row = await FeedbackRows(kind).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null) return NotFound();

        long? parentId = null;
        var replies = 0;
        if (kind == "comments")
        {
            parentId = await Db.Set<PostComment>().AsNoTracking().Where(item => item.Id == id)
                .Select(item => item.ParentId).SingleOrDefaultAsync(cancellationToken);
            replies = await Db.Set<PostComment>().CountAsync(item => item.ParentId == id, cancellationToken);
        }
        var snapshot = _protector.Protect(JsonSerializer.Serialize(new FeedbackSnapshot(
            id, kind, row.Status, ContentHash(row.Body, row.Rating, parentId))));
        return View(new FeedbackDetailsModel(row, parentId, replies, snapshot));
    }

    [HttpPost("{kind}/{id:long}/visibility")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Visibility(string kind, long id, string? snapshot,
        string? targetStatus, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || targetStatus is not ("APPROVED" or "REJECTED"))
            return Task.FromResult<IActionResult>(BadRequest("Thao tác không hợp lệ."));
        return ChangeAsync(kind, id, snapshot, targetStatus, false, cancellationToken);
    }

    [HttpPost("{kind}/{id:long}/delete")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Delete(string kind, long id, string? snapshot,
        CancellationToken cancellationToken) =>
        ChangeAsync(kind, id, snapshot, null, true, cancellationToken);

    private IQueryable<FeedbackRow> FeedbackRows(string kind)
    {
        if (kind == "comments")
            return Db.Set<PostComment>().AsNoTracking().Select(item => new FeedbackRow
            {
                Id = item.Id, Kind = "comments", Target = item.Post.Title, Author = item.User.UserName,
                Body = item.Body, Rating = null, Status = item.Status, CreatedAtUtc = item.CreatedAtUtc
            });

        return Db.Set<DishReview>().AsNoTracking().Select(item => new FeedbackRow
        {
            Id = item.Id, Kind = "reviews", Target = item.Dish.Name, Author = item.User.UserName,
            Body = item.Body, Rating = item.Rating, Status = item.Status, CreatedAtUtc = item.CreatedAtUtc
        });
    }

    private async Task<IActionResult> ChangeAsync(string kind, long id, string? protectedSnapshot,
        string? targetStatus, bool delete, CancellationToken cancellationToken)
    {
        if (kind is not ("comments" or "reviews")) return NotFound();
        FeedbackSnapshot? expected;
        try
        {
            expected = JsonSerializer.Deserialize<FeedbackSnapshot>(_protector.Unprotect(protectedSnapshot ?? ""));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException)
        {
            return BadRequest("Phiên thao tác không hợp lệ. Hãy mở lại nội dung.");
        }
        if (expected is null || expected.Id != id || expected.Kind != kind)
            return BadRequest("Phiên thao tác không khớp nội dung.");

        try
        {
            var error = await Db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                Db.ChangeTracker.Clear();
                await using var transaction = await Db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);

                PostComment? comment = null;
                DishReview? review = null;
                if (kind == "comments")
                    comment = await Db.Set<PostComment>().FromSqlInterpolated($"""
                        SELECT * FROM dbo.PostComments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}
                        """).AsTracking().SingleOrDefaultAsync(cancellationToken);
                else
                    review = await Db.Set<DishReview>().FromSqlInterpolated($"""
                        SELECT * FROM dbo.DishReviews WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}
                        """).AsTracking().SingleOrDefaultAsync(cancellationToken);

                if (comment is null && review is null) return "Nội dung đã bị xóa.";
                var status = comment?.Status ?? review!.Status;
                var body = comment is not null ? comment.Body : review!.Body;
                if (status != expected.Status || ContentHash(body, review?.Rating, comment?.ParentId) != expected.ContentHash)
                    return "Nội dung vừa thay đổi. Hãy mở lại trang chi tiết để đối chiếu.";

                if (comment is not null)
                {
                    if (delete)
                    {
                        // Giữ các trả lời, chuyển chúng lên cấp cha của bình luận bị xóa.
                        var parentId = comment.ParentId;
                        await Db.Set<PostComment>().Where(item => item.ParentId == id)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ParentId, parentId),
                                cancellationToken);
                        Db.Set<PostComment>().Remove(comment);
                    }
                    else
                    {
                        comment.Status = targetStatus!;
                        comment.ModeratedBy = ActorId;
                    }
                }
                else if (delete) Db.Set<DishReview>().Remove(review!);
                else review!.Status = targetStatus!;

                RecordAudit(delete ? "feedback.delete" : "feedback.visibility", kind, id,
                    delete ? "Xóa nội dung; giữ các trả lời của bình luận." : $"Trạng thái: {targetStatus}.");
                await Db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return (string?)null;
            });
            TempData[error is null ? "ManagementSuccess" : "ManagementError"] = error
                ?? (delete ? "Đã xóa nội dung." : "Đã cập nhật trạng thái hiển thị.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 547 })
        {
            TempData["ManagementError"] = "Dữ liệu liên quan vừa thay đổi. Hãy tải lại trang trước khi thao tác.";
        }
        catch (SqlException exception) when (exception.Number is 547 or 1205)
        {
            TempData["ManagementError"] = "Nội dung đang được cập nhật. Hãy kiểm tra lại danh sách.";
        }
        return RedirectToAction(nameof(Index), new { kind });
    }

    private static string ContentHash(string? body, byte? rating, long? parentId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Body = body, Rating = rating, ParentId = parentId }))));
}
