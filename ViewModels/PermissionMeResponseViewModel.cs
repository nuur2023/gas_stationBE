namespace gas_station.ViewModels;

public class PermissionMeItemViewModel
{
    public string Route { get; set; } = "";
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
}

public class PermissionMeResponseViewModel
{
    /// <summary>SuperAdmin / Admin: all navigation allowed without checking items.</summary>
    public bool FullAccess { get; set; }

    /// <summary>When false, pool-related routes are omitted from <see cref="Items"/> (JWT business).</summary>
    public bool SupportsPool { get; set; } = true;

    public List<PermissionMeItemViewModel> Items { get; set; } = new();
}
