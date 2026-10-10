using AIVES.DAL;
using AIVES.DAL.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AIVES.Tests;

public sealed class DataProtectionTests : IDisposable
{
    private readonly string certificatePath = Path.Combine(Path.GetTempPath(), $"aives-dp-{Guid.NewGuid():N}.pfx");

    [Fact]
    public void KeysAreStoredInTheDatabaseInPlainFormWithoutACertificate()
    {
        using var services = BuildServices(new Dictionary<string, string?>());
        var xml = ProtectOnceAndReadStoredKey(services);

        Assert.Contains("<masterKey", xml);
        Assert.DoesNotContain("<encryptedSecret", xml);
    }

    [Fact]
    public void ConfiguredCertificateEncryptsStoredKeysAndStillRoundTrips()
    {
        const string password = "test-certificate-password";
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=AIVES Data Protection Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, password));
        }

        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["DataProtection:CertificatePath"] = certificatePath,
            ["DataProtection:CertificatePassword"] = password
        });
        var xml = ProtectOnceAndReadStoredKey(services);

        Assert.Contains("<encryptedSecret", xml);
        Assert.DoesNotContain("<masterKey", xml);
    }

    [Fact]
    public void MissingCertificateFileFailsAtStartup()
    {
        Assert.ThrowsAny<CryptographicException>(() => BuildServices(new Dictionary<string, string?>
        {
            ["DataProtection:CertificatePath"] = certificatePath,
            ["DataProtection:CertificatePassword"] = "unused"
        }));
    }

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddDataAccess(configuration, _ => { });
        var databaseName = Guid.NewGuid().ToString();
        services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    /// <summary>Protects a value (creating the first key), checks it round-trips and returns the stored key XML.</summary>
    private static string ProtectOnceAndReadStoredKey(ServiceProvider services)
    {
        var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
        Assert.Equal("secret", protector.Unprotect(protector.Protect("secret")));

        using var scope = services.CreateScope();
        return Assert.Single(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().DataProtectionKeys).Xml!;
    }

    public void Dispose()
    {
        if (File.Exists(certificatePath))
            File.Delete(certificatePath);
    }
}
