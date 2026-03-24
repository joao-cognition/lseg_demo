using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MarketDataHub.Utils;

namespace MarketDataHub.Tests
{
    [TestClass]
    public class CryptoHelperTests
    {
        [TestMethod]
        public void HashPassword_ShouldReturnConsistentHash()
        {
            string password = "testPassword123";
            string hash1 = CryptoHelper.HashPassword(password);
            string hash2 = CryptoHelper.HashPassword(password);
            Assert.AreEqual(hash1, hash2);
        }

        [TestMethod]
        public void HashPassword_DifferentPasswords_ShouldReturnDifferentHashes()
        {
            string hash1 = CryptoHelper.HashPassword("password1");
            string hash2 = CryptoHelper.HashPassword("password2");
            Assert.AreNotEqual(hash1, hash2);
        }

        [TestMethod]
        public void ValidatePassword_CorrectPassword_ShouldReturnTrue()
        {
            string password = "mySecurePass";
            string hash = CryptoHelper.HashPassword(password);
            Assert.IsTrue(CryptoHelper.ValidatePassword(password, hash));
        }

        [TestMethod]
        public void ValidatePassword_WrongPassword_ShouldReturnFalse()
        {
            string hash = CryptoHelper.HashPassword("correctPassword");
            Assert.IsFalse(CryptoHelper.ValidatePassword("wrongPassword", hash));
        }

        [TestMethod]
        public void GenerateApiKey_ShouldStartWithMDHPrefix()
        {
            string key = CryptoHelper.GenerateApiKey("testuser");
            Assert.IsTrue(key.StartsWith("MDH-"));
        }

        [TestMethod]
        public void ValidateApiKey_ValidKey_ShouldReturnUsername()
        {
            string username = "stuart.m";
            string key = CryptoHelper.GenerateApiKey(username);
            string result = CryptoHelper.ValidateApiKey(key);
            Assert.AreEqual(username, result);
        }

        [TestMethod]
        public void ValidateApiKey_InvalidKey_ShouldReturnNull()
        {
            string result = CryptoHelper.ValidateApiKey("MDH-invalid-key");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GenerateTempPassword_ShouldReturn8Characters()
        {
            string password = CryptoHelper.GenerateTempPassword();
            Assert.AreEqual(8, password.Length);
        }
    }
}
