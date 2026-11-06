// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Textures;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.ManiaOrtho.Highway;
using osu.Game.Tests.Visual;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// Backlog M1 gate for perspective-correct texturing: a checkerboard on a strong trapezoid. Left: subdivided (checker
    /// lines must be straight and converge on the vanishing point). Right: one quad, showing the affine bend it avoids.
    /// </summary>
    public partial class TestSceneTrackSprite : OsuTestScene
    {
        private const int squares_across = 8;
        private const int squares_along = 16;
        private const int square_size = 32;

        [Resolved]
        private IRenderer renderer { get; set; } = null!;

        private Texture checkerboard = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            var image = new Image<Rgba32>(squares_across * square_size, squares_along * square_size);

            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    bool dark = (x / square_size + y / square_size) % 2 == 0;
                    image[x, y] = dark ? new Rgba32(40, 40, 60) : new Rgba32(230, 230, 240);
                }
            }

            checkerboard = renderer.CreateTexture(image.Width, image.Height);

            // The upload takes ownership of the image.
            checkerboard.SetData(new TextureUpload(image));
        }

        [TestCase(0.2f)]
        [TestCase(0.5f)]
        public void TestCheckerboard(float farWidthRatio)
        {
            AddStep("show checkerboards", () =>
            {
                var layout = new HighwayLayout { JudgementY = 0.92f, FarY = 0.08f, NearWidth = 0.9f, FarWidthRatio = farWidthRatio };

                Child = new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            panel("subdivided (correct)", new TrackSprite { Texture = checkerboard, Layout = layout }),
                            panel("one quad (affine bend)", new TrackSprite { Texture = checkerboard, Layout = layout, Subdivide = false }),
                        }
                    }
                };
            });
        }

        private static Drawable panel(string label, Drawable content) => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black },
                content,
                new OsuSpriteText { Text = label, Margin = new MarginPadding(10) },
            }
        };
    }
}
