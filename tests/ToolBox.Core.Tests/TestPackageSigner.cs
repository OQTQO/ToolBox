using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace ToolBox.Core.Tests;

internal sealed class TestPackageSigner : IDisposable
{
    internal static TestPackageSigner Shared { get; } = new();

    private readonly RSA _rsa;
    private readonly X509Certificate2 _certificate;

    internal TestPackageSigner()
    {
        _rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=ToolBox Test Publisher",
            _rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        _certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30));
    }

    internal byte[] CreateSignature(byte[] packageMetadata, string publisherId)
    {
        var signature = _rsa.SignData(
            packageMetadata,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            publisherId,
            algorithm = "rsa-sha256",
            payload = "package.json",
            certificate = Convert.ToBase64String(_certificate.Export(X509ContentType.Cert)),
            signature = Convert.ToBase64String(signature)
        }));
    }

    public void Dispose()
    {
        _certificate.Dispose();
        _rsa.Dispose();
    }
}
