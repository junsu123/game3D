using System;
using System.IO;

namespace Mylti.Domain
{
    /// <summary>Minimal Radiance .hdr (RGBE) reader: flat and new-style run-length encoded scanlines.</summary>
    public static class RadianceHdr
    {
        public sealed class Image { public int Width, Height; public float[] Rgb; }

        public static Image Read(string path) => Decode(File.ReadAllBytes(path));

        public static Image Decode(byte[] d)
        {
            int p = 0;
            string Line()
            {
                int s = p;
                while (p < d.Length && d[p] != '\n') p++;
                string l = System.Text.Encoding.ASCII.GetString(d, s, p - s);
                p++;
                return l;
            }
            string first = Line();
            if (!first.StartsWith("#?", StringComparison.Ordinal)) throw new InvalidDataException("not a Radiance HDR file");
            while (p < d.Length && Line().Length > 0) { }
            var res = Line().Split(' ');
            if (res.Length < 4 || res[0] != "-Y" || res[2] != "+X") throw new InvalidDataException("unsupported orientation " + string.Join(" ", res));
            int h = int.Parse(res[1]), w = int.Parse(res[3]);
            var img = new Image { Width = w, Height = h, Rgb = new float[w * h * 3] };
            var scan = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                if (w >= 8 && w < 32768 && p + 4 <= d.Length && d[p] == 2 && d[p + 1] == 2 && (d[p + 2] << 8 | d[p + 3]) == w)
                {
                    p += 4;
                    for (int c = 0; c < 4; c++)
                    {
                        int x = 0;
                        while (x < w)
                        {
                            int count = d[p++];
                            if (count > 128)
                            {
                                count -= 128;
                                byte v = d[p++];
                                for (int k = 0; k < count && x < w; k++) scan[(x++) * 4 + c] = v;
                            }
                            else
                                for (int k = 0; k < count && x < w; k++) scan[(x++) * 4 + c] = d[p++];
                        }
                    }
                }
                else
                {
                    Buffer.BlockCopy(d, p, scan, 0, w * 4);
                    p += w * 4;
                }
                for (int x = 0; x < w; x++)
                {
                    int e = scan[x * 4 + 3];
                    float f = e == 0 ? 0f : (float)Math.Pow(2, e - 136); // ldexp(1, e - (128 + 8))
                    int o = (y * w + x) * 3;
                    img.Rgb[o] = scan[x * 4] * f;
                    img.Rgb[o + 1] = scan[x * 4 + 1] * f;
                    img.Rgb[o + 2] = scan[x * 4 + 2] * f;
                }
            }
            return img;
        }
    }
}
