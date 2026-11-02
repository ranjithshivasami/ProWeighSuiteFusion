using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Core.Security
{
    public interface ICurrentUserContext
    {
        User? CurrentUser { get; }
    }
}
