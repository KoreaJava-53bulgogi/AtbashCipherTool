//! 모든 변환의 입구 (C# CipherEngine.cs = Java AtbashCipherGUI.performTransformation 과 같은 규칙).

use super::{aes, ciphers, codec};

/// 변환 방식. 번호는 Java·C# 버전의 목록 순서이자 저장된 설정 값이므로 바꾸지 말 것
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Mode {
    Atbash = 0,
    Morse = 1,
    Binary = 2,
    CaesarEncrypt = 3,
    CaesarDecrypt = 4,
    ScytaleEncrypt = 5,
    ScytaleDecrypt = 6,
    VigenereEncrypt = 7,
    VigenereDecrypt = 8,
    AesTextEncrypt = 9,
    AesTextDecrypt = 10,
    Base64Encode = 11,
    Base64Decode = 12,
    HexEncode = 13,
    HexDecode = 14,
    UrlEncode = 15,
    UrlDecode = 16,
    Rot13 = 17,
    HangulEncrypt = 18,
    HangulDecrypt = 19,
}

impl Mode {
    pub const ALL: [Mode; 20] = [
        Mode::Atbash, Mode::Morse, Mode::Binary, Mode::CaesarEncrypt, Mode::CaesarDecrypt,
        Mode::ScytaleEncrypt, Mode::ScytaleDecrypt, Mode::VigenereEncrypt, Mode::VigenereDecrypt,
        Mode::AesTextEncrypt, Mode::AesTextDecrypt, Mode::Base64Encode, Mode::Base64Decode,
        Mode::HexEncode, Mode::HexDecode, Mode::UrlEncode, Mode::UrlDecode, Mode::Rot13,
        Mode::HangulEncrypt, Mode::HangulDecrypt,
    ];

    pub fn from_index(i: i32) -> Option<Mode> {
        usize::try_from(i).ok().and_then(|i| Mode::ALL.get(i).copied())
    }

    /// 언어 파일의 이름 키 (C# MainViewModel.ModeKeys 와 같은 순서)
    pub fn name_key(self) -> &'static str {
        const KEYS: [&str; 20] = [
            "CIPHER_ATBASH", "CIPHER_MORSE", "CIPHER_BINARY",
            "CIPHER_CAESAR_ENCRYPT", "CIPHER_CAESAR_DECRYPT",
            "CIPHER_SCYTALE_ENCRYPT", "CIPHER_SCYTALE_DECRYPT",
            "CIPHER_VIGENERE_ENCRYPT", "CIPHER_VIGENERE_DECRYPT",
            "CIPHER_AES_TEXT_ENCRYPT", "CIPHER_AES_TEXT_DECRYPT",
            "CIPHER_BASE64_ENCODE", "CIPHER_BASE64_DECODE",
            "CIPHER_HEX_ENCODE", "CIPHER_HEX_DECODE",
            "CIPHER_URL_ENCODE", "CIPHER_URL_DECODE",
            "CIPHER_ROT13",
            "CIPHER_HANGUL_ENCRYPT", "CIPHER_HANGUL_DECRYPT",
        ];
        KEYS[self as usize]
    }

    pub fn parameter(self) -> Parameter {
        match self {
            Mode::CaesarEncrypt | Mode::CaesarDecrypt | Mode::HangulEncrypt | Mode::HangulDecrypt => Parameter::Shift,
            Mode::ScytaleEncrypt | Mode::ScytaleDecrypt => Parameter::Diameter,
            Mode::VigenereEncrypt | Mode::VigenereDecrypt => Parameter::Keyword,
            Mode::AesTextEncrypt | Mode::AesTextDecrypt => Parameter::Password,
            _ => Parameter::None,
        }
    }

    pub fn is_aes(self) -> bool {
        matches!(self, Mode::AesTextEncrypt | Mode::AesTextDecrypt)
    }
}

/// 변환 방식마다 필요한 입력칸
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Parameter {
    None,
    Shift,
    Diameter,
    Keyword,
    Password,
}

/// 변환에 실패했을 때 화면에 보여줄 안내의 종류 (언어 파일 키)
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum TransformError {
    /// 입력이 그 형식으로 인코딩된 문자열이 아님 (DECODE_ERROR)
    DecodeFailed,
    /// AES 인데 비밀번호가 비어 있음 (AES_TEXT_NO_PASSWORD)
    AesNoPassword,
    /// 비밀번호가 틀렸거나 AES 텍스트 암호문이 아님 (AES_TEXT_DECRYPT_ERROR)
    AesDecryptFailed,
}

impl TransformError {
    pub fn message_key(self) -> &'static str {
        match self {
            TransformError::DecodeFailed => "DECODE_ERROR",
            TransformError::AesNoPassword => "AES_TEXT_NO_PASSWORD",
            TransformError::AesDecryptFailed => "AES_TEXT_DECRYPT_ERROR",
        }
    }
}

/// `key`: 이동 값 또는 지름, `keyword`: 비즈네르 키워드 또는 AES 비밀번호
pub fn transform(mode: Mode, input: &[u16], key: i32, keyword: &[u16]) -> Result<Vec<u16>, TransformError> {
    let decode = |r: Option<Vec<u16>>| r.ok_or(TransformError::DecodeFailed);
    match mode {
        Mode::Atbash => Ok(ciphers::atbash(input)),
        Mode::Morse => Ok(if ciphers::is_morse(input) { ciphers::from_morse(input) } else { ciphers::to_morse(input) }),
        Mode::Binary => Ok(if ciphers::is_binary(input) { ciphers::from_binary(input) } else { ciphers::to_binary(input) }),
        Mode::CaesarEncrypt => Ok(ciphers::caesar_encrypt(input, key)),
        Mode::CaesarDecrypt => Ok(ciphers::caesar_decrypt(input, key)),
        Mode::ScytaleEncrypt => Ok(ciphers::scytale_encrypt(input, key)),
        Mode::ScytaleDecrypt => Ok(ciphers::scytale_decrypt(input, key)),
        Mode::VigenereEncrypt => Ok(ciphers::vigenere(input, keyword, true)),
        Mode::VigenereDecrypt => Ok(ciphers::vigenere(input, keyword, false)),
        Mode::AesTextEncrypt | Mode::AesTextDecrypt => {
            if keyword.is_empty() {
                return Err(TransformError::AesNoPassword);
            }
            if input.is_empty() {
                return Ok(Vec::new());
            }
            if mode == Mode::AesTextEncrypt {
                Ok(aes::encrypt_text(input, keyword))
            } else {
                aes::decrypt_text(input, keyword).map_err(|_| TransformError::AesDecryptFailed)
            }
        }
        Mode::Base64Encode => Ok(codec::to_base64(input)),
        Mode::Base64Decode => decode(codec::from_base64(input)),
        Mode::HexEncode => Ok(codec::to_hex(input)),
        Mode::HexDecode => decode(codec::from_hex(input)),
        Mode::UrlEncode => Ok(codec::to_url(input)),
        Mode::UrlDecode => decode(codec::from_url(input)),
        Mode::Rot13 => Ok(codec::rot13(input)),
        Mode::HangulEncrypt => Ok(ciphers::hangul_encrypt(input, key)),
        Mode::HangulDecrypt => Ok(ciphers::hangul_decrypt(input, key)),
    }
}
