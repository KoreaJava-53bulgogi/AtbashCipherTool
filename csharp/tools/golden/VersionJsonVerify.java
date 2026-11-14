import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;

/**
 * 이미 배포된 Java 8.1.0 의 업데이트 확인 코드(UpdateChecker.parse)로 version.json 을 읽어 봅니다.
 * 새 형식(platforms 가 덧붙은 파일)을 예전 앱이 여전히 올바르게 읽는지 확인하는 용도입니다.
 *   java VersionJsonVerify <version.json 경로>
 * 출력: 버전|다운로드 페이지|설치 파일 주소|SHA-256   (변경 내용은 한글이라 출력하지 않음)
 */
public class VersionJsonVerify {
    public static void main(String[] args) throws Exception {
        String json = new String(Files.readAllBytes(Paths.get(args[0])), StandardCharsets.UTF_8);
        UpdateChecker.UpdateInfo info = UpdateChecker.parse(json);
        System.out.println(info.version + "|" + info.downloadUrl + "|" + info.installerUrl + "|" + info.sha256);
    }
}
