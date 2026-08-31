using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Orion.Api.Authentication;
using Orion.Core.Configuration;

namespace Orion.Tests.Services;

/// <summary>
/// Le frein doit plafonner la devinette SANS jamais enfermer le proprietaire dehors. Ces deux
/// proprietes sont en tension : c'est pour ca qu'elles sont testees toutes les deux.
/// </summary>
public class LoginThrottleTests
{
    private static LoginThrottle Build(int failures = 3, int minutes = 15)
    {
        var options = Options.Create(new AuthOptions
        {
            LoginFailuresPerWindow = failures,
            LoginWindowMinutes = minutes,
        });

        return new LoginThrottle(options, Mock.Of<ILogger<LoginThrottle>>());
    }

    [Fact]
    public void IsBlocked_CleanSlate_False()
    {
        Assert.False(Build().IsBlocked(out var retryAfter));
        Assert.Equal(TimeSpan.Zero, retryAfter);
    }

    [Fact]
    public void IsBlocked_BelowQuota_False()
    {
        var throttle = Build(failures: 3);

        throttle.RecordFailure();
        throttle.RecordFailure();

        Assert.False(throttle.IsBlocked(out _));
    }

    [Fact]
    public void IsBlocked_AtQuota_ReturnsUsableDelay()
    {
        var throttle = Build(failures: 3, minutes: 15);

        throttle.RecordFailure();
        throttle.RecordFailure();
        throttle.RecordFailure();

        Assert.True(throttle.IsBlocked(out var retryAfter));

        // Un delai nul ou negatif dirait au client de reessayer dans le passe.
        Assert.True(retryAfter > TimeSpan.Zero);
        Assert.True(retryAfter <= TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void Reset_AfterSuccess_ClearsSlate()
    {
        var throttle = Build(failures: 3);

        throttle.RecordFailure();
        throttle.RecordFailure();
        throttle.RecordFailure();
        Assert.True(throttle.IsBlocked(out _));

        throttle.Reset();

        Assert.False(throttle.IsBlocked(out _));
    }

    [Fact]
    public void IsBlocked_OldFailures_SlideOutOfWindow()
    {
        // Fenetre nulle : tout echec est deja expire quand on interroge. Prouve la purge sans
        // faire dormir le test.
        var throttle = Build(failures: 1, minutes: 0);

        throttle.RecordFailure();
        throttle.RecordFailure();

        Assert.False(throttle.IsBlocked(out _));
    }

    [Fact]
    public void Reset_QuotaExhausted_OwnerStillGetsIn()
    {
        // Le controleur verifie le mot de passe AVANT de consulter le frein : un mot de passe
        // correct n'enregistre aucun echec et remet le compteur a zero. C'est ce qui empeche
        // l'attaque de devenir un deni de service.
        var throttle = Build(failures: 2);

        throttle.RecordFailure();
        throttle.RecordFailure();
        Assert.True(throttle.IsBlocked(out _));

        throttle.Reset();
        Assert.False(throttle.IsBlocked(out _));
    }
}
