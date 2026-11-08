using System.Security.Cryptography;

namespace Islet
{
    /// <summary>
    /// Checks that an update was signed with Islet's release key before it's installed.
    ///
    /// HTTPS only protects the download on its way; a signature also protects against a
    /// release that was tampered with on GitHub itself. The private key lives only in the
    /// RELEASE_SIGNING_KEY secret the release workflow signs with (see docs/RELEASING.md).
    /// </summary>
    public static class ReleaseSignature
    {
        /// <summary>The public half of the release key. install.ps1 carries the same one.</summary>
        public const string PublicKey =
            "<RSAKeyValue><Modulus>pNGdcQG3b4/+tGyz8nOOuKAg5bt235ti/0gbMawJifyY9cAMgk66W4/OnbUbXT9wc/2Gj7i+l9L1stssQHLdjWrfTC+H9luGw8QBkjTUkbbbs4WwixZi3Uihdi4F9RYk3I4pexhGh4m7drgZgP6COcrnPca66cc4HPbGigoNKCUHl7cdoDg+cy0oZyWV1ikJeBI1fqmA13de0rxS2kp8EkQw7SM/LSuJE1qXVy9M0aO5z3quChpasZurkoDM/tPThDJlZtQAVtXuUP4uqXMOu4kJa9KAdxvcNu1B+hEpuSYLDYN3/7d8pKA3Ux/hsIwP9y0CqHMOGpahGoehvvJqPEGgJm9NjqqvWi8d21j0ARqnUhQ+53OAgqxOdmrXmCCel7u5/Hf90vSAVDSGzhovoKnZRlcbHkHZVs6Nb6ql1CyU2Rux9ZOWHvi1lmE+zGJPr8dm9T1NzECIe7pp2lCOeePwWglghT0pwl1qZvd7HAf6eDA6x02DsRqmHxLyAuhJ</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public static bool Verify(byte[] data, string signatureBase64) => Verify(data, signatureBase64, PublicKey);

        /// <summary>An RSA SHA-256 signature, as the release workflow writes it: Base64, in a .sig file.</summary>
        public static bool Verify(byte[] data, string signatureBase64, string publicKeyXml)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.FromXmlString(publicKeyXml);

                return rsa.VerifyData(data, Convert.FromBase64String(signatureBase64.Trim()),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }
}
