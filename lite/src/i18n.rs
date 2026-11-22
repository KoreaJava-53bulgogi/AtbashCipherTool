//! 언어 파일: 정식 버전(Java·C#)과 같은 ko/en/ja/zh.json 을 실행 파일 안에 넣어 씀.
//! 없는 키는 영어로 채움 (C# Localizer 와 같은 규칙).

use std::collections::HashMap;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Language {
    Korean,
    English,
    Chinese,
    Japanese,
}

impl Language {
    /// 화면의 언어 목록 순서 (정식 버전 메뉴 순서와 같음)
    pub const ALL: [Language; 4] = [Language::Korean, Language::English, Language::Chinese, Language::Japanese];

    /// 저장 값 ("KOREAN" 등, 정식 버전과 같음). 모르는 값이면 한국어
    pub fn parse(saved: &str) -> Language {
        match saved.to_ascii_uppercase().as_str() {
            "ENGLISH" => Language::English,
            "CHINESE" => Language::Chinese,
            "JAPANESE" => Language::Japanese,
            _ => Language::Korean,
        }
    }

    pub fn saved(self) -> &'static str {
        match self {
            Language::Korean => "KOREAN",
            Language::English => "ENGLISH",
            Language::Chinese => "CHINESE",
            Language::Japanese => "JAPANESE",
        }
    }

    /// 언어 목록에 보여줄 이름 (그 언어로 쓴 이름)
    pub fn native_name(self) -> &'static str {
        match self {
            Language::Korean => "한국어",
            Language::English => "English",
            Language::Chinese => "简体中文",
            Language::Japanese => "日本語",
        }
    }

    fn file(self) -> &'static str {
        match self {
            Language::Korean => include_str!("../../ko.json"),
            Language::English => include_str!("../../en.json"),
            Language::Chinese => include_str!("../../zh.json"),
            Language::Japanese => include_str!("../../ja.json"),
        }
    }
}

pub struct Texts {
    strings: HashMap<String, String>,
}

impl Texts {
    pub fn load(language: Language) -> Texts {
        let mut strings: HashMap<String, String> = serde_json::from_str(Language::English.file()).unwrap_or_default();
        if language != Language::English {
            let own: HashMap<String, String> = serde_json::from_str(language.file()).unwrap_or_default();
            strings.extend(own);
        }
        Texts { strings }
    }

    /// 없는 키는 "!키!" 로 보여 줌 (정식 버전과 같음)
    pub fn get(&self, key: &str) -> String {
        self.strings.get(key).cloned().unwrap_or_else(|| format!("!{key}!"))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::core::engine::Mode;

    #[test]
    fn every_language_has_every_text_lite_uses() {
        for language in Language::ALL {
            let t = Texts::load(language);
            for mode in Mode::ALL {
                assert!(!t.get(mode.name_key()).starts_with('!'), "{language:?} {mode:?}");
            }
            for key in ["WINDOW_TITLE", "INPUT_LABEL", "OUTPUT_LABEL", "CIPHER_LABEL", "TRANSFORM_BUTTON", "COPY_BUTTON",
                "CLEAR_BUTTON", "COPY_FEEDBACK", "LOAD_MENU", "SAVE_MENU", "DARK_MODE_MENU", "LANGUAGE_MENU",
                "PARAM_SHIFT_KEY", "PARAM_DIAMETER", "PARAM_KEYWORD", "PARAM_PASSWORD", "DECODE_ERROR",
                "AES_TEXT_NO_PASSWORD", "AES_TEXT_DECRYPT_ERROR", "LOAD_ERROR_MESSAGE", "SAVE_ERROR_MESSAGE"]
            {
                assert!(!t.get(key).starts_with('!'), "{language:?} {key}");
            }
        }
        assert_eq!(Texts::load(Language::Korean).get("TRANSFORM_BUTTON"), "변환");
        assert_eq!(Language::parse("japanese"), Language::Japanese);
        assert_eq!(Language::parse("???"), Language::Korean);
    }
}
