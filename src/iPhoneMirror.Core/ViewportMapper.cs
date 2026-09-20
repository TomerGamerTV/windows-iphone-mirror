namespace iPhoneMirror.Core;

public static class ViewportMapper
{
    public static Viewport CalculateViewport(
        double hostWidth,
        double hostHeight,
        double sourceWidth,
        double sourceHeight,
        double dpiScale = 1.0)
    {
        if (hostWidth <= 0 || hostHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return new Viewport(0, 0, 0, 0);
        }

        dpiScale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1.0;
        var pixelWidth = hostWidth * dpiScale;
        var pixelHeight = hostHeight * dpiScale;
        var sourceAspect = sourceWidth / sourceHeight;
        var hostAspect = pixelWidth / pixelHeight;

        double width;
        double height;
        if (hostAspect > sourceAspect)
        {
            height = pixelHeight;
            width = height * sourceAspect;
        }
        else
        {
            width = pixelWidth;
            height = width / sourceAspect;
        }

        return new Viewport(
            (pixelWidth - width) / 2.0 / dpiScale,
            (pixelHeight - height) / 2.0 / dpiScale,
            width / dpiScale,
            height / dpiScale);
    }

    public static PhonePoint? MapToPhone(
        double x,
        double y,
        double hostWidth,
        double hostHeight,
        double sourceWidth,
        double sourceHeight,
        double dpiScale = 1.0,
        bool clamp = false)
    {
        var viewport = CalculateViewport(hostWidth, hostHeight, sourceWidth, sourceHeight, dpiScale);
        if (viewport.Width <= 1 || viewport.Height <= 1)
        {
            return null;
        }

        var localX = x - viewport.Left;
        var localY = y - viewport.Top;
        if (!clamp && (localX < 0 || localY < 0 || localX >= viewport.Width || localY >= viewport.Height))
        {
            return null;
        }

        localX = Math.Clamp(localX, 0, viewport.Width - 1);
        localY = Math.Clamp(localY, 0, viewport.Height - 1);
        var px = (ushort)Math.Round(localX / (viewport.Width - 1) * ushort.MaxValue);
        var py = (ushort)Math.Round(localY / (viewport.Height - 1) * ushort.MaxValue);
        return new PhonePoint(px, py);
    }
}
