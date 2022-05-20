﻿using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DirectPackageInstaller
{
    public static class TempHelper
    {
        static string GetHash(string Content) {
            SHA256 Hasher = SHA256.Create();
            var Hash = Hasher.ComputeHash(Encoding.UTF8.GetBytes(Content));
            return string.Join("", Hash.Select(x => x.ToString("X2")));
        }

        static Random random = new Random();
        public static string TempDir => Path.Combine(App.WorkingDirectory, "Temp");
        public static string GetTempFile(string? ID)
        {
            if (ID == null)
                ID = random.Next().ToString() + random.Next().ToString() + random.Next().ToString();
            
            if (!Directory.Exists(TempDir))
                Directory.CreateDirectory(TempDir);

            return Path.Combine(TempDir, GetHash(ID) + ".tmp");
        }

        public static void Clear()
        {
            if (!Directory.Exists(TempDir))
                return;

            foreach (var Filepath in Directory.GetFiles(TempDir, "*.*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(Filepath);
                }
                catch { }
            }
        }
    }
}