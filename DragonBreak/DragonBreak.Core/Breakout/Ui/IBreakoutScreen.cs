#nullable enable
using DragonBreak.Core.Input;
using Microsoft.Xna.Framework.Graphics;

namespace DragonBreak.Core.Breakout.Ui;

internal interface IBreakoutScreen
{
    void Update(DragonBreakInput[] inputs, Viewport vp, float dtSeconds);
    void Draw(SpriteBatch sb, Viewport vp);
}
