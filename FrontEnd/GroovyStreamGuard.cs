namespace MiSTerCast
{
    enum GroovyStreamRepairAction
    {
        LaunchExisting,
        OfferUpdateOrLaunchExisting,
        UpdateLatest,
        InstallLatest
    }

    static class GroovyStreamGuard
    {
        public static GroovyStreamRepairAction DecideRepairAction(GroovyTargetStatus status, GroovyReleaseInfo latestRelease)
        {
            if (status == null || status.Inventory == null ||
                !status.Inventory.HasMisterBinary ||
                !status.Inventory.HasGroovyRbf)
                return GroovyStreamRepairAction.InstallLatest;

            if (latestRelease == null)
                return GroovyStreamRepairAction.LaunchExisting;

            if (status.Manifest == null)
                return GroovyStreamRepairAction.OfferUpdateOrLaunchExisting;

            return status.Manifest.MatchesRelease(latestRelease)
                ? GroovyStreamRepairAction.LaunchExisting
                : GroovyStreamRepairAction.UpdateLatest;
        }
    }
}
