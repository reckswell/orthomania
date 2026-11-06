// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.Playtest
{
    /// <summary>
    /// Builds a replay that plays like an imperfect human: autoplay's key presses and releases, shifted by seeded
    /// normally-distributed timing error. The result produces a realistic mix of judgements (including misses), which
    /// is what a parity test needs. Autoplay alone only ever produces perfect judgements.
    /// </summary>
    public static class HumanisedReplayGenerator
    {
        /// <param name="beatmap">Any beatmap that can be played as osu!mania.</param>
        /// <param name="seed">RNG seed. The same seed always gives the same replay.</param>
        /// <param name="meanErrorMs">Average press offset (positive = late).</param>
        /// <param name="sdErrorMs">Standard deviation of the press offset. 30ms gives mostly GREAT/GOOD with a few misses at OD8.</param>
        public static Replay Generate(IBeatmap beatmap, int seed, double meanErrorMs = 4, double sdErrorMs = 30)
        {
            var playable = new TestWorkingBeatmap(beatmap).GetPlayableBeatmap(new ManiaRuleset().RulesetInfo);
            var auto = new ManiaModAutoplay().CreateScoreFromReplayData(playable, Array.Empty<Mod>()).Replay;
            return Humanise(auto, seed, meanErrorMs, sdErrorMs);
        }

        public static Replay Humanise(Replay auto, int seed, double meanErrorMs, double sdErrorMs)
        {
            var random = new Random(seed);

            double gaussian()
            {
                // Box-Muller.
                double u1 = 1.0 - random.NextDouble();
                double u2 = random.NextDouble();
                return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            }

            // 1. Turn frames into press/release intervals per action.
            var intervals = new Dictionary<ManiaAction, List<(double press, double release)>>();
            var openPresses = new Dictionary<ManiaAction, double>();
            var held = new HashSet<ManiaAction>();

            foreach (var frame in auto.Frames.Cast<ManiaReplayFrame>())
            {
                var now = frame.Actions.ToHashSet();

                foreach (var pressed in now.Except(held))
                    openPresses[pressed] = frame.Time;

                foreach (var released in held.Except(now))
                {
                    if (!intervals.TryGetValue(released, out var list))
                        intervals[released] = list = new List<(double, double)>();

                    list.Add((openPresses[released], frame.Time));
                    openPresses.Remove(released);
                }

                held = now;
            }

            // 2. Jitter each interval, keeping intervals on the same key ordered and non-overlapping.
            var events = new List<(double time, ManiaAction action, bool down)>();

            foreach (var (action, list) in intervals.OrderBy(kvp => kvp.Key))
            {
                double previousRelease = double.MinValue;

                foreach (var (press, release) in list)
                {
                    double newPress = press + meanErrorMs + gaussian() * sdErrorMs;
                    double newRelease = newPress + (release - press) + gaussian() * sdErrorMs * 0.5;

                    newPress = Math.Max(newPress, previousRelease + 1);
                    newRelease = Math.Max(newRelease, newPress + 1);

                    events.Add((newPress, action, true));
                    events.Add((newRelease, action, false));
                    previousRelease = newRelease;
                }
            }

            // 3. Rebuild frames, one per distinct event time.
            var replay = new Replay();
            var state = new HashSet<ManiaAction>();

            foreach (var group in events.OrderBy(e => e.time).GroupBy(e => Math.Round(e.time, 3)))
            {
                foreach (var e in group)
                {
                    if (e.down)
                        state.Add(e.action);
                    else
                        state.Remove(e.action);
                }

                replay.Frames.Add(new ManiaReplayFrame(group.Key, state.OrderBy(a => a).ToArray()));
            }

            return replay;
        }
    }
}
