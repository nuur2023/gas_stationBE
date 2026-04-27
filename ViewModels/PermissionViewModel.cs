namespace backend.ViewModels;

public class PermissionViewModel
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int BusinessId { get; set; }
    public int MenuId { get; set; }
    public int? SubMenuId { get; set; }
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
}
