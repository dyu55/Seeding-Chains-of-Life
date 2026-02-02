using System.Collections;
using UnityEngine;

namespace SCoL
{
    /// <summary>
    /// Simple fire spread behavior as requested:
    /// - Ignite a cell -> turns red immediately
    /// - Spreads outward up to 3 cells
    /// - 1 cell per second (rings), total 3 seconds
    /// </summary>
    public static class FireSpreadSimple
    {
        public static IEnumerator Spread(SCoLRuntime runtime, int x, int y, int maxDistance = 3, float secondsPerStep = 1f)
        {
            // Step 0: center
            Ignite(runtime, x, y);
            runtime.ForceRender();

            for (int d = 1; d <= maxDistance; d++)
            {
                yield return new WaitForSeconds(secondsPerStep);

                for (int dy = -d; dy <= d; dy++)
                {
                    for (int dx = -d; dx <= d; dx++)
                    {
                        // ring only
                        if (Mathf.Abs(dx) != d && Mathf.Abs(dy) != d) continue;
                        Ignite(runtime, x + dx, y + dy);
                    }
                }

                runtime.ForceRender();
            }

            // Optional: keep them red but stop "on fire" after spread duration
            yield return new WaitForSeconds(0.25f);
            ExtinguishArea(runtime, x, y, maxDistance);
            runtime.ForceRender();
        }

        private static void Ignite(SCoLRuntime runtime, int x, int y)
        {
            if (runtime?.Grid == null) return;
            if (!runtime.Grid.InBounds(x, y)) return;
            var c = runtime.Grid.Get(x, y);
            c.IsOnFire = true;
            c.FireFuel = 1.0f;
        }

        private static void ExtinguishArea(SCoLRuntime runtime, int cx, int cy, int r)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (runtime.Grid.InBounds(x, y))
                    {
                        var c = runtime.Grid.Get(x, y);
                        c.IsOnFire = false;
                        c.FireFuel = 0f;
                        c.PlantStage = PlantStage.Burnt;
                    }
                }
            }
        }
    }
}
