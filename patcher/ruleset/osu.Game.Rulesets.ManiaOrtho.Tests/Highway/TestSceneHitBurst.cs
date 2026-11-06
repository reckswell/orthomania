// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.ManiaOrtho.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Highway
{
    /// <summary>
    /// A contact sheet of hit bursts and hold glows, frozen at several ages, over a dark lane colour, for reviewing their look
    /// (backlog M2 lane beams and hit lighting). Columns: Perfect, Great, Good, Meh, then a hold glow. Rows: age in ms.
    /// </summary>
    public partial class TestSceneHitBurst : OsuTestScene
    {
        private static readonly double[] ages = { 20, 60, 120, 220, 350 };
        private static readonly HitResult[] results = { HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Meh };

        private const float lane = 160;

        private readonly List<ManualClock> clocks = new List<ManualClock>();

        [Test]
        public void TestContactSheet()
        {
            AddStep("show bursts", () =>
            {
                clocks.Clear();

                var sheet = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                };

                for (int row = 0; row < ages.Length; row++)
                {
                    var clock = new ManualClock();
                    clocks.Add(clock);

                    var cells = new Container
                    {
                        Y = row * lane * 0.9f,
                        AutoSizeAxes = Axes.Both,
                        Clock = new FramedClock(clock),
                    };

                    cells.Add(new OsuSpriteText { Text = $"{ages[row]} ms", Y = lane * 0.4f, Colour = Color4.Gray });

                    for (int col = 0; col <= results.Length; col++)
                    {
                        var cell = new Container
                        {
                            X = 80 + col * lane * 1.4f,
                            Size = new Vector2(lane * 1.4f, lane * 0.9f),
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(40, 22, 30, 255) },
                                new Box { Width = 2, Height = lane * 0.9f, X = lane * 0.2f, Colour = Color4.Teal },
                                new Box { Width = 2, Height = lane * 0.9f, X = lane * 1.2f, Colour = Color4.Teal },
                                new Box { Width = lane * 1.4f, Height = 3, Y = lane * 0.6f, Colour = Color4.Gold },
                            },
                        };

                        Drawable effect;

                        if (col < results.Length)
                            effect = new StandaloneBurst(results[col]);
                        else
                            effect = new HighwayHoldGlow();

                        effect.Position = new Vector2(lane * 0.7f, lane * 0.6f);
                        effect.Scale = new Vector2(lane / HighwayHitBurst.UNIT);
                        cell.Add(effect);
                        cells.Add(cell);
                    }

                    sheet.Add(cells);
                }

                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black },
                        sheet,
                    }
                };
            });

            AddUntilStep("loaded", () => Child.IsLoaded);

            AddStep("age each row", () =>
            {
                for (int row = 0; row < clocks.Count; row++)
                    clocks[row].CurrentTime = ages[row];

                foreach (var glow in this.ChildrenOfType<HighwayHoldGlow>())
                {
                    glow.SetHolding(true, 0.3f);

                    // Show it fully lit, not part way through its fade-in.
                    glow.FinishTransforms(true);
                }
            });

            AddWaitStep("let transforms apply", 3);
        }

        /// <summary>
        /// A burst used outside a pool: prepared when it loads.
        /// </summary>
        private partial class StandaloneBurst : HighwayHitBurst
        {
            public StandaloneBurst(HitResult result)
            {
                Apply(0, result, HitBurstStyle.For(result)!.Value, (int)result * 7919);
            }
        }
    }
}
