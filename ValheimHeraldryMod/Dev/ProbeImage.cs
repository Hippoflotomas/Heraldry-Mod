using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ValheimHeraldryMod.Dev
{
    /// <summary>
    ///     PNG saving for the dev probe. Like ShieldShare's TextureIO, this never names UnityEngine.ImageConversion
    ///     in code: that module is built against netstandard 2.1 in Unity 6, and a net48 project calling it
    ///     directly fails with CS1705. EncodeToPNG is looked up by reflection instead.
    /// </summary>
    internal static class ProbeImage
    {
        private static MethodInfo encodeToPng;

        public static void Save(Color32[] pixels, int width, int height, string path)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(pixels);
                tex.Apply(false);
                File.WriteAllBytes(path, Encode(tex));
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        private static byte[] Encode(Texture2D tex)
        {
            if (encodeToPng == null)
            {
                var type = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", true);
                encodeToPng = type.GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Texture2D) }, null)
                              ?? throw new MissingMethodException("UnityEngine.ImageConversion", "EncodeToPNG");
            }
            return (byte[])encodeToPng.Invoke(null, new object[] { tex });
        }
    }
}
