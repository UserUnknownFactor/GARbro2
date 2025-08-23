using System;
using System.Collections.Generic;
using GameRes.Cryptography;

namespace GameRes.Formats.Malie
{
    [Serializable]
    public abstract class LibScheme
    {
        uint     DataAlign;

        public LibScheme (uint align)
        {
            DataAlign = align;
        }

        public abstract IMalieDecryptor CreateDecryptor ();

        public virtual long GetAlignedOffset (long offset)
        {
            long align = DataAlign - 1;
            return (offset + align) & ~align;
        }
    }

    [Serializable]
    public class LibCamelliaScheme : LibScheme
    {
        public uint[] Key { get; set; }

        public LibCamelliaScheme (uint[] key) : this (0x1000, key)
        {
        }

        public LibCamelliaScheme (uint align, uint[] key) : base (align)
        {
            Key = key;
        }

        public LibCamelliaScheme (string key) : this (Camellia.GenerateKey (key))
        {
        }

        public LibCamelliaScheme (uint align, string key) : this (align, Camellia.GenerateKey (key))
        {
        }

        public override IMalieDecryptor CreateDecryptor ()
        {
            return new CamelliaDecryptor (Key);
        }
    }

    [Serializable]
    public class LibCfiScheme : LibScheme
    {
        public byte[] Key { get; set; }
        public uint[] RotateKey { get; set; }

        public LibCfiScheme (uint align, byte[] key, uint[] rot_key) : base (align)
        {
            Key = key;
            RotateKey = rot_key;
        }

        public override IMalieDecryptor CreateDecryptor ()
        {
            return new CfiDecryptor (Key, RotateKey);
        }
    }

    [Serializable]
    public class MalieScheme : ResourceScheme
    {
        public Dictionary<string, LibScheme> KnownSchemes;
    }
}
