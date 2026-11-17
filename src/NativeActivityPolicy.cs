namespace UmapyonPresence
{
    internal static class NativeActivityPolicy
    {
        internal static ActivityState Select(CareerSnapshot career, ActivityState concert, ActivityState race, NativeMenuSnapshot menu = null)
        {
            if (career != null && career.Legacy) return career.ToState();
            return concert ?? race ?? (career != null ? career.ToState() : menu == null ? null : menu.ToState());
        }
        internal static bool CareerContextChanged(CareerSnapshot previous, CareerSnapshot next)
        {
            return previous == null || next == null || previous.Card != next.Card || previous.Scenario != next.Scenario || previous.Results != next.Results || previous.Legacy != next.Legacy || previous.BareLegacy != next.BareLegacy ||
                previous.PracticePartner != next.PracticePartner || previous.RecordId != next.RecordId || previous.RivalTrainer != next.RivalTrainer || previous.RivalPlacement != next.RivalPlacement || previous.RivalMarginKind != next.RivalMarginKind || previous.RivalGapLengths != next.RivalGapLengths || previous.Turn != next.Turn || previous.Stats != next.Stats || previous.ResultLine != next.ResultLine ||
                previous.RivalGapSeconds != next.RivalGapSeconds ||
                (previous.Extras == null ? null : previous.Extras.Tooltip("")) != (next.Extras == null ? null : next.Extras.Tooltip("")) ||
                (next.Legacy && LegacyTooltip(previous) != LegacyTooltip(next));
        }
        private static string LegacyTooltip(CareerSnapshot career) { return career.LegacyDetails == null ? null : career.BareLegacy ? career.LegacyDetails.BareTooltip() : career.LegacyDetails.Tooltip(""); }
        internal static ActivityState ClearEnded(ActivityState previous, ActivityState selected)
        {
            return selected == null && (previous.Concert != null || previous.Career != null || previous.Race != null || previous.Source == NativeMenuSnapshot.ActivitySource) ? new ActivityState() : previous;
        }
    }
}
