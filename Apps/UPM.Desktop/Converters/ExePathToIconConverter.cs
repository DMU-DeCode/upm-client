using System.Collections.Concurrent;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UPM.Desktop.Converters {
    /// <summary>실행 파일 경로에서 연결 아이콘 추출. 실패 시 단색 플레이스홀더.</summary>
    public class ExePathToIconConverter : IValueConverter {
        private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ImageSource Fallback = CreateSolidIcon(24, 80, 80, 82);

        private static ImageSource CreateSolidIcon(int size, byte r, byte g, byte b) {
            var wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Pbgra32, null);
            var stride = size * 4;
            var pixels = new byte[stride * size];
            for (var y = 0; y < size; y++) {
                for (var x = 0; x < size; x++) {
                    var i = y * stride + x * 4;
                    pixels[i] = b;
                    pixels[i + 1] = g;
                    pixels[i + 2] = r;
                    pixels[i + 3] = 255;
                }
            }

            wb.WritePixels(new Int32Rect(0, 0, size, size), pixels, stride, 0);
            wb.Freeze();
            return wb;
        }

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            var path = value as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return Fallback;

            return Cache.GetOrAdd(path, static p => {
                try {
                    using var icon = Icon.ExtractAssociatedIcon(p);
                    if (icon == null) return Fallback;
                    var src = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                } catch {
                    return Fallback;
                }
            });
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
