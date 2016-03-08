using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nop.Core.Configuration;
using Nop.Web.Framework;

namespace Nop.Plugin.Payments.PayPoint
{
    public class PayPointHelper
    {
        #region Methods
        public static string CalcRequestSign(RemotePost post, string remotePassword)
        {
            return CalcMD5Hash(String.Format("{0}{1}{2}", post.Params["trans_id"], post.Params["amount"], remotePassword));
        }

        public static bool ValidateResponseSign(Uri requestUrl, string digestKey)
        {
            string paq = requestUrl.PathAndQuery;
            Match m = Regex.Match(requestUrl.PathAndQuery, @"^.*\&hash=(?<hash>.*)$");
            if (!m.Success)
            {
                return false;
            }
            string hash = m.Groups["hash"].Value;
            paq = Regex.Replace(paq, @"hash=.*$", String.Empty);
            string hash2 = CalcMD5Hash(paq + digestKey);

            return hash.Equals(hash2);
        }
        #endregion

        #region Utilities
        private static string CalcMD5Hash(string s)
        {
            using (var cs = MD5.Create())
            {
                var sb = new StringBuilder();
                byte[] hash;

                hash = cs.ComputeHash(Encoding.UTF8.GetBytes(s));
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }
        #endregion
    }
}
