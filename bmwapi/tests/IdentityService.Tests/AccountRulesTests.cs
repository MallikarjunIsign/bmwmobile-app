using IdentityService.Services;
using Microsoft.AspNetCore.DataProtection;

namespace IdentityService.Tests;

public class UsernameGeneratorTests
{
    [Theory]
    [InlineData("Mallikarjun Reddy", "mallikarjun")]
    [InlineData("  mallikarjun  ", "mallikarjun")]
    [InlineData("O'Brien Smith", "obrien")]
    [InlineData("José Álvarez", "jose")]
    [InlineData("Chidi-Obi Okafor", "chidiobi")]
    [InlineData("R2D2 Droid", "r2d2")]
    [InlineData("李 Wei", "user")]
    [InlineData("", "user")]
    public void Base_is_first_name_lowercase_letters_and_numbers(string fullName, string expected) =>
        Assert.Equal(expected, UsernameGenerator.BaseFromFullName(fullName));

    [Fact]
    public void Base_is_capped_at_20_characters() =>
        Assert.Equal(20, UsernameGenerator.BaseFromFullName("Abcdefghijklmnopqrstuvwxyz Smith").Length);

    [Fact]
    public void Free_name_is_used_as_is() =>
        Assert.Equal("mallikarjun", UsernameGenerator.FirstFree("mallikarjun", ["mallikarjunreddy"]));

    [Fact]
    public void Taken_name_gets_the_next_number() =>
        Assert.Equal("mallikarjun3", UsernameGenerator.FirstFree("mallikarjun", ["Mallikarjun", "mallikarjun2"]));

    [Fact]
    public void Gaps_are_filled_first() =>
        Assert.Equal("alice2", UsernameGenerator.FirstFree("alice", ["alice", "alice3"]));
}

public class PasswordRulesTests
{
    [Theory]
    [InlineData("Passw0rd!")]
    [InlineData("Abcdefg1#")]
    public void Strong_passwords_pass(string password) => Assert.Null(PasswordRules.Validate(password));

    [Theory]
    [InlineData("", "at least 8")]
    [InlineData("Pa1!", "at least 8")]
    [InlineData("password1!", "uppercase")]
    [InlineData("PASSWORD1!", "lowercase")]
    [InlineData("Password!!", "number")]
    [InlineData("Password11", "special")]
    public void Weak_passwords_explain_what_is_missing(string password, string expected) =>
        Assert.Contains(expected, PasswordRules.Validate(password));
}

public class MaskEmailTests
{
    [Theory]
    [InlineData("mallikarjun@example.com", "m*****n@example.com")]
    [InlineData("ab@example.com", "a*b@example.com")]
    [InlineData("a@example.com", "a@example.com")]
    public void Hides_most_of_the_name(string email, string expected) =>
        Assert.Equal(expected, AccountService.MaskEmail(email));
}

public class LoginChallengesTests
{
    private readonly LoginChallenges _challenges = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Round_trips_the_user_id()
    {
        var challenge = _challenges.Create("user-123");
        Assert.NotEqual("user-123", challenge);
        Assert.Equal("user-123", _challenges.Read(challenge));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-challenge")]
    public void Invalid_challenges_are_rejected(string? challenge) => Assert.Null(_challenges.Read(challenge));

    [Fact]
    public void Tampered_challenge_is_rejected()
    {
        var challenge = _challenges.Create("user-123");
        var tampered = challenge[..^2] + (challenge[^2] == 'A' ? "B" : "A") + challenge[^1];
        Assert.Null(_challenges.Read(tampered));
    }
}
