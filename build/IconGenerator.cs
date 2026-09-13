using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconGenerator
{
    private static GraphicsPath RoundedRectangle(RectangleF box, float radius)
    {
        float diameter = radius * 2F;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
        path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
        path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static byte[] Render(int size)
    {
        using (Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float inset = Math.Max(1F, size * 0.035F);
            RectangleF box = new RectangleF(inset, inset, size - inset * 2F, size - inset * 2F);
            using (GraphicsPath path = RoundedRectangle(box, size * 0.22F))
            using (LinearGradientBrush gradient = new LinearGradientBrush(box, Color.FromArgb(42, 105, 255), Color.FromArgb(0, 185, 158), 38F))
                graphics.FillPath(gradient, path);

            float penWidth = Math.Max(1.5F, size * 0.075F);
            using (Pen pen = new Pen(Color.White, penWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawLines(pen, new PointF[]
                {
                    new PointF(size * 0.40F, size * 0.29F),
                    new PointF(size * 0.24F, size * 0.50F),
                    new PointF(size * 0.40F, size * 0.71F)
                });
                graphics.DrawLines(pen, new PointF[]
                {
                    new PointF(size * 0.60F, size * 0.29F),
                    new PointF(size * 0.76F, size * 0.50F),
                    new PointF(size * 0.60F, size * 0.71F)
                });
            }

            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }
    }

    public static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        List<byte[]> images = new List<byte[]>();
        foreach (int size in sizes) images.Add(Render(size));

        using (FileStream file = File.Create(args[0]))
        using (BinaryWriter writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)images[i].Length);
                writer.Write((uint)offset);
                offset += images[i].Length;
            }
            foreach (byte[] image in images) writer.Write(image);
        }
        return 0;
    }
}
