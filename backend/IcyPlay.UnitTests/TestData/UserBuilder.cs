using IcyPlay.Domain.Identity;

namespace IcyPlay.UnitTests.TestData;

public sealed class UserBuilder
{
    private string _email = "customer@example.com";
    private string _fullName = "Juan Dela Cruz";
    private string? _phoneNumber = "09123456789";
    private string? _passwordHash;

    public UserBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    public UserBuilder WithFullName(string fullName)
    {
        _fullName = fullName;
        return this;
    }

    public UserBuilder WithPhoneNumber(string? phoneNumber)
    {
        _phoneNumber = phoneNumber;
        return this;
    }

    public UserBuilder WithPasswordHash(string passwordHash)
    {
        _passwordHash = passwordHash;
        return this;
    }

    public User Build()
    {
        var user = new User(_email, _fullName, _phoneNumber);
        if (_passwordHash is not null)
        {
            user.SetPasswordHash(_passwordHash);
        }

        return user;
    }
}

