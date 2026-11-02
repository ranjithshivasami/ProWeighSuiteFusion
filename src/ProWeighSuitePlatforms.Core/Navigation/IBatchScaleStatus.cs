using System.ComponentModel;

namespace ProWeighSuitePlatforms.Core.Wpf.Navigation
{
    public interface IBatchScaleStatus : INotifyPropertyChanged
    {
        string ScaleStatusText { get; }

        bool IsScaleConnected { get; }

        void OnViewActivated();
    }
}
