// Helpful video by javidx9 https://youtu.be/ZQ8qtAizis4?si=wrfBlnjDrZ-yroBC

using System;
using SkiaSharp;

namespace Scribble.State;

/// <summary>
/// Manages world to screen space coordinate conversions
/// </summary>
public static class CameraState
{
    // The offset of the infinite world space from the screen space (viewport)
    public static float WorldOffSetX { get; set; }
    public static float WorldOffSetY { get; set; }

    // Clamp zoom between 10% and 500%
    public const float MinZoom = 0.1f;
    public const float MaxZoom = 5.0f;
    public static float Zoom { get; private set; } = 1.0f;

    public static void SetZoom(float newZoom)
    {
        Zoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
    }

    public static SKPoint ScreenToWorld(SKPoint screenPoint)
    {
        return new SKPoint(screenPoint.X / Zoom + WorldOffSetX, screenPoint.Y / Zoom + WorldOffSetY);
    }

    public static SKPoint WorldToScreen(SKPoint worldPoint)
    {
        return new SKPoint((worldPoint.X - WorldOffSetX) * Zoom, (worldPoint.Y - WorldOffSetY) * Zoom);
    }

    public static SKMatrix GetViewMatrix()
    {
        var matrix = SKMatrix.CreateScale(Zoom, Zoom);
        // Negate the translation to give the illusion that the canvas elements are moving
        // to the opposite direction of the camera
        matrix = matrix.PostConcat(SKMatrix.CreateTranslation(-WorldOffSetX * Zoom, -WorldOffSetY * Zoom));
        return matrix;
    }

    public static void Reset()
    {
        WorldOffSetX = 0;
        WorldOffSetY = 0;
        Zoom = 1.0f;
    }
}