pub mod aes;
pub mod ciphers;
pub mod codec;
pub mod engine;
pub mod java;

/// `String` → UTF-16 단위
pub fn to_units(s: &str) -> Vec<u16> {
    s.encode_utf16().collect()
}

/// UTF-16 단위 → `String`. 짝이 없는 서로게이트(스퀴탈레가 이모지를 가른 경우 등)는 U+FFFD 로 보여줌
pub fn from_units(units: &[u16]) -> String {
    String::from_utf16_lossy(units)
}
