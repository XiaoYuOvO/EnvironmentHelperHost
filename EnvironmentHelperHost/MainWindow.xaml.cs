using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CsvHelper;
using HandyControl.Controls;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.SKCharts;
using LiveChartsCore.SkiaSharpView.WPF;
using MahApps.Metro.Controls;
using Microsoft.VisualBasic.FileIO;
using SkiaSharp;

namespace EnvironmentHelperHost
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private static readonly SolidColorBrush OpenBrush = new(Colors.Green);
        private static readonly SolidColorBrush CloseBrush = new(Colors.Gray);
        private DeviceController? _controller;
        private int _timeUpdateTime = 60000;
        private readonly Thread _timeUpdateThread;
        private readonly RealTimeViewModel _tempModel = new("温度(°C)", SKColors.Gold, -40, 80);
        private readonly RealTimeViewModel _humidityModel = new("湿度(%RH)", SKColors.CornflowerBlue, 0, 100);
        private readonly DirectoryInfo _directory = Directory.CreateDirectory("./Records");
        private CsvWriter? _normalRecords;
        private CsvWriter? _warningRecords;
        private float _tempLimitCache = 30;
        private volatile bool _overheated;
        private volatile bool _isRunning = true;
        private volatile int _timeoutCount = 0;

        public MainWindow()
        {
            InitializeComponent();
            foreach (var portName in SerialPort.GetPortNames())
            {
                SerialPortList.Items.Add(portName);
            }

            _timeUpdateThread = new Thread(RunDeviceLoop)
            {
                Name = "DeviceTimeUpdateThread"
            };
            _timeUpdateThread.Start();
            InitChartModel(_tempModel, TempChart);
            InitChartModel(_humidityModel, HumidityChart);
            DebugSendBuffer.Instance.Show();
        }

        private void InitChartModel(RealTimeViewModel model, CartesianChart chart)
        {
            chart.XAxes = model.XAxes;
            chart.YAxes = model.YAxes;
            chart.Series = model.Series;
            chart.Title = model.Title;
            chart.Legend = new CustomLegend();
            chart.Tooltip = new SKDefaultTooltip
            {
                FontPaint = new SolidColorPaint(new SKColor(30, 20, 30))
                {
                    SKTypeface = SKTypeface.FromFamilyName("Microsoft YaHei UI", SKFontStyle.Bold),
                    ZIndex = 10051
                }
            };
            chart.DrawMarginFrame = model.DrawMarginFrame;
        }

        private void RunDeviceLoop()
        {
            while (_isRunning)
            {
                if (_controller == null || !_controller.IsOpen())
                {
                    SafeSleep(100);
                    continue;
                }

                _controller.SetSystemTime((int)DateTime.Now.Subtract(DateTime.UnixEpoch).TotalSeconds + 1,
                    () => GrowlHelper.Error("无法同步设备时间: 设备超时"));
               
                SafeSleep(_timeUpdateTime);
            }
        }

        private static void SafeSleep(int ms)
        {
            try
            {
                Thread.Sleep(ms);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private void OpenPortButton_OnClick(object sender, RoutedEventArgs e)
        {
            lock (this)
            {
                OpenPortButton.IsEnabled = false;
                SerialPortList.IsEnabled = false;
                var selectedItem = SerialPortList.SelectedItem;
                if (selectedItem is not string selectedPortName) return;
                _controller = new DeviceController(selectedPortName);
                _controller.RegisterErrorHandler((_, args) =>
                {
                    GrowlHelper.Error($"串口异常!: {args.EventType}");
                    if (_warningRecords == null) return;
                    _warningRecords.WriteField($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    _warningRecords.WriteField($"{args.EventType}");
                    _warningRecords.WriteField($"{args.EventType}");
                    _warningRecords.WriteField(_tempLimitCache);
                    _warningRecords.NextRecord();
                    _warningRecords.Flush();
                });
                _controller.DeviceTimeout += () =>
                {
                    Interlocked.Add(ref _timeoutCount, 1);
                    if (_timeoutCount <= 5) return;
                    this.Invoke(() =>
                    {
                        ResetDeviceOpenState();
                        _timeoutCount = 0;
                        GrowlHelper.Error("超时次数过多,设备关闭!");
                    });
                };

                try
                {
                    _controller.Open();
                    InitDeviceOpened();
                    _controller.GetTempLimit(res => this.Invoke(() =>
                    {
                        _timeoutCount = 0;
                        _tempLimitCache = res;
                        TemperatureThresholdBox.Value = _tempLimitCache;
                    }), () =>
                        this.Invoke(() => { GrowlHelper.Error($"获取温度阈值失败! 设备超时"); }));
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(exception);
                    GrowlHelper.Error($"无法打开串口! {exception.Message}");
                    ResetDeviceOpenState();
                }
            }
        }

        private void InitDeviceOpened()
        {
            ClosePortButton.IsEnabled = true;
            DeviceStateLabel.Foreground = OpenBrush;
            DeviceStateLabel.Content = "已开启";
            _humidityModel.Clear();
            _tempModel.Clear();
            if (_controller == null)
            {
                return;
            }

            _controller.SensorDataUpdate += (temperature, humidity) =>
            {
                _tempModel.AddDataPoint(temperature);
                _humidityModel.AddDataPoint(humidity);
            };
            TemperatureThresholdBox.Value = _tempLimitCache;
            _normalRecords = new CsvWriter(
                new StreamWriter(File.Open($"{_directory.FullName}/{DateTime.Now.Date:yyyy-MM-dd}.csv", FileMode.Append,
                    FileAccess.Write, FileShare.Read)),
                CultureInfo.CurrentCulture);
            _warningRecords = new CsvWriter(
                new StreamWriter(File.Open($"{_directory.FullName}/WarningRecords.csv", FileMode.Append,
                    FileAccess.Write, FileShare.Read)),
                CultureInfo.CurrentCulture);

            UpdateLogState();
            _controller.SensorDataUpdate += CheckTemperatureAndWarn;
        }

        private void ResetDeviceOpenState()
        {
            lock (this)
            {
                ClosePortButton.IsEnabled = false;
                OpenPortButton.IsEnabled = true;
                SerialPortList.IsEnabled = true;
                DeviceStateLabel.Foreground = CloseBrush;
                DeviceStateLabel.Content = "未开启";
                try
                {
                    _controller?.Close();
                }
                catch (Exception)
                {
                    // ignored
                }
               
                _normalRecords?.Flush();
                _normalRecords?.Dispose();
                _warningRecords?.Flush();
                _warningRecords?.Dispose();
                _timeoutCount = 0;
            }
        }

        private void ClosePortButton_OnClick(object sender, RoutedEventArgs e)
        {
            ResetDeviceOpenState();
        }


        private void ApplyConfigButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (_controller == null)
            {
                GrowlHelper.Error("未打开设备");
                return;
            }

            _timeUpdateTime = (int)(TimeUpdateFrequencyBox.Value ?? 60000);
            _controller.SetQueryInterval((int)(UpdateFrequencyBox.Value ?? 1000));
            var tempLimit = (float)(TemperatureThresholdBox.Value ?? 30);
            ApplyConfigButton.IsEnabled = false;
            _controller.SetTempLimit(tempLimit,
                () => this.Invoke(() =>
                {
                    Growl.Info("设置成功!");
                    ApplyConfigButton.IsEnabled = true;
                }),
                () => this.Invoke(() =>
                {
                    GrowlHelper.Error("无法设置温度阈值: 设备超时");
                    ApplyConfigButton.IsEnabled = true;
                })
            );
            _tempLimitCache = tempLimit;
            _timeUpdateThread.Interrupt();
        }

        private void ToggleButton_OnChecked(object sender, RoutedEventArgs e)
        {
            if (AnimationToggle.IsChecked ?? false)
            {
                _humidityModel.EnableAnimation();
                _tempModel.EnableAnimation();
            }
            else
            {
                _humidityModel.DisableAnimation();
                _tempModel.DisableAnimation();
            }
        }

        private void DisplayCount_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            var displayCountValue = (int)(DisplayCount.Value ?? 20);
            if (displayCountValue <= 0)
            {
                displayCountValue = 20;
            }
            _tempModel.SetMaxPoints(displayCountValue);
            _humidityModel.SetMaxPoints(displayCountValue);
            DisplayCount.Value = displayCountValue;
        }

        private void LogToFileButton_OnClick(object sender, RoutedEventArgs e)
        {
            UpdateLogState();
        }

        private void UpdateLogState()
        {
            if (_controller == null)
            {
                return;
            }

            if (LogToFileButton.IsChecked ?? false)
            {
                _controller.SensorDataUpdate += LogSensorDataToFile;
            }
            else
            {
                _controller.SensorDataUpdate -= LogSensorDataToFile;
            }
        }

        private void CheckTemperatureAndWarn(float temperature, float humidity)
        {
            if (_warningRecords == null) return;
            if (temperature < _tempLimitCache)
            {
                if (_overheated)
                {
                    OnExitOverheated();
                }

                return;
            }

            if (!_overheated)
            {
                OnIntoOverheated(temperature, humidity);
            }

            _warningRecords.WriteField($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _warningRecords.WriteField(temperature);
            _warningRecords.WriteField(humidity);
            _warningRecords.WriteField(_tempLimitCache);
            _warningRecords.NextRecord();
            _warningRecords.Flush();
        }

        private void LogSensorDataToFile(float temperature, float humidity)
        {
            if (_normalRecords == null) return;
            _normalRecords.WriteField($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _normalRecords.WriteField(temperature);
            _normalRecords.WriteField(humidity);
            _normalRecords.NextRecord();
            _normalRecords.Flush();
        }

        private void OnExitOverheated()
        {
            _overheated = false;
            this.Invoke(() => { TempChart.DrawMarginFrame!.Fill = null; });
        }

        private static readonly SolidColorPaint WarningBrush = new(SKColors.Red);

        private void OnIntoOverheated(float temperature, float humidity)
        {
            Growl.WarningGlobal($"温度超出阈值! 温度: {temperature}°C 湿度: {humidity} 阈值: {_tempLimitCache:F}°C");
            _overheated = true;
            this.Invoke(() => { TempChart.DrawMarginFrame!.Fill = WarningBrush; });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _isRunning = false;
            _timeUpdateThread.Interrupt();
            _controller?.Close();
            _normalRecords?.Dispose();
            _warningRecords?.Dispose();
            base.OnClosing(e);
        }

        private void OpenRecordFileButton_OnClick(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer", _directory.FullName);
        }
    }
}