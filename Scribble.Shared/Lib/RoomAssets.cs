using System.Security.Cryptography;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Lib.Events;

namespace Scribble.Shared.Lib;

/// <summary>Translates between embedded document images and content references used only on the wire.</summary>
public static class RoomAssets
{
    /// <summary>Names an asset by its encoded file bytes, independently of its placement.</summary>
    public static string Key(byte[] bytes) => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Enumerates the content references a placement or embedded board requires.</summary>
    public static IEnumerable<string> Sources(Event edit) => edit switch
    {
        AddImageEvent image => [image.ImageBase64String],
        PasteCanvasElementsEvent paste => paste.CopiedElements.OfType<CanvasImage>()
            .Select(image => image.ImageBase64String),
        LoadCanvasEvent load => load.CanvasElements.OfType<CanvasImage>().Select(image => image.ImageBase64String),
        _ => []
    };

    /// <summary>Rewrites image sources without changing the event or its original elements.</summary>
    public static Event Map(Event edit, Func<string, string> map) => edit switch
    {
        AddImageEvent image => image with { ImageBase64String = map(image.ImageBase64String) },
        PasteCanvasElementsEvent paste => paste with { CopiedElements = Map(paste.CopiedElements, map) },
        LoadCanvasEvent load => load with { CanvasElements = Map(load.CanvasElements, map) },
        _ => edit
    };

    /// <summary>Copies image placements while preserving their IDs, geometry and owners.</summary>
    private static List<CanvasElement> Map(IEnumerable<CanvasElement> elements, Func<string, string> map) =>
        elements.Select(element =>
            element is CanvasImage image ? WithSource(image, map(image.ImageBase64String)) : element).ToList();

    /// <summary>Copies a placement with a replacement source while preserving its transform and owner.</summary>
    public static CanvasImage WithSource(CanvasImage image, string source) => new()
    {
        Id = image.Id, Bounds = image.Bounds, Rotation = image.Rotation, FlipX = image.FlipX,
        FlipY = image.FlipY, LayerIndex = image.LayerIndex, CreatorConnectionId = image.CreatorConnectionId,
        ImageBase64String = source
    };
}