using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed record NavigationParentOption(
    long Id,
    long MenuId,
    string Label);

public sealed class NavigationItemCreateInput
{
    [Required(ErrorMessage = "Anh chưa chọn nhóm menu.")]
    public long? MenuId { get; set; }

    public long? ParentId { get; set; }

    public NavigationItemEditInput Item { get; set; } = new()
    {
        IsVisible = true
    };

    [BindNever]
    [ValidateNever]
    public List<SelectListItem> MenuOptions { get; set; } = new();

    [BindNever]
    [ValidateNever]
    public List<NavigationParentOption> ParentOptions { get; set; } = new();
}