using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

static class Program
{
    static readonly SKColor BgDark = new(0x0E, 0x12, 0x2A);
    static readonly SKColor BgPurple = new(0x2B, 0x10, 0x55);
    static readonly SKColor Cyan = new(0x22, 0xD3, 0xEE);
    static readonly SKColor Violet = new(0xA8, 0x55, 0xF7);
    static readonly SKColor Pink = new(0xF4, 0x72, 0xB6);

    static int Main()
    {
        string root = Directory.GetCurrentDirectory();
        string assets = Path.Combine(root, "assets");
        string appAssets = Path.Combine(root, "src", "GamingHub.App", "Assets");
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(appAssets);

        // Main square logo (high-res)
        using (var bmp = RenderMark(1024))
            SavePng(bmp, Path.Combine(assets, "logo.png"));
        using (var bmp = RenderMark(1024))
            SavePng(bmp, Path.Combine(appAssets, "logo.png"));

        // Multi-size ICO from the mark
        var icoBytes = BuildIco(new[] { 256, 128, 64, 48, 32, 24, 16 });
        File.WriteAllBytes(Path.Combine(assets, "logo.ico"), icoBytes);
        File.WriteAllBytes(Path.Combine(appAssets, "logo.ico"), icoBytes);

        // Readme banner with wordmark
        using (var bmp = RenderBanner(1400, 460))
            SavePng(bmp, Path.Combine(assets, "banner.png"));

        // Quiet intro chime
        WriteIntroWav(Path.Combine(assets, "intro.wav"));
        WriteIntroWav(Path.Combine(appAssets, "intro.wav"));

        Console.WriteLine("Logo assets written to:");
        Console.WriteLine("  " + assets);
        Console.WriteLine("  " + appAssets);
        return 0;
    }

    static SKBitmap RenderMark(int size)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        float s = size;
        canvas.Clear(SKColors.Transparent);

        // Background rounded square with diagonal gradient
        float pad = s * 0.03f;
        var bgRect = new SKRect(pad, pad, s - pad, s - pad);
        float radius = s * 0.22f;
        using (var bgPaint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(bgRect.Left, bgRect.Top),
                new SKPoint(bgRect.Right, bgRect.Bottom),
                new[] { BgDark, new SKColor(0x1B, 0x10, 0x3A), BgPurple },
                new[] { 0f, 0.55f, 1f },
                SKShaderTileMode.Clamp)
        })
        {
            canvas.DrawRoundRect(bgRect, radius, radius, bgPaint);
        }

        // Soft radial highlight top-left
        using (var glow = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(s * 0.30f, s * 0.24f),
                s * 0.55f,
                new[] { new SKColor(0x38, 0xBD, 0xF8, 0x55), new SKColor(0x38, 0xBD, 0xF8, 0x00) },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp)
        })
        {
            canvas.DrawRoundRect(bgRect, radius, radius, glow);
        }

        float cx = s / 2f, cy = s / 2f;
        float hexR = s * 0.30f;

        // Hexagon points (pointy-top)
        var hex = new SKPoint[6];
        for (int i = 0; i < 6; i++)
        {
            double ang = Math.PI / 180 * (-90 + i * 60);
            hex[i] = new SKPoint(cx + (float)(Math.Cos(ang) * hexR), cy + (float)(Math.Sin(ang) * hexR));
        }

        using var path = new SKPath();
        path.MoveTo(hex[0]);
        for (int i = 1; i < 6; i++) path.LineTo(hex[i]);
        path.Close();

        // Hexagon fill (subtle)
        using (var fill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(0x0B, 0x0E, 0x22, 0x99)
        })
            canvas.DrawPath(path, fill);

        // Hexagon gradient stroke
        using (var stroke = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = s * 0.045f,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(cx - hexR, cy - hexR),
                new SKPoint(cx + hexR, cy + hexR),
                new[] { Cyan, Violet, Pink },
                null,
                SKShaderTileMode.Clamp)
        })
            canvas.DrawPath(path, stroke);

        // Play triangle with gradient
        float t = s * 0.115f;
        using var play = new SKPath();
        play.MoveTo(cx - t * 0.85f, cy - t);
        play.LineTo(cx - t * 0.85f, cy + t);
        play.LineTo(cx + t * 1.05f, cy);
        play.Close();
        using (var playPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(cx - t, cy - t),
                new SKPoint(cx + t, cy + t),
                new[] { Cyan, Pink },
                null,
                SKShaderTileMode.Clamp)
        })
            canvas.DrawPath(play, playPaint);

        // Node dots on hexagon vertices (the "hub" nodes)
        SKColor[] dotColors = { Cyan, Violet, Pink, Violet, Cyan, Pink };
        for (int i = 0; i < 6; i++)
        {
            using var dot = new SKPaint { IsAntialias = true, Color = dotColors[i] };
            canvas.DrawCircle(hex[i], s * 0.033f, dot);
            using var dotGlow = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateRadialGradient(
                    hex[i], s * 0.075f,
                    new[] { new SKColor(dotColors[i].Red, dotColors[i].Green, dotColors[i].Blue, 0x66), new SKColor(0, 0, 0, 0) },
                    new[] { 0f, 1f }, SKShaderTileMode.Clamp)
            };
            canvas.DrawCircle(hex[i], s * 0.075f, dotGlow);
        }

        return bmp;
    }

    static SKBitmap RenderBanner(int w, int h)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(BgDark);

        using (var bg = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(w, h),
                new[] { BgDark, new SKColor(0x1B, 0x10, 0x3A), BgPurple },
                new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp)
        })
            canvas.DrawRect(0, 0, w, h, bg);

        using (var glow = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(w * 0.75f, h * 0.3f), w * 0.5f,
                new[] { new SKColor(0xA8, 0x55, 0xF7, 0x44), new SKColor(0xA8, 0x55, 0xF7, 0x00) },
                new[] { 0f, 1f }, SKShaderTileMode.Clamp)
        })
            canvas.DrawRect(0, 0, w, h, glow);

        // Mark on the left
        using (var mark = RenderMark(h - 80))
        {
            canvas.DrawBitmap(mark, new SKRect(50, 40, 50 + (h - 80), 40 + (h - 80)));
        }

        float tx = h + 40;
        using var titleFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), h * 0.30f);
        using var titlePaint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(tx, 0), new SKPoint(w - 40, 0),
                new[] { Cyan, Violet, Pink }, null, SKShaderTileMode.Clamp)
        };
        canvas.DrawText("GAMING HUB", tx, h * 0.50f, titleFont, titlePaint);

        using var subFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), h * 0.11f);
        using var subPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0xC9, 0xD4, 0xF0) };
        canvas.DrawText("Von Gamern fur Gamer", tx, h * 0.66f, subFont, subPaint);

        return bmp;
    }

    // A short, quiet two-note chime used as the app intro sound.
    static void WriteIntroWav(string path)
    {
        const int sampleRate = 44100;
        const double duration = 1.25;
        int samples = (int)(sampleRate * duration);

        // (frequency, startSeconds, amplitude, decay)
        var notes = new (double f, double start, double amp, double decay)[]
        {
            (523.25, 0.00, 0.16, 3.2),  // C5
            (783.99, 0.11, 0.13, 3.6),  // G5
            (1046.50, 0.22, 0.10, 4.2), // C6
            (130.81, 0.00, 0.10, 1.6)   // C3 warmth
        };

        var buffer = new short[samples];
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / sampleRate;
            double v = 0;
            foreach (var n in notes)
            {
                if (t < n.start) continue;
                double dt = t - n.start;
                double env = Math.Exp(-dt * n.decay);
                v += Math.Sin(2 * Math.PI * n.f * t) * env * n.amp;
            }
            // soft attack and fade-out to avoid clicks
            double attack = Math.Min(1, t / 0.012);
            double release = Math.Min(1, (duration - t) / 0.25);
            v *= attack * Math.Max(0, release);
            v = Math.Clamp(v, -0.98, 0.98);
            buffer[i] = (short)(v * short.MaxValue);
        }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        int dataSize = buffer.Length * 2;
        bw.Write(new[] { 'R', 'I', 'F', 'F' });
        bw.Write(36 + dataSize);
        bw.Write(new[] { 'W', 'A', 'V', 'E' });
        bw.Write(new[] { 'f', 'm', 't', ' ' });
        bw.Write(16);              // PCM chunk size
        bw.Write((short)1);        // PCM
        bw.Write((short)1);        // mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2);  // byte rate
        bw.Write((short)2);        // block align
        bw.Write((short)16);       // bits
        bw.Write(new[] { 'd', 'a', 't', 'a' });
        bw.Write(dataSize);
        foreach (short s in buffer) bw.Write(s);
        bw.Flush();
        File.WriteAllBytes(path, ms.ToArray());
        Console.WriteLine("  wrote " + path);
    }

    static void SavePng(SKBitmap bmp, string path)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
        Console.WriteLine("  wrote " + path);
    }

    // Build a Windows .ico container with PNG-compressed entries.
    static byte[] BuildIco(int[] sizes)
    {
        var images = new List<byte[]>();
        var metas = new List<(int size, byte[] data)>();
        foreach (var sz in sizes)
        {
            using var bmp = RenderMark(sz);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            metas.Add((sz, data.ToArray()));
        }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0);            // reserved
        bw.Write((ushort)1);            // type = icon
        bw.Write((ushort)metas.Count);  // count

        int offset = 6 + metas.Count * 16;
        foreach (var (sz, data) in metas)
        {
            bw.Write((byte)(sz >= 256 ? 0 : sz)); // width
            bw.Write((byte)(sz >= 256 ? 0 : sz)); // height
            bw.Write((byte)0);  // colors
            bw.Write((byte)0);  // reserved
            bw.Write((ushort)1); // planes
            bw.Write((ushort)32); // bpp
            bw.Write(data.Length);
            bw.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in metas)
            bw.Write(data);

        bw.Flush();
        return ms.ToArray();
    }
}
