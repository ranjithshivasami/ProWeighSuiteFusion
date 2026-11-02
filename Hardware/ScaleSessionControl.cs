using System.Threading.Tasks;
using ProWeighSuitePlatforms.Models;

namespace ProWeighSuitePlatforms.Services
{
    public interface IScaleSessionControl
    {
        Task<bool> ConnectScaleAsync(ScaleSettings? settings = null);

        void DisconnectScale();
    }

    public sealed class ScaleSessionControlBridge : IScaleSessionControl
    {
        private IScaleSessionControl? _inner;

        public void Attach(IScaleSessionControl? inner) => _inner = inner;

        public Task<bool> ConnectScaleAsync(ScaleSettings? settings = null) =>
            _inner?.ConnectScaleAsync(settings) ?? Task.FromResult(false);

        public void DisconnectScale() => _inner?.DisconnectScale();
    }
}
