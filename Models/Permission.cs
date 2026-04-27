using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace backend.Models;

public class Permission : BaseModel
{
    public int UserId { get; set; }
    public int BusinessId { get; set; }
    public int MenuId { get; set; }
    public int? SubMenuId { get; set; }

    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }

    [ValidateNever]
    public User User { get; set; } = null!;

    [ValidateNever]
    public Business Business { get; set; } = null!;

    [ValidateNever]
    public Menu Menu { get; set; } = null!;

    [ValidateNever]
    public SubMenu? SubMenu { get; set; }
}
