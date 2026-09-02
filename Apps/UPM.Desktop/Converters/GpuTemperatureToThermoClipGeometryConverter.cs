using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace UPM.Desktop.Converters {
    /// <summary>
    /// GPU 온도(°C) → 온도계 실루엣 안에서 보일 영역(아래에서 위로 자람).
    /// 전체 크기 그라데이션 + 하단 정렬 Clip으로 스케일 왜곡 없이 채움.
    /// </summary>
    public class GpuTemperatureToThermoClipGeometryConverter : IValueConverter {
        public double MinTemp { get; set; } = 28;
        public double MaxTemp { get; set; } = 92;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            double w = 48, h = 58;
            if (parameter is string s && s.Contains(',')) {
                var parts = s.Split(',');
                if (parts.Length >= 2 &&
                    double.TryParse(parts[0].Trim(), NumberStyles.Any, culture, out var pw) &&
                    double.TryParse(parts[1].Trim(), NumberStyles.Any, culture, out var ph)) {
                    w = pw;
                    h = ph;
                }
            }

            double temp = 0;
            if (value is double d)
                temp = d;
            else if (value != null && double.TryParse(value.ToString(), NumberStyles.Any, culture, out var parsed))
                temp = parsed;

            double ratio;
            if (temp <= 0)
                ratio = 0.05;
            else {
                var span = MaxTemp - MinTemp;
                ratio = span > 0 ? (temp - MinTemp) / span : 1;
                ratio = Math.Clamp(ratio, 0.05, 1.0);
            }

            var clipH = Math.Max(1, h * ratio);
            var y = h - clipH;
            var geo = new RectangleGeometry(new Rect(0, y, w, clipH));
            geo.Freeze();
            return geo;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
