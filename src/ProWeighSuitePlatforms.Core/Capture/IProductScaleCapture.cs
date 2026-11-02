using ProWeighSuitePlatforms.Models;
using ProWeighSuitePlatforms.Services;

namespace ProWeighSuitePlatforms.Platform.Weighing.Capture
{
    public interface IProductScaleCapture
    {
        void Start(ScaleReaderService reader, ScaleSettings settings, string dbPath);

        void Stop();
    }

    public static class ProductScaleCaptureLocator
    {
        public static IProductScaleCapture? Current { get; set; }
    }
}
