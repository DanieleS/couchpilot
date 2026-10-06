using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using static CouchPilot.Lang;

namespace CouchPilot.Server
{
    // The .pfx that turns the server to HTTPS. Errors come back in words the settings can show.
    public static class ServerCertificate
    {
        public static X509Certificate2 Load(string path, string password)
        {
            if (!File.Exists(path)) throw new InvalidOperationException(Pc($"The certificate {path} doesn't exist.", $"Il certificato {path} non esiste."));

            X509Certificate2 certificate;
            try
            {
                // Not EphemeralKeySet: SChannel can't use a key that never touched the disk.
                certificate = new X509Certificate2(path, password ?? "", X509KeyStorageFlags.UserKeySet);
            }
            catch (CryptographicException e)
            {
                throw new InvalidOperationException(Pc($"Couldn't open the certificate (wrong password or unsupported format): {e.Message}", $"Impossibile aprire il certificato (password sbagliata o formato non supportato): {e.Message}"), e);
            }

            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new InvalidOperationException(Pc("The certificate has no private key: export the .pfx together with its key.", "Il certificato non contiene la chiave privata: esporta il .pfx insieme alla chiave."));
            }
            return certificate;
        }

        // The name to put in the phone's address bar: the first DNS name in the certificate,
        // or null when it only names IP addresses.
        public static string HostName(X509Certificate2 certificate)
        {
            var name = certificate.GetNameInfo(X509NameType.DnsName, false);
            return string.IsNullOrWhiteSpace(name) || IPAddress.TryParse(name, out _) ? null : name;
        }

        public static bool IsExpired(X509Certificate2 certificate) => DateTime.Now > certificate.NotAfter;
    }
}
