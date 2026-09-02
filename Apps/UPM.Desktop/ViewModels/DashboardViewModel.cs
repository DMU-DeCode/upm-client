using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

        /// <summary>호를 따라 부드러운 그라데이션처럼 보이도록 나누는 조각 수 (많을수록 매끈함).</summary>
        private const int HeatSliceCount = 28;

        private readonly ObservableValue[] _cpuSlices = new ObservableValue[HeatSliceCount];
        private readonly ObservableValue[] _ramSlices = new ObservableValue[HeatSliceCount];
        private readonly ObservableValue[] _gpuSlices = new ObservableValue[HeatSliceCount];

        public ObservableValue CpuValue { get; } = new(0);
        public ObservableValue CpuTrackValue { get; } = new(100);
        public ObservableValue RamValue { get; } = new(0);
        public ObservableValue RamTrackValue { get; } = new(100);
        public ObservableValue GpuValue { get; } = new(0);
        public ObservableValue GpuTrackValue { get; } = new(100);
        public ObservableValue GpuTemperatureCelsius { get; } = new(0);

        public IEnumerable<ISeries> CpuGauge { get => _cpuGauge; set { _cpuGauge = value; OnPropertyChanged(); } }
        public IEnumerable<ISeries> RamGauge { get => _ramGauge; set { _ramGauge = value; OnPropertyChanged(); } }
        public IEnumerable<ISeries> GpuGauge { get => _gpuGauge; set { _gpuGauge = value; OnPropertyChanged(); } }

        /// <summary>범례 점 등 — 0~100 스케일에서 25%마다 4색 보간.</summary>
        public static SKColor UsageHeatColor(double percent) {
            var v = Math.Clamp(percent, 0, 100);
            var u = v / 25.0;
            var g = new SKColor(52, 199, 89);
            var y = new SKColor(255, 214, 10);
            var o = new SKColor(255, 159, 10);
            var r = new SKColor(255, 69, 58);
            if (u <= 1) return LerpRgb(g, y, u);
            if (u <= 2) return LerpRgb(y, o, u - 1);
            if (u <= 3) return LerpRgb(o, r, u - 2);
            return r;
        }

        private static SKColor LerpRgb(SKColor a, SKColor b, double t) {
            t = Math.Clamp(t, 0, 1);
            return new SKColor(
                (byte)Math.Round(a.Red + (b.Red - a.Red) * t),
                (byte)Math.Round(a.Green + (b.Green - a.Green) * t),
                (byte)Math.Round(a.Blue + (b.Blue - a.Blue) * t));
        }

        public DashboardViewModel() {
            for (var i = 0; i < HeatSliceCount; i++) {
                _cpuSlices[i] = new ObservableValue(0);
                _ramSlices[i] = new ObservableValue(0);
                _gpuSlices[i] = new ObservableValue(0);
            }

            _cpuGauge = CreateArcHeatGauge(_cpuSlices, CpuTrackValue, "CPU");
            _ramGauge = CreateArcHeatGauge(_ramSlices, RamTrackValue, "RAM");
            _gpuGauge = CreateArcHeatGauge(_gpuSlices, GpuTrackValue, "GPU");
        }

        public void ApplyCpuGauge(double cpuPercent) {
            var v = Math.Clamp(cpuPercent, 0, 100);
            CpuValue.Value = v;
            CpuTrackValue.Value = 100 - v;
            FillHeatSlices(_cpuSlices, v);
        }

        public void ApplyRamGauge(double ramPercent) {
            var v = Math.Clamp(ramPercent, 0, 100);
            RamValue.Value = v;
            RamTrackValue.Value = 100 - v;
            FillHeatSlices(_ramSlices, v);
        }

        /// <summary>호·중앙 숫자는 GPU 사용률(%), <see cref="GpuTemperatureCelsius"/>는 별도 표시용.</summary>
        public void ApplyGpuGauge(double gpuUsagePercent, double temperatureCelsius) {
            var u = Math.Clamp(gpuUsagePercent, 0, 100);
            GpuValue.Value = u;
            GpuTrackValue.Value = 100 - u;
            GpuTemperatureCelsius.Value = temperatureCelsius;
            FillHeatSlices(_gpuSlices, u);
        }

        private static void FillHeatSlices(ObservableValue[] slices, double amount) {
            var n = slices.Length;
            for (var i = 0; i < n; i++) {
                var lo = i * 100.0 / n;
                var hi = (i + 1) * 100.0 / n;
                slices[i].Value = Math.Max(0, Math.Min(amount, hi) - lo);
            }
        }

        /// <summary>호 바깥 ‘빈’ 구간 — 순검정에 가깝지 않게 중간 회색 톤.</summary>
        private static readonly SKColor ArcTrackEmptyColor = new(76, 78, 86);

        /// <summary>
        /// 호 시작(저부하)에서 초록 → 끝(고부하)으로 빨강까지, 각 조각은 스케일 위치의 색으로 칠함 (호 방향 스윕과 동일한 분위기).
        /// </summary>
        private static IEnumerable<ISeries> CreateArcHeatGauge(ObservableValue[] slices, ObservableValue track, string tag) {
            var trackPaint = new SolidColorPaint(ArcTrackEmptyColor);
            var list = new List<ISeries>();
            var n = slices.Length;
            for (var i = 0; i < n; i++) {
                var mid = (i + 0.5) * 100.0 / n;
                var color = UsageHeatColor(mid);
                list.Add(new PieSeries<ObservableValue> {
                    Values = new[] { slices[i] },
                    Name = $"{tag}_{i}",
                    InnerRadius = 43,
                    MaxRadialColumnWidth = 8,
                    CornerRadius = 0,
                    Fill = new SolidColorPaint(color),
                    ShowDataLabels = false,
                    IsHoverable = false,
                    HoverPushout = 0
                });
            }

            list.Add(new PieSeries<ObservableValue> {
                Values = new[] { track },
                Name = $"{tag}_track",
                InnerRadius = 43,
                MaxRadialColumnWidth = 8,
                CornerRadius = 0,
                Fill = trackPaint,
                ShowDataLabels = false,
                IsHoverable = false,
                HoverPushout = 0
            });

            return list;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
