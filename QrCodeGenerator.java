import java.awt.Color;
import java.awt.image.BufferedImage;
import java.util.EnumMap;
import java.util.Map;

import com.google.zxing.BarcodeFormat;
import com.google.zxing.EncodeHintType;
import com.google.zxing.WriterException;
import com.google.zxing.common.BitMatrix;
import com.google.zxing.qrcode.QRCodeWriter;
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel;

/**
 * 텍스트를 QR 코드 이미지로 만듭니다. (ZXing 사용)
 * 한글도 읽히도록 UTF-8 로 넣습니다.
 */
public class QrCodeGenerator {

    /** 텍스트가 QR 코드 하나에 담기에 너무 긴 경우 */
    public static class TooLongException extends Exception {
        TooLongException(Throwable cause) {
            super(cause);
        }
    }

    /**
     * @param text 넣을 텍스트
     * @param size 이미지 한 변의 최소 픽셀 수 (QR 칸 크기에 맞춰 조금 커질 수 있음)
     */
    public static BufferedImage create(String text, int size) throws TooLongException {
        Map<EncodeHintType, Object> hints = new EnumMap<>(EncodeHintType.class);
        hints.put(EncodeHintType.CHARACTER_SET, "UTF-8");
        hints.put(EncodeHintType.ERROR_CORRECTION, ErrorCorrectionLevel.M);
        hints.put(EncodeHintType.MARGIN, 0); // 여백은 아래에서 직접 그림

        BitMatrix matrix;
        try {
            // 크기 1로 만들면 칸 하나 = 1 비트인 원본 행렬을 얻음
            matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 1, 1, hints);
        } catch (WriterException | IllegalArgumentException e) {
            throw new TooLongException(e);
        }

        int modules = matrix.getWidth();
        int quiet = 4; // QR 규격의 바깥 여백(4칸)
        int scale = Math.max(1, (int) Math.ceil(size / (double) (modules + quiet * 2)));
        int px = (modules + quiet * 2) * scale;

        BufferedImage image = new BufferedImage(px, px, BufferedImage.TYPE_INT_RGB);
        int white = Color.WHITE.getRGB();
        int black = Color.BLACK.getRGB();
        for (int y = 0; y < px; y++) {
            for (int x = 0; x < px; x++) {
                int mx = x / scale - quiet;
                int my = y / scale - quiet;
                boolean dark = mx >= 0 && my >= 0 && mx < modules && my < modules && matrix.get(mx, my);
                image.setRGB(x, y, dark ? black : white);
            }
        }
        return image;
    }
}
