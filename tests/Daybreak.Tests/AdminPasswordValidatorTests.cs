using Daybreak.Security;

namespace Daybreak.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AdminPasswordValidatorTests
{
    [TestInitialize]
    public void Initialize()
    {
        Environment.SetEnvironmentVariable("DAYBREAK_ADMIN_PASSWORD", null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("DAYBREAK_ADMIN_PASSWORD", null);
    }

    [TestMethod]
    public void DirectEnvironmentValueIsAccepted()
    {
        Environment.SetEnvironmentVariable("DAYBREAK_ADMIN_PASSWORD", "simple");

        var validator = new AdminPasswordValidator();

        Assert.IsTrue(validator.IsValid("simple"));
        Assert.IsFalse(validator.IsValid("different"));
    }

    [TestMethod]
    public void PasswordChangeInvalidatesPrincipalFromPreviousProcessConfiguration()
    {
        Environment.SetEnvironmentVariable("DAYBREAK_ADMIN_PASSWORD", "first-password-long-enough");
        var original = new AdminPasswordValidator();
        var principal = original.CreatePrincipal();
        Assert.IsTrue(original.IsCurrent(principal));

        Environment.SetEnvironmentVariable("DAYBREAK_ADMIN_PASSWORD", "second-password-long-enough");
        var restarted = new AdminPasswordValidator();

        Assert.IsFalse(restarted.IsCurrent(principal));
    }

    [TestMethod]
    public void MissingPasswordIsRejected()
    {
        var missing = Assert.Throws<InvalidOperationException>(() => new AdminPasswordValidator());
        StringAssert.Contains(missing.Message, "Set DAYBREAK_ADMIN_PASSWORD");
    }
}
