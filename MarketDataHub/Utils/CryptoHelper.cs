using System;
using System.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace MarketDataHub.Utils
{
    /// <summary>
    /// Cryptographic helper for password hashing and API key generation.
    /// Created: 2016 by Stuart M.
    /// Updated: Password hashing migrated from MD5 to PBKDF2. Legacy MD5
    /// verification retained for backward compatibility during password migration.
    /// </summary>
    public class CryptoHelper
    {
        private static readonly string API_SECRET = string.IsNullOrEmpty(ConfigurationManager.AppSettings["ApiSecret"]) ? "CHANGE-ME" : ConfigurationManager.AppSettings["ApiSecret"];

        private const int PBKDF2_ITERATIONS = 100000;
        private const int SALT_SIZE = 16;
        private const int HASH_SIZE = 32;
        private const string PBKDF2_PREFIX = "PBKDF2$";

        /// <summary>
        /// Hash a password using PBKDF2 with a random salt.
        /// Returns a string in the format: PBKDF2${iterations}${base64salt}${base64hash}
        /// </summary>
        public static string HashPassword(string password)
        {
            byte[] salt = new byte[SALT_SIZE];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, PBKDF2_ITERATIONS, HashAlgorithmName.SHA256))
            {
                byte[] hash = pbkdf2.GetBytes(HASH_SIZE);
                return PBKDF2_PREFIX + PBKDF2_ITERATIONS + "$" +
                       Convert.ToBase64String(salt) + "$" +
                       Convert.ToBase64String(hash);
            }
        }

        /// <summary>
        /// Validate a password against a stored hash.
        /// Supports both PBKDF2 (new) and MD5 (legacy) hashes.
        /// </summary>
        public static bool ValidatePassword(string password, string storedHash)
        {
            if (storedHash.StartsWith(PBKDF2_PREFIX))
            {
                return ValidatePbkdf2(password, storedHash);
            }

            // Legacy MD5 hash (32-char hex string) — needed during migration
            return ValidateMd5Legacy(password, storedHash);
        }

        /// <summary>
        /// Check whether a stored hash is using the legacy MD5 format
        /// and should be re-hashed on next successful login.
        /// </summary>
        public static bool IsLegacyHash(string storedHash)
        {
            return !storedHash.StartsWith(PBKDF2_PREFIX);
        }

        private static bool ValidatePbkdf2(string password, string storedHash)
        {
            string[] parts = storedHash.Substring(PBKDF2_PREFIX.Length).Split('$');
            if (parts.Length != 3) return false;

            int iterations = int.Parse(parts[0]);
            byte[] salt = Convert.FromBase64String(parts[1]);
            byte[] expectedHash = Convert.FromBase64String(parts[2]);

            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
            {
                byte[] actualHash = pbkdf2.GetBytes(expectedHash.Length);
                return CryptographicEquals(expectedHash, actualHash);
            }
        }

        private static bool ValidateMd5Legacy(string password, string storedHash)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(password);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("X2"));
                }
                return sb.ToString() == storedHash;
            }
        }

        /// <summary>
        /// Constant-time comparison to prevent timing attacks.
        /// </summary>
        private static bool CryptographicEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }

        /// <summary>
        /// Generate an API key for downstream system access.
        /// Format: MDH-{base64(username:timestamp:hmac)}
        /// Keys don't expire - revocation is done by deactivating the user record.
        /// </summary>
        public static string GenerateApiKey(string username)
        {
            string timestamp = DateTime.UtcNow.Ticks.ToString();
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(API_SECRET)))
            {
                byte[] signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(username + timestamp));
                string signature = Convert.ToBase64String(signatureBytes);
                string keyData = username + ":" + timestamp + ":" + signature;
                return "MDH-" + Convert.ToBase64String(Encoding.UTF8.GetBytes(keyData));
            }
        }

        /// <summary>
        /// Validate an API key and return the username if valid.
        /// </summary>
        public static string ValidateApiKey(string apiKey)
        {
            try
            {
                if (!apiKey.StartsWith("MDH-")) return null;

                string encoded = apiKey.Substring(4);
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                string[] parts = decoded.Split(':');
                if (parts.Length == 3)
                {
                    string username = parts[0];
                    string timestamp = parts[1];
                    string signature = parts[2];

                    using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(API_SECRET)))
                    {
                        byte[] expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(username + timestamp));
                        string expectedSig = Convert.ToBase64String(expectedBytes);
                        if (signature == expectedSig)
                        {
                            return username;
                        }
                    }
                }
            }
            catch
            {
                // Invalid key format
            }
            return null;
        }

        /// <summary>
        /// Generate a cryptographically secure temporary password for new users / password resets.
        /// </summary>
        public static string GenerateTempPassword()
        {
            string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%";
            int maxUnbiased = 256 - (256 % chars.Length); // reject values >= this threshold
            char[] password = new char[16];
            int filled = 0;
            using (var rng = RandomNumberGenerator.Create())
            {
                while (filled < password.Length)
                {
                    byte[] buf = new byte[password.Length - filled + 4]; // slight overallocation
                    rng.GetBytes(buf);
                    for (int j = 0; j < buf.Length && filled < password.Length; j++)
                    {
                        if (buf[j] < maxUnbiased)
                        {
                            password[filled++] = chars[buf[j] % chars.Length];
                        }
                    }
                }
            }
            return new string(password);
        }
    }
}
