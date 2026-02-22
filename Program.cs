using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

#region Native structs

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Img
{
    public int T;
    public int Col;
    public int Row;
    public int Unk;
    public long Step;
    public long DataPtr;
}

struct OcrBoundingBox
{
    public float X1;
    public float Y1;
    public float X2;
    public float Y2;
    public float X3;
    public float Y3;
    public float X4;
    public float Y4;
}

#endregion

#region PInvoke

static class OneOcr
{
    const string DLL = "oneocr.dll";

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long CreateOcrInitOptions(out IntPtr ctx);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long OcrInitOptionsSetUseModelDelayLoad(IntPtr ctx, byte flag);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long CreateOcrPipeline(
        IntPtr modelPath,
        IntPtr key,
        IntPtr ctx,
        out IntPtr pipeline);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long CreateOcrProcessOptions(out IntPtr opt);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long OcrProcessOptionsSetMaxRecognitionLineCount(IntPtr opt, long count);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long RunOcrPipeline(
        IntPtr pipeline, ref Img img, IntPtr opt, out IntPtr instance);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long GetOcrLineCount(IntPtr instance, out long count);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long GetOcrLine(IntPtr instance, long index, out IntPtr line);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long GetOcrLineContent(IntPtr line, out IntPtr textPtr);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long GetOcrLineBoundingBox(IntPtr line, out IntPtr boxPtr);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long GetOcrLineWordCount(IntPtr line, out long count);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long GetOcrWord(IntPtr line, long index, out IntPtr word);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long GetOcrWordContent(IntPtr word, out IntPtr textPtr);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern long GetOcrWordBoundingBox(IntPtr word, out IntPtr boxPtr);
}

#endregion

#region DTOs

class BoundingBox
{
    public BoundingBox(OcrBoundingBox box)
    {
        X = box.X1;
        Y = box.Y1;
        Width = box.X3 - X;
        Height = box.Y3 - Y;
    }

    [JsonPropertyName("x")]
    public float X { get; }
    [JsonPropertyName("y")]
    public float Y { get; }
    [JsonPropertyName("width")]
    public float Width { get; }
    [JsonPropertyName("height")]
    public float Height { get; }
}

class OcrWordDto
{
    [JsonPropertyName("text")]
    public string Text { get; set; }
    [JsonPropertyName("boundingBox")]
    public BoundingBox BoundingBox { get; set; }
}

class OcrLineDto
{
    [JsonPropertyName("text")]
    public string Text { get; set; }
    [JsonPropertyName("boundingBox")]
    public BoundingBox BoundingBox { get; set; }
    [JsonPropertyName("words")]
    public List<OcrWordDto> Words { get; set; } = new List<OcrWordDto>();
}

class OcrResultDto
{
    [JsonPropertyName("lines")]
    public List<OcrLineDto> Lines { get; set; } = new List<OcrLineDto>();
}

#endregion

class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: OneOcrWrapper.exe <image.png|bmp|jpg> [--pretty-print]");
            return;
        }

        bool prettyPrint = args.Length > 1 && args[1] == "--pretty-print";

        using Bitmap bmp = new Bitmap(args[0]);
        using Bitmap bgra = bmp.Clone(new Rectangle(0, 0, bmp.Width, bmp.Height), PixelFormat.Format32bppArgb);

        int width = bgra.Width;
        int height = bgra.Height;

        BitmapData bmpData = bgra.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            Img img = new Img
            {
                T = 3,
                Col = width,
                Row = height,
                Unk = 0,
                Step = bmpData.Stride,
                DataPtr = bmpData.Scan0.ToInt64()
            };

            var result = RunOcr(img);
            OutputJson(result, prettyPrint);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            bgra.UnlockBits(bmpData);
        }
    }

    static byte[] StringToAnsiBytes(string s)
    {
        // null-terminated ANSI bytes, matching const char* in C++ 
        var bytes = System.Text.Encoding.Default.GetBytes(s);
        var result = new byte[bytes.Length + 1];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    static void OutputJson(OcrResultDto resultDto, bool prettyPrint)
    {
        string jsonOutput = JsonSerializer.Serialize(resultDto, new JsonSerializerOptions { WriteIndented = prettyPrint });
        Console.WriteLine(jsonOutput);
    }

    static OcrResultDto RunOcr(Img img)
    {
        IntPtr ctx, pipeline, opt, instance;

        long res;

        res = OneOcr.CreateOcrInitOptions(out ctx);
        if (res != 0) throw new Exception($"CreateOcrInitOptions failed: {res}");

        res = OneOcr.OcrInitOptionsSetUseModelDelayLoad(ctx, 0);
        if (res != 0) throw new Exception($"OcrInitOptionsSetUseModelDelayLoad failed: {res}");

        string model = "oneocr.onemodel";
        string key = "kj)TGtrK>f]b[Piow.gU+nC@s\"\"\"\"\"\"4";

        // Pin byte arrays so the native side gets stable const char* pointers
        byte[] modelBytes = StringToAnsiBytes(model);
        byte[] keyBytes = StringToAnsiBytes(key);

        GCHandle modelHandle = GCHandle.Alloc(modelBytes, GCHandleType.Pinned);
        GCHandle keyHandle = GCHandle.Alloc(keyBytes, GCHandleType.Pinned);

        try
        {
            IntPtr modelPtr = modelHandle.AddrOfPinnedObject();
            IntPtr keyPtr = keyHandle.AddrOfPinnedObject();

            res = OneOcr.CreateOcrPipeline(modelPtr, keyPtr, ctx, out pipeline);
            if (res != 0) throw new Exception($"CreateOcrPipeline failed: {res}");

            res = OneOcr.CreateOcrProcessOptions(out opt);
            if (res != 0) throw new Exception($"CreateOcrProcessOptions failed: {res}");

            res = OneOcr.OcrProcessOptionsSetMaxRecognitionLineCount(opt, 1000);
            if (res != 0) throw new Exception($"OcrProcessOptionsSetMaxRecognitionLineCount failed: {res}");

            res = OneOcr.RunOcrPipeline(pipeline, ref img, opt, out instance);
            if (res != 0) throw new Exception($"RunOcrPipeline failed: {res}");
        }
        finally
        {
            modelHandle.Free();
            keyHandle.Free();
        }

        OneOcr.GetOcrLineCount(instance, out long lines);

        return CreateOcrResultDto(instance, lines);
    }

    static OcrResultDto CreateOcrResultDto(IntPtr instance, long lines)
    {
        var resultDto = new OcrResultDto();

        for (long i = 0; i < lines; i++)
        {
            OneOcr.GetOcrLine(instance, i, out IntPtr line);
            if (line == IntPtr.Zero) continue;

            OneOcr.GetOcrLineContent(line, out IntPtr textPtr);
            string text = Marshal.PtrToStringAnsi(textPtr);

            OneOcr.GetOcrLineBoundingBox(line, out IntPtr boxPtr);
            OcrBoundingBox box = Marshal.PtrToStructure<OcrBoundingBox>(boxPtr);

            var lineDto = new OcrLineDto
            {
                Text = text,
                BoundingBox = new BoundingBox(box)
            };

            OneOcr.GetOcrLineWordCount(line, out long wc);
            for (long j = 0; j < wc; j++)
            {
                OneOcr.GetOcrWord(line, j, out IntPtr word);
                OneOcr.GetOcrWordContent(word, out IntPtr wptr);
                OneOcr.GetOcrWordBoundingBox(word, out IntPtr wboxPtr);
                OcrBoundingBox wbox = Marshal.PtrToStructure<OcrBoundingBox>(wboxPtr);

                string wtext = Marshal.PtrToStringAnsi(wptr);

                lineDto.Words.Add(new OcrWordDto
                {
                    Text = wtext,
                    BoundingBox = new BoundingBox(wbox)
                });
            }

            resultDto.Lines.Add(lineDto);
        }

        return resultDto;
    }
}
