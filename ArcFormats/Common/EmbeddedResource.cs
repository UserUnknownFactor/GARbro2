using System;
using System.IO;

namespace GameRes.Formats
{
    public static class EmbeddedResource
    {
        /// <summary>
        /// Open embedded resource as a stream.
        /// </summary>
        public static Stream Open (string name, Type owner)
        {
            var assembly = owner.Assembly;
            string qualified_name = owner.Namespace + '.' + name;
            return assembly.GetManifestResourceStream (qualified_name);
        }

        /// <summary>
        /// Load binary embedded resource as a byte array.
        /// </summary>
        public static byte[] Load (string name, Type owner)
        {
            using (var stream = Open (name, owner))
            {
                if (null == stream)
                    return null;
                var res = new byte[stream.Length];
                stream.Read (res, 0, res.Length);
                return res;
            }
        }
    }
}
