using System;
using System.Security.Cryptography;
using System.Text;

namespace Raccoon.Save
{
    /// <summary>
    /// Encrypted file layout: magic "RSV\x01" | IV (16) | AES-256-CBC ciphertext | HMAC-SHA256 (32) over everything before it.
    /// AES and HMAC keys are both derived from the user key, so one string configures everything.
    /// </summary>
    internal static class SaveCrypto
    {
        private static readonly byte[] Magic = { (byte)'R', (byte)'S', (byte)'V', 1 };
        private const int IvSize = 16;
        private const int MacSize = 32;

        public static bool HasMagic(byte[] data)
        {
            if (data == null || data.Length < Magic.Length) return false;
            for (int i = 0; i < Magic.Length; i++)
                if (data[i] != Magic[i]) return false;
            return true;
        }

        public static byte[] Encrypt(string plainText, string key)
        {
            DeriveKeys(key, out byte[] aesKey, out byte[] macKey);
            byte[] plain = Encoding.UTF8.GetBytes(plainText);

            byte[] iv, cipher;
            using (Aes aes = Aes.Create())
            {
                aes.Key = aesKey;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateIV();
                iv = aes.IV;
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                    cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
            }

            byte[] result = new byte[Magic.Length + IvSize + cipher.Length + MacSize];
            Buffer.BlockCopy(Magic, 0, result, 0, Magic.Length);
            Buffer.BlockCopy(iv, 0, result, Magic.Length, IvSize);
            Buffer.BlockCopy(cipher, 0, result, Magic.Length + IvSize, cipher.Length);

            int macOffset = result.Length - MacSize;
            using (HMACSHA256 hmac = new HMACSHA256(macKey))
            {
                byte[] mac = hmac.ComputeHash(result, 0, macOffset);
                Buffer.BlockCopy(mac, 0, result, macOffset, MacSize);
            }
            return result;
        }

        //False when the data was tampered with, truncated, or encrypted with another key
        public static bool TryDecrypt(byte[] data, string key, out string plainText)
        {
            plainText = null;
            if (!HasMagic(data) || data.Length < Magic.Length + IvSize + 16 + MacSize) return false;

            DeriveKeys(key, out byte[] aesKey, out byte[] macKey);
            int macOffset = data.Length - MacSize;
            using (HMACSHA256 hmac = new HMACSHA256(macKey))
            {
                byte[] expected = hmac.ComputeHash(data, 0, macOffset);
                int diff = 0;
                for (int i = 0; i < MacSize; i++) diff |= expected[i] ^ data[macOffset + i];
                if (diff != 0) return false;
            }

            try
            {
                byte[] iv = new byte[IvSize];
                Buffer.BlockCopy(data, Magic.Length, iv, 0, IvSize);
                int cipherOffset = Magic.Length + IvSize;
                using (Aes aes = Aes.Create())
                {
                    aes.Key = aesKey;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    {
                        byte[] plain = decryptor.TransformFinalBlock(data, cipherOffset, macOffset - cipherOffset);
                        plainText = Encoding.UTF8.GetString(plain);
                    }
                }
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private static void DeriveKeys(string key, out byte[] aesKey, out byte[] macKey)
        {
            using (HMACSHA256 kdf = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
            {
                aesKey = kdf.ComputeHash(Encoding.UTF8.GetBytes("raccoon.save.aes"));
                macKey = kdf.ComputeHash(Encoding.UTF8.GetBytes("raccoon.save.mac"));
            }
        }
    }
}
