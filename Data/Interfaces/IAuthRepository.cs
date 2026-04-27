using backend.ViewModels;

namespace backend.Data.Interfaces;

public interface IAuthRepository
{
    Task<AuthResponseViewModel?> LoginAsync(LoginRequestViewModel model);
}
