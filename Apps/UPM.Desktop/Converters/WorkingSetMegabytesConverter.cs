using System;
using System.Globalization;
using System.Windows.Data;

namespace UPM.Desktop.Converters {
    public class WorkingSetMegabytesConverter : IValueConverter {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            if (value is long bytes) return $"{bytes / (1024.0 * 1024.0):0.0} MB";
            if (value is int ib) return $"{ib / (1024.0 * 1024.0):0.0} MB";
            return "—";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
