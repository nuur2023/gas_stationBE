using gas_station.ViewModels;

namespace gas_station.Data.Interfaces;

public interface IAuthRepository
{
    Task<AuthResponseViewModel?> LoginAsync(LoginRequestViewModel model);
}
