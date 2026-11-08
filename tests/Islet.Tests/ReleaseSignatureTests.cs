using System.IO;
using System.Security.Cryptography;

namespace Islet.Tests
{
    public class ReleaseSignatureTests
    {
        // A stand-in key pair, signed the same way the release workflow signs
        static (string PublicKey, string Signature) Sign(byte[] data)
        {
            using var rsa = RSA.Create(3072);
            var signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return (rsa.ToXmlString(false), Convert.ToBase64String(signature));
        }

        static readonly byte[] Zip = "pretend this is Islet-win-x64.zip"u8.ToArray();

        [Fact]
        public void AcceptsWhatTheKeySigned()
        {
            var (key, signature) = Sign(Zip);

            Assert.True(ReleaseSignature.Verify(Zip, signature, key));
            Assert.True(ReleaseSignature.Verify(Zip, signature + "\r\n", key));
        }

        [Fact]
        public void RejectsATamperedRelease()
        {
            var (key, signature) = Sign(Zip);
            var tampered = (byte[])Zip.Clone();
            tampered[0] ^= 1;

            Assert.False(ReleaseSignature.Verify(tampered, signature, key));
        }

        [Fact]
        public void RejectsAnotherKeysSignature()
        {
            var (key, _) = Sign(Zip);
            var (_, otherSignature) = Sign(Zip);

            Assert.False(ReleaseSignature.Verify(Zip, otherSignature, key));
        }

        [Fact]
        public void RejectsNonsenseWithoutThrowing()
        {
            Assert.False(ReleaseSignature.Verify(Zip, "not base64!"));
            Assert.False(ReleaseSignature.Verify(Zip, Convert.ToBase64String(new byte[384])));
        }

        [Fact]
        public void TheInstallerCarriesTheSamePublicKey()
        {
            string installer = Path.Combine(RepositoryRoot(), "install.ps1");

            Assert.Contains(ReleaseSignature.PublicKey, File.ReadAllText(installer));
        }

        // Found from this file's own location, wherever the tests were built to
        static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
    }
}
