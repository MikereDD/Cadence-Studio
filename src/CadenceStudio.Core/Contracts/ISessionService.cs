using CadenceStudio.Core.Models;

namespace CadenceStudio.Core.Contracts;

public interface ISessionService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
