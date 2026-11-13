using System;
using QRCoder;

namespace Atbash.App.Services;

/// <summary>텍스트를 QR 코드 PNG 로 (Java 버전과 같이 UTF-8, 오류 정정 M, 바깥 여백 4칸, 약 360px)</summary>
public static class QrCodeService
{
    public sealed class TooLongException : Exception
    {
        public TooLongException(Exception inner) : base("Text is too long for one QR code", inner) { }
    }

    public static byte[] CreatePng(string text, int size = 360)
    {
        QRCodeData data;
        try
        {
            data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M, forceUtf8: true,
                utf8BOM: false, eciMode: QRCodeGenerator.EciMode.Utf8);
        }
        catch (QRCoder.Exceptions.DataTooLongException e)
        {
            throw new TooLongException(e);
        }
        using (data)
        {
            // ModuleMatrix 에는 바깥 여백 4칸이 이미 들어 있음
            int modules = data.ModuleMatrix.Count;
            int pixelsPerModule = Math.Max(1, (int)Math.Ceiling(size / (double)modules));
            return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        }
    }
}
