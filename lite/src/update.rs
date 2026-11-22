//! 업데이트 알림 (알림만, 자동 설치 없음).
//! 정식 앱과 같은 version.json 을 읽되, 경량판 항목(platforms."win-x64-lite")만 봄.
//! 태그 규칙: lite_9.1.0 (버전 번호는 정식 버전과 맞춤). C# 쪽 PlatformInfo.LiteWindowsX64 와 같은 이름.

use std::cmp::Ordering;
use std::time::Duration;

pub const MANIFEST_URL: &str = "https://raw.githubusercontent.com/KoreaJava-53bulgogi/AtbashCipherTool/main/version.json";
pub const RELEASES_PAGE: &str = "https://github.com/KoreaJava-53bulgogi/AtbashCipherTool/releases";
pub const PLATFORM_ID: &str = "win-x64-lite";
/// 시험용: 이 환경 변수에 주소(file:///... 도 가능)를 넣으면 그 version.json 을 읽음
pub const TEST_URL_ENV: &str = "ATBASH_UPDATE_URL";
/// 자동 확인 간격 (정식 앱과 같음: 하루)
pub const AUTO_CHECK_INTERVAL_MS: u64 = 24 * 60 * 60 * 1000;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Release {
    pub version: String,
    /// 다운로드 페이지
    pub url: String,
    pub notes: String,
}

/// version.json 에서 경량판 항목을 꺼냄. 항목이 아직 없으면 `Ok(None)` (= 알릴 새 버전 없음)
pub fn select(json: &str) -> Result<Option<Release>, String> {
    let doc: serde_json::Value = serde_json::from_str(json).map_err(|e| e.to_string())?;
    let Some(entry) = doc.get("platforms").and_then(|p| p.get(PLATFORM_ID)) else {
        return Ok(None);
    };
    let text = |key: &str| entry.get(key).and_then(|v| v.as_str()).map(str::to_string);
    let version = text("version").ok_or("missing version")?;
    let url = text("url")
        .filter(|u| u.starts_with("https://"))
        .or_else(|| text("tag").map(|t| format!("https://github.com/KoreaJava-53bulgogi/AtbashCipherTool/releases/tag/{t}")))
        .unwrap_or_else(|| RELEASES_PAGE.to_string());
    Ok(Some(Release { version, url, notes: text("notes").unwrap_or_default() }))
}

/// 정식 앱(C# UpdateInfo.CompareVersions)과 같은 비교: 숫자 부분 먼저, 같으면 정식 > 베타, beta < beta.2
pub fn compare_versions(a: &str, b: &str) -> Ordering {
    fn split(v: &str) -> (Vec<i64>, Option<String>) {
        let v = v.trim().trim_start_matches(['v', 'V']);
        let (core, pre) = match v.find('-') {
            Some(i) => (&v[..i], Some(&v[i + 1..])),
            None => (v, None),
        };
        let numbers = core.split('.').map(|p| p.parse().unwrap_or(0)).collect();
        (numbers, pre.filter(|p| !p.is_empty()).map(str::to_string))
    }
    let (na, pa) = split(a);
    let (nb, pb) = split(b);
    for i in 0..na.len().max(nb.len()) {
        let (x, y) = (na.get(i).copied().unwrap_or(0), nb.get(i).copied().unwrap_or(0));
        if x != y {
            return x.cmp(&y);
        }
    }
    match (pa, pb) {
        (None, None) => Ordering::Equal,
        (None, Some(_)) => Ordering::Greater,
        (Some(_), None) => Ordering::Less,
        (Some(pa), Some(pb)) => {
            let (ia, ib): (Vec<&str>, Vec<&str>) = (pa.split('.').collect(), pb.split('.').collect());
            for (x, y) in ia.iter().zip(ib.iter()) {
                let c = match (x.parse::<i64>(), y.parse::<i64>()) {
                    (Ok(x), Ok(y)) => x.cmp(&y),
                    (Ok(_), Err(_)) => Ordering::Less, // 숫자 이름이 글자 이름보다 낮음
                    (Err(_), Ok(_)) => Ordering::Greater,
                    (Err(_), Err(_)) => x.cmp(y),
                };
                if c != Ordering::Equal {
                    return c;
                }
            }
            ia.len().cmp(&ib.len())
        }
    }
}

/// version.json 내용 가져오기 (시험용 환경 변수가 있으면 그 주소, file:/// 도 됨)
pub fn fetch_manifest() -> Result<String, String> {
    let url = std::env::var(TEST_URL_ENV).ok().filter(|u| !u.is_empty()).unwrap_or_else(|| MANIFEST_URL.to_string());
    if let Some(path) = url.strip_prefix("file:///") {
        return std::fs::read_to_string(path.replace("%20", " ")).map_err(|e| e.to_string());
    }
    let agent: ureq::Agent = ureq::Agent::config_builder()
        .tls_config(ureq::tls::TlsConfig::builder().provider(ureq::tls::TlsProvider::NativeTls).build())
        .timeout_global(Some(Duration::from_secs(15)))
        .build()
        .into();
    agent
        .get(&url)
        .call()
        .map_err(|e| e.to_string())?
        .body_mut()
        .read_to_string()
        .map_err(|e| e.to_string())
}

/// 브라우저로 주소 열기
pub fn open_in_browser(url: &str) {
    #[cfg(windows)]
    let _ = std::process::Command::new("explorer").arg(url).spawn();
    #[cfg(target_os = "macos")]
    let _ = std::process::Command::new("open").arg(url).spawn();
    #[cfg(all(unix, not(target_os = "macos")))]
    let _ = std::process::Command::new("xdg-open").arg(url).spawn();
}

pub fn now_ms() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map_or(0, |d| d.as_millis() as u64)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn compares_like_the_full_app() {
        use Ordering::*;
        for (a, b, want) in [
            ("9.1.0", "9.1.0-beta", Greater),
            ("9.1.0-beta", "9.1.0", Less),
            ("9.1.0-beta.2", "9.1.0-beta", Greater),
            ("9.1.0-beta.10", "9.1.0-beta.9", Greater),
            ("9.1.0-rc", "9.1.0-beta", Greater),
            ("9.10.0", "9.9.0", Greater),
            ("v9.0.0", "9.0.0", Equal),
            ("9.0", "9.0.0", Equal),
        ] {
            assert_eq!(compare_versions(a, b), want, "{a} vs {b}");
        }
    }

    #[test]
    fn reads_only_the_lite_entry() {
        let json = r#"{ "version": "9.9.9", "installer": "x", "platforms": {
            "win-x64": { "version": "9.9.9" },
            "win-x64-lite": { "version": "9.1.0", "tag": "lite_9.1.0", "url": "https://github.com/x/releases/tag/lite_9.1.0", "notes": "경량판" } } }"#;
        let r = select(json).unwrap().unwrap();
        assert_eq!(r.version, "9.1.0");
        assert_eq!(r.notes, "경량판");
        assert!(r.url.ends_with("/lite_9.1.0"));

        // 경량판 항목이 아직 없으면 = 새 버전 없음 (정식 앱의 9.9.9 를 알리면 안 됨)
        assert_eq!(select(r#"{ "version": "9.9.9" }"#), Ok(None));
        // url 이 없으면 태그 페이지
        let r = select(r#"{ "platforms": { "win-x64-lite": { "version": "1.0.0", "tag": "lite_1.0.0" } } }"#).unwrap().unwrap();
        assert_eq!(r.url, "https://github.com/KoreaJava-53bulgogi/AtbashCipherTool/releases/tag/lite_1.0.0");
        assert!(select("not json").is_err());
    }

    /// 인터넷이 필요해서 평소에는 건너뜀: cargo test -- --ignored
    #[test]
    #[ignore]
    fn fetches_the_real_manifest_over_https() {
        let json = fetch_manifest().expect("download version.json");
        assert!(json.contains("\"version\""));
        assert!(select(&json).is_ok());
    }

    #[test]
    fn reads_the_committed_version_json() {
        // 저장소의 실제 version.json 을 읽을 수 있어야 함 (경량판 항목이 없으면 None)
        let json = std::fs::read_to_string(concat!(env!("CARGO_MANIFEST_DIR"), "/../version.json")).unwrap();
        assert!(select(&json).is_ok());
    }
}
