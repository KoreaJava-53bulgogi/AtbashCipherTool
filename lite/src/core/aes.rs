//! AES-256-GCM 텍스트 암호화. Java·C# 버전과 형식이 같아서 서로 만든 결과를 풀 수 있음.
//!   키:   PBKDF2-HMAC-SHA256(비밀번호 UTF-8, salt, 65,536회) → 256비트
//!   형식: salt(16) | iv(12) | 암호문 | 인증 태그(16), 그 전체를 Base64 로

use aes_gcm::aead::{Aead, KeyInit};
use aes_gcm::{Aes256Gcm, Nonce};
use sha2::Sha256;

use super::java;

pub const SALT_LENGTH: usize = 16;
pub const IV_LENGTH: usize = 12;
pub const TAG_LENGTH: usize = 16;
pub const ITERATIONS: u32 = 65_536;
const HEADER: usize = SALT_LENGTH + IV_LENGTH;

#[derive(Debug, PartialEq, Eq)]
pub enum AesError {
    /// 암호문 형식이 아님 (Base64 가 아니거나 너무 짧음)
    NotCiphertext,
    /// 비밀번호가 틀렸거나 내용이 손상됨
    WrongPassword,
}

fn derive_key(password: &[u16], salt: &[u8]) -> [u8; 32] {
    let mut key = [0u8; 32];
    pbkdf2::pbkdf2_hmac::<Sha256>(&java::utf8_bytes(password), salt, ITERATIONS, &mut key);
    key
}

/// 바이트를 암호화해 salt | iv | 암호문 | 태그 형식으로 돌려줌
pub fn encrypt(plain: &[u8], password: &[u16]) -> Vec<u8> {
    let mut header = [0u8; HEADER];
    getrandom::getrandom(&mut header).expect("the system random number generator is not available");
    let (salt, iv) = header.split_at(SALT_LENGTH);
    let mut key = derive_key(password, salt);
    let cipher = Aes256Gcm::new_from_slice(&key).expect("32-byte key");
    key.fill(0);
    let sealed = cipher.encrypt(Nonce::from_slice(iv), plain).expect("AES-GCM encryption cannot fail");
    let mut out = Vec::with_capacity(HEADER + sealed.len());
    out.extend_from_slice(&header);
    out.extend_from_slice(&sealed); // aes-gcm 은 암호문 뒤에 태그를 붙여 줌 = Java 와 같은 순서
    out
}

/// `encrypt` 로 만든 바이트를 풀기
pub fn decrypt(data: &[u8], password: &[u16]) -> Result<Vec<u8>, AesError> {
    if data.len() < HEADER + TAG_LENGTH {
        return Err(AesError::NotCiphertext);
    }
    let (salt, rest) = data.split_at(SALT_LENGTH);
    let (iv, sealed) = rest.split_at(IV_LENGTH);
    let mut key = derive_key(password, salt);
    let cipher = Aes256Gcm::new_from_slice(&key).expect("32-byte key");
    key.fill(0);
    cipher.decrypt(Nonce::from_slice(iv), sealed).map_err(|_| AesError::WrongPassword)
}

/// 텍스트를 암호화해 메신저 등에 붙여넣을 수 있는 Base64 문자열로 만듦
pub fn encrypt_text(plain: &[u16], password: &[u16]) -> Vec<u16> {
    java::base64_encode(&encrypt(&java::utf8_bytes(plain), password)).encode_utf16().collect()
}

pub fn decrypt_text(encoded: &[u16], password: &[u16]) -> Result<Vec<u16>, AesError> {
    let compact: Vec<u16> = encoded.iter().copied().filter(|&c| !java::is_regex_space(c)).collect();
    let data = java::base64_decode(&compact).ok_or(AesError::NotCiphertext)?;
    Ok(java::utf8_string(&decrypt(&data, password)?))
}
