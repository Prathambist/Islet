// Makes the key pair that signs Islet's releases.
//
//   dotnet run tools/new-release-key.cs -- <where to save the private key>
//
// The private key goes to the file you name; keep it out of the repository, back it up, and
// add its contents as the RELEASE_SIGNING_KEY secret on GitHub. The public key is printed:
// it belongs in ReleaseSignature.cs and install.ps1, so new builds can check releases with it.
// Making a new pair means copies built with the old public key can no longer update, so only
// do it if the private key is lost or leaked.

using System.Security.Cryptography;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run tools/new-release-key.cs -- <private key file>");
    return 1;
}

string path = Path.GetFullPath(args[0]);

if (File.Exists(path))
{
    Console.Error.WriteLine($"{path} already exists; not overwriting a signing key.");
    return 1;
}

using var rsa = RSA.Create(3072);

Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());

Console.WriteLine($"Private key saved to {path}");
Console.WriteLine();
Console.WriteLine("Public key:");
Console.WriteLine(rsa.ToXmlString(includePrivateParameters: false));
return 0;
