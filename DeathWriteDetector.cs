namespace DeadCellsUniversalQuickSave;

internal sealed record DeathWriteDecision(
    bool IsCandidate,
    long PreviousBytes,
    long CurrentBytes,
    long DropBytes,
    double Ratio,
    string Reason);

internal static class DeathWriteDetector
{
    public static DeathWriteDecision Evaluate(
        AppSettings settings,
        long previousBytes,
        long currentBytes)
    {
        if (previousBytes <= 0 || currentBytes <= 0)
        {
            return new DeathWriteDecision(
                false,
                previousBytes,
                currentBytes,
                0,
                0,
                "missing-size");
        }

        if (currentBytes >= previousBytes)
        {
            return new DeathWriteDecision(
                false,
                previousBytes,
                currentBytes,
                currentBytes - previousBytes,
                currentBytes / (double)previousBytes,
                "not-shrinking");
        }

        var drop = previousBytes - currentBytes;
        var ratio = currentBytes / (double)previousBytes;

        var previousMin = Math.Clamp(
            settings.DeathCandidatePreviousMinBytes,
            1000,
            20000);

        var currentMax = Math.Clamp(
            settings.DeathCandidateCurrentMaxBytes,
            1200,
            8000);

        var minDrop = Math.Clamp(
            settings.DeathCandidateMinDropBytes,
            256,
            8192);

        var maxRatio = Math.Clamp(
            settings.DeathCandidateDropRatio,
            0.45,
            0.90);

        if (previousBytes < previousMin)
        {
            return new DeathWriteDecision(
                false,
                previousBytes,
                currentBytes,
                drop,
                ratio,
                "previous-too-small");
        }

        if (currentBytes > currentMax)
        {
            return new DeathWriteDecision(
                false,
                previousBytes,
                currentBytes,
                drop,
                ratio,
                "current-too-large");
        }

        if (drop < minDrop)
        {
            return new DeathWriteDecision(
                false,
                previousBytes,
                currentBytes,
                drop,
                ratio,
                "drop-too-small");
        }

        if (ratio > maxRatio)
        {
            return new DeathWriteDecision(
                false,
                previousBytes,
                currentBytes,
                drop,
                ratio,
                "ratio-too-high");
        }

        return new DeathWriteDecision(
            true,
            previousBytes,
            currentBytes,
            drop,
            ratio,
            "size-collapse");
    }
}
