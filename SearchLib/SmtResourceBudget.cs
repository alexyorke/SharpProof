namespace SearchLib.Smt
{
    /// <summary>
    /// Converts wall-clock-denominated SMT budgets into deterministic Z3 resource
    /// limits. Z3's rlimit counts internal solver operations, so the same query with
    /// the same budget produces the same outcome regardless of machine speed or CPU
    /// load, where a wall-clock timeout flips results nondeterministically under
    /// contention.
    /// </summary>
    public static class SmtResourceBudget
    {
        /// <summary>
        /// Calibrated against Z3 4.12.2, which consumes roughly 3,200-4,500 rlimit
        /// units per millisecond on a modern desktop core for the integer/string
        /// queries this code base emits. Using the top of that range keeps existing
        /// wall-clock-tuned budgets at least as permissive as before on typical
        /// hardware.
        /// </summary>
        public const long RlimitPerMillisecond = 4000;

        /// <summary>
        /// The wall-clock timeout is kept only as a safety net (e.g. a wedged native
        /// solver). It is scaled up so it does not bind under CPU contention, which
        /// would reintroduce load-dependent proof outcomes.
        /// </summary>
        public const int WallClockSafetyFactor = 8;

        public static uint GetRlimit(TimeSpan budget)
        {
            var rlimit = budget.TotalMilliseconds * RlimitPerMillisecond;
            if (rlimit >= uint.MaxValue)
            {
                return uint.MaxValue;
            }

            return (uint)Math.Max(1, rlimit);
        }

        public static TimeSpan GetWallClockSafetyNet(TimeSpan budget)
        {
            if (budget >= TimeSpan.FromMilliseconds(TimeSpan.MaxValue.TotalMilliseconds / WallClockSafetyFactor))
            {
                return TimeSpan.MaxValue;
            }

            return TimeSpan.FromTicks(budget.Ticks * WallClockSafetyFactor);
        }

        public static long GetMethodRlimitBudget(TimeSpan methodBudget)
        {
            var rlimit = methodBudget.TotalMilliseconds * RlimitPerMillisecond;
            if (rlimit >= long.MaxValue)
            {
                return long.MaxValue;
            }

            return (long)rlimit;
        }
    }
}
