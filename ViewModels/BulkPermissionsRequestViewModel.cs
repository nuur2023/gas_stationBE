namespace gas_station.ViewModels;

public class BulkPermissionItemViewModel
{
    public int MenuId { get; set; }
    public int? SubMenuId { get; set; }
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
}

public class BulkPermissionsRequestViewModel
{
    public int UserId { get; set; }
    public int BusinessId { get; set; }
    public List<BulkPermissionItemViewModel> Items { get; set; } = new();
}
