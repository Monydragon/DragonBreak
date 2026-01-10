using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DragonBreak.Core.Graphics;

public static class PixelTexture
{
    public static Texture2D Create(GraphicsDevice graphicsDevice)
    {
        var tex = new Texture2D(graphicsDevice, 1, 1, false, SurfaceFormat.Color);
        tex.SetData(new[] { Color.White });
        return tex;
    }
}

