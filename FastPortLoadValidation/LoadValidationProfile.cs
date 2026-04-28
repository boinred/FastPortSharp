namespace FastPortLoadValidation;

internal sealed record LoadValidationProfile(string Name, IReadOnlyList<LoadValidationStage> Stages);

internal static class LoadValidationProfiles
{
    public const string Smoke = "smoke";
    public const string Staged = "staged";

    public static bool IsKnownProfile(string profile)
    {
        return string.Equals(profile, Smoke, StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile, Staged, StringComparison.OrdinalIgnoreCase);
    }

    public static LoadValidationProfile Get(string profile)
    {
        return profile.ToLowerInvariant() switch
        {
            Smoke => CreateSmokeProfile(),
            Staged => CreateStagedProfile(),
            _ => throw new ArgumentException($"Unknown profile '{profile}'.", nameof(profile))
        };
    }

    private static LoadValidationProfile CreateSmokeProfile()
    {
        return new LoadValidationProfile(
            Smoke,
            [
                new LoadValidationStage(
                    "smoke-fixed-10",
                    Sessions: 10,
                    Payload: "fixed:1024",
                    SendRatePerSession: 1,
                    RampUp: TimeSpan.FromSeconds(2),
                    Duration: TimeSpan.FromSeconds(10),
                    MetricsInterval: TimeSpan.FromSeconds(1),
                    Thresholds: LoadValidationThresholds.Default),
                new LoadValidationStage(
                    "smoke-random-25",
                    Sessions: 25,
                    Payload: "random:4096-16384",
                    SendRatePerSession: 1,
                    RampUp: TimeSpan.FromSeconds(5),
                    Duration: TimeSpan.FromSeconds(15),
                    MetricsInterval: TimeSpan.FromSeconds(1),
                    Thresholds: LoadValidationThresholds.Default)
            ]);
    }

    private static LoadValidationProfile CreateStagedProfile()
    {
        return new LoadValidationProfile(
            Staged,
            [
                new LoadValidationStage(
                    "s1-fixed-1k",
                    Sessions: 1_000,
                    Payload: "fixed:8192",
                    SendRatePerSession: 1,
                    RampUp: TimeSpan.FromSeconds(30),
                    Duration: TimeSpan.FromMinutes(2),
                    MetricsInterval: TimeSpan.FromSeconds(1),
                    Thresholds: LoadValidationThresholds.Default),
                new LoadValidationStage(
                    "s2-random-1k",
                    Sessions: 1_000,
                    Payload: "random:4096-16384",
                    SendRatePerSession: 1,
                    RampUp: TimeSpan.FromSeconds(30),
                    Duration: TimeSpan.FromMinutes(2),
                    MetricsInterval: TimeSpan.FromSeconds(1),
                    Thresholds: LoadValidationThresholds.Default),
                new LoadValidationStage(
                    "s3-random-3k",
                    Sessions: 3_000,
                    Payload: "random:4096-16384",
                    SendRatePerSession: 1,
                    RampUp: TimeSpan.FromSeconds(60),
                    Duration: TimeSpan.FromMinutes(3),
                    MetricsInterval: TimeSpan.FromSeconds(1),
                    Thresholds: LoadValidationThresholds.Default),
                new LoadValidationStage(
                    "s4-random-5k",
                    Sessions: 5_000,
                    Payload: "random:4096-16384",
                    SendRatePerSession: 1,
                    RampUp: TimeSpan.FromSeconds(90),
                    Duration: TimeSpan.FromMinutes(5),
                    MetricsInterval: TimeSpan.FromSeconds(1),
                    Thresholds: LoadValidationThresholds.Default),
                new LoadValidationStage(
                    "s5-random-10k",
                    Sessions: 10_000,
                    Payload: "random:4096-16384",
                    SendRatePerSession: 1,
                    RampUp: TimeSpan.FromSeconds(120),
                    Duration: TimeSpan.FromMinutes(5),
                    MetricsInterval: TimeSpan.FromSeconds(1),
                    Thresholds: LoadValidationThresholds.Default)
            ]);
    }
}
