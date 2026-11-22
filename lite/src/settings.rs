//! 설정 파일. 정식 버전과 섞이지 않게 따로 둠:
//!   Windows: %APPDATA%\AtbashCipherToolLite\settings.json
//!   그 밖:   $XDG_CONFIG_HOME (없으면 ~/.config) /AtbashCipherToolLite/settings.json

use serde::{Deserialize, Serialize};
use std::path::PathBuf;

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
#[serde(default, rename_all = "camelCase")]
pub struct Settings {
    pub language: String,
    pub dark: bool,
    pub cipher_mode: i32,
    pub caesar_shift: i32,
    pub scytale_diameter: i32,
    pub hangul_shift: i32,
    /// 마지막으로 자동 업데이트 확인을 한 때 (밀리초)
    pub last_update_check: u64,
    /// "이 버전 건너뛰기" 한 버전
    pub skipped_version: Option<String>,
}

impl Default for Settings {
    /// 정식 버전과 같은 기본값
    fn default() -> Self {
        Settings {
            language: "KOREAN".into(),
            dark: false,
            cipher_mode: 0,
            caesar_shift: 3,
            scytale_diameter: 5,
            hangul_shift: 3,
            last_update_check: 0,
            skipped_version: None,
        }
    }
}

pub fn default_path() -> PathBuf {
    let base = std::env::var_os("APPDATA")
        .map(PathBuf::from)
        .or_else(|| std::env::var_os("XDG_CONFIG_HOME").map(PathBuf::from).filter(|p| p.is_absolute()))
        .or_else(|| std::env::var_os("HOME").map(|h| PathBuf::from(h).join(".config")))
        .unwrap_or_else(|| PathBuf::from("."));
    base.join("AtbashCipherToolLite").join("settings.json")
}

impl Settings {
    /// 손상된 파일이나 없는 파일은 기본값으로 시작
    pub fn load(path: &std::path::Path) -> Settings {
        std::fs::read(path)
            .ok()
            .and_then(|bytes| serde_json::from_slice(&bytes).ok())
            .unwrap_or_default()
    }

    /// 임시 파일에 쓴 뒤 바꿔치기 (중간에 꺼져도 설정 파일이 깨지지 않게)
    pub fn save(&self, path: &std::path::Path) -> std::io::Result<()> {
        if let Some(dir) = path.parent() {
            std::fs::create_dir_all(dir)?;
        }
        let temp = path.with_extension("json.tmp");
        std::fs::write(&temp, serde_json::to_vec_pretty(self).expect("settings serialize"))?;
        std::fs::rename(&temp, path)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn roundtrip_and_broken_files() {
        let dir = std::env::temp_dir().join(format!("atbash-lite-test-{}", std::process::id()));
        let path = dir.join("settings.json");
        let s = Settings { language: "ENGLISH".into(), dark: true, cipher_mode: 9, caesar_shift: -7, ..Default::default() };
        s.save(&path).unwrap();
        assert_eq!(Settings::load(&path), s);

        std::fs::write(&path, b"{ broken").unwrap();
        assert_eq!(Settings::load(&path), Settings::default());
        std::fs::write(&path, br#"{"language":"JAPANESE"}"#).unwrap(); // 빠진 값은 기본값
        assert_eq!(Settings::load(&path).caesar_shift, 3);
        std::fs::remove_dir_all(dir).unwrap();
    }
}
