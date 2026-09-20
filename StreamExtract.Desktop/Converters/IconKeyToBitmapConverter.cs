using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace StreamExtract.Desktop.Converters;

public class IconKeyToBitmapConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string iconKey || string.IsNullOrWhiteSpace(iconKey))
            return null;

        return Cache.GetOrAdd(iconKey, key =>
        {
            try
            {
                var uri = new Uri($"avares://StreamExtract.Desktop/Assets/{key}.png");
                if (AssetLoader.Exists(uri))
                {
                    using var stream = AssetLoader.Open(uri);
                    return new Bitmap(stream);
                }
            }
            catch (Exception ex)
            {
                // Fallback gracefully if asset missing, but surface it during development.
                Debug.WriteLine($"[IconKeyToBitmapConverter] Failed to load asset '{key}': {ex.Message}");
            }
            return null;
        });
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
