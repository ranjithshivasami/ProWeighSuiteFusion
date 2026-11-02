using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Security
{
    public interface ICurrentUserContext
    {
        User? CurrentUser { get; }
    }
}
