using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.Measure;
using LiveChartsCore.Defaults;
using SkiaSharp;

namespace UPM.Desktop.ViewModels {
    public class DashboardViewModel : INotifyPropertyChanged {
        private IEnumerable<ISeries> _cpuGauge;
        private IEnumerable<ISeries> _ramGauge;
        private IEnumerable<ISeries> _gpuGauge;

        // 게이지 채움 / 트랙(합계 100) — 값이 하나뿐이면 항상 풀 서클이 되므로 둘 다 필요함
        public ObservableValue CpuValue { get; } = new(0);
        public ObservableValue CpuTrackValue { get; } = new(100);
        public ObservableValue RamValue { get; } = new(0);
        public ObservableValue RamTrackValue { get; } = new(100);
        /// <summary>GPU 호(0–100) 게이지 채움. 실제 °C는 <see cref="GpuTemperatureCelsius"/>.</summary>
        public ObservableValue GpuValue { get; } = new(0);
        public ObservableValue GpuTrackValue { get; } = new(100);
        public ObservableValue GpuTemperatureCelsius { get; } = new(0);

        // View와 바인딩될 프로퍼티들
        public IEnumerable<ISeries> CpuGauge { get => _cpuGauge; set { _cpuGauge = value; OnPropertyChanged(); } }
        public IEnumerable<ISeries> RamGauge { get => _ramGauge; set { _ramGauge = value; OnPropertyChanged(); } }
        public IEnumerable<ISeries> GpuGauge { get => _gpuGauge; set { _gpuGauge = value; OnPropertyChanged(); } }

        public DashboardViewModel() {
            // Apple 스타일의 깔끔한 게이지 초기화
            _cpuGauge = CreatePercentDonutGauge(CpuValue, CpuTrackValue, "CPU", new SKColor(10, 132, 255));
            _ramGauge = CreatePercentDonutGauge(RamValue, RamTrackValue, "RAM", new SKColor(48, 209, 88));
            _gpuGauge = CreateTemperatureDonutGauge(GpuValue, GpuTrackValue, GpuTemperatureCelsius, "GPU", new SKColor(255, 159, 10));
        }

        private static IEnumerable<ISeries> CreatePercentDonutGauge(
            ObservableValue fill,
            ObservableValue track,
            string label,
            SKColor color) {
            var trackPaint = new SolidColorPaint(new SKColor(58, 58, 60));
            return new ISeries[] {
                new PieSeries<ObservableValue> {
                    Values = new[] { fill },
                    Name = label,
                    InnerRadius = 43,
                    MaxRadialColumnWidth = 8,
                    CornerRadius = 40,
                    DataLabelsSize = 23,
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsPosition = PolarLabelsPosition.ChartCenter,
                    DataLabelsFormatter = _ => $"{fill.Value:0}%",
                    Fill = new SolidColorPaint(color),
                    IsHoverable = false,
                    HoverPushout = 0
                },
                new PieSeries<ObservableValue> {
                    Values = new[] { track },
                    Name = label + " (track)",
                    InnerRadius = 43,
                    MaxRadialColumnWidth = 8,
                    CornerRadius = 0,
                    Fill = trackPaint,
                    ShowDataLabels = false,
                    IsHoverable = false,
                    HoverPushout = 0
                }
            };
        }

        private static IEnumerable<ISeries> CreateTemperatureDonutGauge(
            ObservableValue fill,
            ObservableValue track,
            ObservableValue celsiusDisplay,
            string label,
            SKColor color) {
            var trackPaint = new SolidColorPaint(new SKColor(58, 58, 60));
            return new ISeries[] {
                new PieSeries<ObservableValue> {
                    Values = new[] { fill },
                    Name = label,
                    InnerRadius = 43,
                    MaxRadialColumnWidth = 8,
                    CornerRadius = 40,
                    DataLabelsSize = 23,
                    DataLabelsPaint = new SolidColorPaint(SKColors.White),
                    DataLabelsPosition = PolarLabelsPosition.ChartCenter,
                    DataLabelsFormatter = _ => $"{celsiusDisplay.Value:0}°C",
                    Fill = new SolidColorPaint(color),
                    IsHoverable = false,
                    HoverPushout = 0
                },
                new PieSeries<ObservableValue> {
                    Values = new[] { track },
                    Name = label + " (track)",
                    InnerRadius = 43,
                    MaxRadialColumnWidth = 8,
                    CornerRadius = 0,
                    Fill = trackPaint,
                    ShowDataLabels = false,
                    IsHoverable = false,
                    HoverPushout = 0
                }
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}