using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.VisualElements;
using LiveChartsCore.VisualElements;
using SkiaSharp;

namespace EnvironmentHelperHost;

public class RealTimeViewModel : ObservableObject
{
    private readonly List<DateTimePoint> _values = new();
    private readonly Axis _xAxis;
    private int _maxPointCount = 20;
    private readonly LineSeries<DateTimePoint> _lineSeries;
    private readonly Axis _yAxis;
    public RealTimeViewModel(string name, SKColor color, int minValue, int maxValue)
    {
        _lineSeries = new LineSeries<DateTimePoint>
        {
            Values = _values,
            LineSmoothness = 0.3,
            Stroke = new SolidColorPaint(color),
            Fill = new SolidColorPaint(color.WithAlpha(127)),
            GeometryFill = new SolidColorPaint(color),
            GeometryStroke  = new SolidColorPaint(color),
            AnimationsSpeed = TimeSpan.FromMilliseconds(200),
            Name = name
        };
        Series = new ObservableCollection<ISeries>
        {
            _lineSeries
        };

        _xAxis = new DateTimeAxis(TimeSpan.FromSeconds(1), Formatter)
        {
            CustomSeparators = GetSeparators(),
            AnimationsSpeed = TimeSpan.FromMilliseconds(0),
            SeparatorsPaint = new SolidColorPaint(SKColors.Black.WithAlpha(100)),
        };

        _yAxis = new()
        {
            AnimationsSpeed = TimeSpan.FromMilliseconds(0),
            MinStep = 1,
            MinLimit = minValue,
            MaxLimit = maxValue,
        };
      

        XAxes = new[] { _xAxis };
        YAxes = new[] { _yAxis };
        Title = new LabelVisual
        {
            Text = name,
            TextSize = 25,
            Padding = new LiveChartsCore.Drawing.Padding(15),
            Paint = new SolidColorPaint(SKColors.DarkSlateGray)
        };
    }

    public ObservableCollection<ISeries> Series { get; set; }
    public LabelVisual Title { get; set; }
    public Axis[] XAxes { get; set; }
    public Axis[] YAxes { get; set; }

    private IEnumerable<double> GetSeparators()
    {
        var separators = new List<double>();
        if (_values.Count != 0)
        {
            var index = 0;
            var step = Math.Max(_values.Count / 5, 1);            
            while (index < _values.Count - 1)
            {
                separators.Add(_values[index].DateTime.Ticks);                
                index += step;
            }
        }
        separators.Add(DateTime.Now.Ticks);
        return separators.ToArray();
    }

    public void AddDataPoint(float data)
    {
        lock (this)
        {
            if (_values.Count > _maxPointCount)
            {
                _values.RemoveAt(0);
            }

            _values.Add(new DateTimePoint(DateTime.Now, data));
            _xAxis.CustomSeparators = GetSeparators();    
        }
    }

    public void Clear()
    {
        _values.Clear();
    }

    public void SetMaxPoints(int count)
    {
        if (count < _maxPointCount)
        {
            for (var i = 0; i < _maxPointCount - count; i++)
            {
                _values.RemoveAt(0);
            }
        }
        _maxPointCount = count;
    }

    public void DisableAnimation()
    {
        _lineSeries.AnimationsSpeed = TimeSpan.Zero;
    }

    public void EnableAnimation()
    {
        _lineSeries.AnimationsSpeed = TimeSpan.FromMilliseconds(200);
    }
    
    public DrawMarginFrame DrawMarginFrame => new();
    
    private static string Formatter(DateTime date)
    {
        var secsAgo = (DateTime.Now - date).TotalSeconds;

        return secsAgo < 1
            ? "0s"
            : $"{secsAgo:N0}s";
    }
    
}