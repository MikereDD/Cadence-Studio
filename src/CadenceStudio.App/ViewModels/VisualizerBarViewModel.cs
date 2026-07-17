using System.Windows.Media;
using CadenceStudio.App.Mvvm;

namespace CadenceStudio.App.ViewModels;

public sealed class VisualizerBarViewModel : ObservableObject
{
    private double _level;
    private double _height = 6;
    private double _expandedHeight = 8;
    private Brush _fill = Brushes.Gray;
    private Brush _expandedFill = Brushes.Gray;

    public double Level
    {
        get => _level;
        set => SetProperty(ref _level, Math.Clamp(value, 0, 1));
    }

    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, Math.Clamp(value, 6, 126));
    }

    public double ExpandedHeight
    {
        get => _expandedHeight;
        set => SetProperty(ref _expandedHeight, Math.Clamp(value, 8, 240));
    }

    public Brush Fill
    {
        get => _fill;
        set => SetProperty(ref _fill, value);
    }

    public Brush ExpandedFill
    {
        get => _expandedFill;
        set => SetProperty(ref _expandedFill, value);
    }
}
