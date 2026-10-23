using AssettoServer.Server.Configuration;
using FluentValidation;

namespace SonmezMakasPlugin;

public class MakasConfiguration : IValidateConfiguration<MakasConfigurationValidator>
{
    // ---- Yarış ----
    public bool RaceEnabled { get; set; } = true;   // 3x korna ile yarış (iki pluginden sadece birinde açık olsun)
    public float RaceDistanceMeters { get; set; } = 1500f;
    public float RaceRadiusMeters { get; set; } = 120f;     // 3. kornada bu mesafedeki oyuncular yarışa alınır
    public int CountdownSeconds { get; set; } = 3;          // 3-2-1
    public int FinishBannerSeconds { get; set; } = 8;
    public int RaceTimeoutSeconds { get; set; } = 240;
    public int RaceWinBonus { get; set; } = 500;
    public int HornCount { get; set; } = 3;
    public int HornWindowMs { get; set; } = 3000;

    // ---- Puan ----
    public int MaxCombo { get; set; } = 3;
    public int ComboWindowMs { get; set; } = 4000;

    // ---- Teşhis ----
    public bool DebugLog { get; set; } = true;   // 3 saniyede bir sunucu loguna AI/mesafe bilgisi yazar

    // ---- Makas ----
    public float MakasMinSpeedKmh { get; set; } = 80f;
    public float MakasCloseDistance { get; set; } = 4.0f;   // metre (araç merkezleri arası)
    public float MakasMinRelSpeedKmh { get; set; } = 15f;
    public int MakasPairCooldownMs { get; set; } = 500;

    // ---- Drift ----
    public float DriftMinSpeedKmh { get; set; } = 40f;
    public float DriftMinAngle { get; set; } = 15f;
    public float DriftMaxAngle { get; set; } = 110f;
    public float DriftPointFactor { get; set; } = 0.02f;
}

public class MakasConfigurationValidator : AbstractValidator<MakasConfiguration>
{
    public MakasConfigurationValidator()
    {
        RuleFor(c => c.RaceDistanceMeters).GreaterThan(100f);
        RuleFor(c => c.CountdownSeconds).InclusiveBetween(1, 10);
        RuleFor(c => c.MaxCombo).GreaterThanOrEqualTo(1);
    }
}
