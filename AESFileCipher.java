import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.RandomAccessFile;
import java.security.SecureRandom;
import java.security.spec.KeySpec;

import javax.crypto.Cipher;
import javax.crypto.CipherInputStream;
import javax.crypto.CipherOutputStream;
import javax.crypto.SecretKey;
import javax.crypto.SecretKeyFactory;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.PBEKeySpec;
import javax.crypto.spec.SecretKeySpec;

/**
 * AES (Advanced Encryption Standard)를 사용하여 파일을 암호화 및 복호화하는 유틸리티 클래스입니다.
 * 비밀번호 기반 암호화(PBE)를 사용하여 키를 생성하고, 보안을 위해 Salt와 IV를 사용합니다.
 */
public class AESFileCipher {

    private static final int KEY_SIZE = 256; // AES-256
    private static final int ITERATION_COUNT = 65536; // PBKDF2 반복 횟수
    private static final int SALT_LENGTH = 16; // 16 bytes for salt
    private static final int IV_LENGTH = 12; // 12 bytes is recommended for GCM
    private static final int TAG_LENGTH = 128; // Authentication tag length in bits
    private static final String ALGORITHM = "AES";
    private static final String CIPHER_MODE = "AES/GCM/NoPadding";
    private static final String SECRET_KEY_FACTORY_ALGORITHM = "PBKDF2WithHmacSHA256";

    /**
     * 비밀번호와 Salt로부터 SecretKey를 생성합니다.
     * @param password 사용자 비밀번호
     * @param salt Salt 값
     * @return 생성된 SecretKey
     * @throws Exception 키 생성 중 오류 발생 시
     */
    private static SecretKey getKeyFromPassword(String password, byte[] salt) throws Exception {
        SecretKeyFactory factory = SecretKeyFactory.getInstance(SECRET_KEY_FACTORY_ALGORITHM);
        KeySpec spec = new PBEKeySpec(password.toCharArray(), salt, ITERATION_COUNT, KEY_SIZE);
        return new SecretKeySpec(factory.generateSecret(spec).getEncoded(), ALGORITHM);
    }

    /**
     * 파일을 암호화합니다.
     * 암호화된 파일은 Salt와 IV를 파일 시작 부분에 포함합니다.
     *
     * @param inputFile 암호화할 원본 파일
     * @param outputFile 암호화된 내용을 저장할 파일
     * @param password 암호화에 사용할 비밀번호
     * @throws Exception 암호화 중 오류 발생 시
     */
    public static void encryptFile(File inputFile, File outputFile, String password) throws Exception {
        SecureRandom random = new SecureRandom();
        byte[] salt = new byte[SALT_LENGTH];
        random.nextBytes(salt); // Salt 생성

        byte[] iv = new byte[IV_LENGTH];
        random.nextBytes(iv); // IV 생성

        SecretKey secretKey = getKeyFromPassword(password, salt);

        Cipher cipher = Cipher.getInstance(CIPHER_MODE);
        cipher.init(Cipher.ENCRYPT_MODE, secretKey, new GCMParameterSpec(TAG_LENGTH, iv));

        try (FileInputStream fis = new FileInputStream(inputFile);
             FileOutputStream fos = new FileOutputStream(outputFile);
             CipherOutputStream cos = new CipherOutputStream(fos, cipher)) {

            fos.write(salt); // Salt를 파일 시작 부분에 저장
            fos.write(iv);   // IV를 Salt 다음에 저장

            byte[] buffer = new byte[4096];
            int bytesRead;
            while ((bytesRead = fis.read(buffer)) != -1) {
                cos.write(buffer, 0, bytesRead);
            }
        }
    }

    /**
     * 파일을 복호화합니다.
     * 파일 시작 부분에서 Salt와 IV를 읽어와 복호화에 사용합니다.
     *
     * @param inputFile 복호화할 암호화된 파일
     * @param outputFile 복호화된 내용을 저장할 파일
     * @param password 복호화에 사용할 비밀번호
     * @throws Exception 복호화 중 오류 발생 시 (잘못된 비밀번호 포함)
     */
    public static void decryptFile(File inputFile, File outputFile, String password) throws Exception {
        try (FileInputStream fis = new FileInputStream(inputFile);
             FileOutputStream fos = new FileOutputStream(outputFile)) {

            byte[] salt = new byte[SALT_LENGTH];
            if (fis.read(salt) != SALT_LENGTH) {
                throw new IOException("파일에서 Salt를 읽을 수 없습니다. 파일이 손상되었거나 암호화되지 않았을 수 있습니다.");
            }

            byte[] iv = new byte[IV_LENGTH];
            if (fis.read(iv) != IV_LENGTH) {
                throw new IOException("파일에서 IV를 읽을 수 없습니다. 파일이 손상되었거나 암호화되지 않았을 수 있습니다.");
            }

            SecretKey secretKey = getKeyFromPassword(password, salt);
            Cipher cipher = Cipher.getInstance(CIPHER_MODE);
            cipher.init(Cipher.DECRYPT_MODE, secretKey, new GCMParameterSpec(TAG_LENGTH, iv));

            try (CipherInputStream cis = new CipherInputStream(fis, cipher)) {
                byte[] buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = cis.read(buffer)) != -1) {
                    fos.write(buffer, 0, bytesRead);
                }
            }
        }
    }

    /**
     * 텍스트를 암호화해 메신저 등에 붙여넣을 수 있는 Base64 문자열로 만듭니다.
     * 형식: Base64( salt(16) | iv(12) | 암호문+인증태그 ) — 파일 암호화와 같은 방식
     */
    public static String encryptText(String plainText, String password) throws Exception {
        SecureRandom random = new SecureRandom();
        byte[] salt = new byte[SALT_LENGTH];
        random.nextBytes(salt);
        byte[] iv = new byte[IV_LENGTH];
        random.nextBytes(iv);

        Cipher cipher = Cipher.getInstance(CIPHER_MODE);
        cipher.init(Cipher.ENCRYPT_MODE, getKeyFromPassword(password, salt), new GCMParameterSpec(TAG_LENGTH, iv));
        byte[] encrypted = cipher.doFinal(plainText.getBytes(java.nio.charset.StandardCharsets.UTF_8));

        byte[] out = new byte[SALT_LENGTH + IV_LENGTH + encrypted.length];
        System.arraycopy(salt, 0, out, 0, SALT_LENGTH);
        System.arraycopy(iv, 0, out, SALT_LENGTH, IV_LENGTH);
        System.arraycopy(encrypted, 0, out, SALT_LENGTH + IV_LENGTH, encrypted.length);
        return java.util.Base64.getEncoder().encodeToString(out);
    }

    /**
     * encryptText 로 만든 문자열을 원래 텍스트로 되돌립니다.
     * @throws javax.crypto.AEADBadTagException 비밀번호가 틀리거나 암호문이 손상된 경우
     * @throws IllegalArgumentException 암호문 형식이 아닌 경우
     */
    public static String decryptText(String encoded, String password) throws Exception {
        byte[] data = java.util.Base64.getDecoder().decode(encoded.replaceAll("\\s", ""));
        if (data.length < SALT_LENGTH + IV_LENGTH + TAG_LENGTH / 8) {
            throw new IllegalArgumentException("too short");
        }
        byte[] salt = java.util.Arrays.copyOfRange(data, 0, SALT_LENGTH);
        byte[] iv = java.util.Arrays.copyOfRange(data, SALT_LENGTH, SALT_LENGTH + IV_LENGTH);

        Cipher cipher = Cipher.getInstance(CIPHER_MODE);
        cipher.init(Cipher.DECRYPT_MODE, getKeyFromPassword(password, salt), new GCMParameterSpec(TAG_LENGTH, iv));
        byte[] plain = cipher.doFinal(data, SALT_LENGTH + IV_LENGTH, data.length - SALT_LENGTH - IV_LENGTH);
        return new String(plain, java.nio.charset.StandardCharsets.UTF_8);
    }

    /**
     * 파일을 안전하게 삭제합니다. 삭제 전 무작위 데이터로 덮어씌워 복구를 방지합니다.
     * @param file 삭제할 파일
     * @throws IOException 파일 접근 오류 시
     */
    public static void secureWipe(File file) throws IOException {
        if (file.exists() && file.isFile()) {
            long length = file.length();
            SecureRandom random = new SecureRandom();
            // "rws" 모드는 메타데이터와 콘텐츠 변경 사항을 즉시 동기화합니다.
            try (RandomAccessFile raf = new RandomAccessFile(file, "rws")) {
                byte[] data = new byte[4096];
                long pos = 0;
                while (pos < length) {
                    random.nextBytes(data);
                    int toWrite = (int) Math.min(data.length, length - pos);
                    raf.write(data, 0, toWrite);
                    pos += toWrite;
                }
            }
            file.delete();
        }
    }
}