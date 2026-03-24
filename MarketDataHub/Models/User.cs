using System;

namespace MarketDataHub.Models
{
    public class User
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }      // MD5 hash
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Department { get; set; }         // Trading, Risk, Compliance, IT, Operations
        public string Role { get; set; }               // Admin, DataManager, Analyst, ReadOnly
        public int IsActive { get; set; }
        public DateTime LastLoginDate { get; set; }
        public int FailedLoginAttempts { get; set; }
        public DateTime CreatedDate { get; set; }
        public string ApiKey { get; set; }             // For downstream system access
    }
}
