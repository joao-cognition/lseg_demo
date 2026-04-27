using System;
using System.Data;
using System.DirectoryServices;
using System.Web.Mvc;
using MarketDataHub.Data;
using MarketDataHub.Utils;

namespace MarketDataHub.Controllers
{
    /// <summary>
    /// Authentication controller. Supports LDAP (Active Directory) with local DB fallback.
    /// </summary>
    public class AuthController : Controller
    {
        // GET: /Auth/Login
        public ActionResult Login()
        {
            if (Session["Username"] != null)
                return RedirectToAction("Index", "Dashboard");
            return View();
        }

        // POST: /Auth/Login
        [HttpPost]
        public ActionResult Login(string username, string password, string rememberMe)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Username and password are required.";
                return View();
            }

            MvcApplication.WriteLog("Login attempt: user=" + username + " IP=" + Request.UserHostAddress);

            bool authenticated = false;
            string role = "ReadOnly";
            string fullName = username;

            // Try LDAP first
            try
            {
                authenticated = AuthenticateViaLdap(username, password);
                if (authenticated)
                {
                    MvcApplication.WriteLog("LDAP auth successful for " + username);
                    DataTable user = DatabaseHelper.GetUserByUsername(username);
                    if (user.Rows.Count > 0)
                    {
                        role = user.Rows[0]["Role"].ToString();
                        fullName = user.Rows[0]["FullName"].ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                MvcApplication.WriteLog("LDAP unavailable, falling back to local auth: " + ex.Message);
            }

            // Fall back to local DB auth
            if (!authenticated)
            {
                DataTable user = DatabaseHelper.GetUserByUsername(username);
                if (user.Rows.Count > 0)
                {
                    string storedHash = user.Rows[0]["PasswordHash"].ToString();
                    int failedAttempts = Convert.ToInt32(user.Rows[0]["FailedLoginAttempts"]);

                    if (failedAttempts >= 10)
                    {
                        ViewBag.Error = "Account is locked. Contact IT support.";
                        MvcApplication.WriteLog("Locked account login attempt: " + username);
                        return View();
                    }

                    if (CryptoHelper.ValidatePassword(password, storedHash))
                    {
                        authenticated = true;
                        role = user.Rows[0]["Role"].ToString();
                        fullName = user.Rows[0]["FullName"].ToString();
                        DatabaseHelper.UpdateLastLogin(username);

                        // Re-hash legacy MD5 passwords to PBKDF2 on successful login
                        if (CryptoHelper.IsLegacyHash(storedHash))
                        {
                            try
                            {
                                string newHash = CryptoHelper.HashPassword(password);
                                DatabaseHelper.UpdatePasswordHash(username, newHash);
                                MvcApplication.WriteLog("Migrated password hash to PBKDF2 for user: " + username);
                            }
                            catch (Exception hashEx)
                            {
                                MvcApplication.WriteLog("Failed to migrate password hash for " + username + ": " + hashEx.Message);
                            }
                        }
                    }
                    else
                    {
                        DatabaseHelper.IncrementFailedLogins(username);
                        MvcApplication.WriteLog("Failed login for " + username + " (attempt " + (failedAttempts + 1) + ")");
                    }
                }
            }

            if (authenticated)
            {
                Session["Username"] = username;
                Session["FullName"] = fullName;
                Session["Role"] = role;
                Session["LoginTime"] = DateTime.Now;

                MvcApplication.WriteLog("User logged in: " + username + " (" + role + ") from " + Request.UserHostAddress);
                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.Error = "Invalid username or password.";
            return View();
        }

        // GET: /Auth/Logout
        public ActionResult Logout()
        {
            string username = Session["Username"]?.ToString();
            Session.Clear();
            Session.Abandon();
            MvcApplication.WriteLog("User logged out: " + username);
            return RedirectToAction("Login");
        }

        private bool AuthenticateViaLdap(string username, string password)
        {
            string ldapPath = ConfigManager.LdapServer + "/" + ConfigManager.LdapBaseDn;
            using (DirectoryEntry entry = new DirectoryEntry(ldapPath, "CORP\\" + username, password))
            {
                using (DirectorySearcher searcher = new DirectorySearcher(entry))
                {
                    searcher.Filter = "(SAMAccountName=" + username + ")";
                    SearchResult result = searcher.FindOne();
                    return result != null;
                }
            }
        }
    }
}
