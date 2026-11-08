using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Core.Storage;

namespace Oluso.Imaging;

public static class ImagingServiceCollectionExtensions
{
    /// <summary>
    /// Register the default (SkiaSharp, MIT-licensed) image resizer used by the
    /// local/dev public-image shim. Safe to call more than once.
    /// </summary>
    public static IServiceCollection AddImageResizing(this IServiceCollection services)
    {
        services.TryAddSingleton<IIconImageNormalizer, IconImageNormalizer>();
        services.TryAddSingleton<IImageResizer, SkiaImageResizer>();
        services.TryAddScoped<IPublicImageVariantGenerator, PublicImageVariantGenerator>();
        return services;
    }
}
