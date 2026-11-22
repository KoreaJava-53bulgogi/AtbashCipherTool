//! 간단한 인코딩: Base64, Hex, URL, ROT13 (C# TextCodec.cs = Java TextCodec.java 와 같은 결과).
//! 디코딩에 실패하면 `None`.

use super::java;

fn units(s: &str) -> Vec<u16> {
    s.encode_utf16().collect()
}

pub fn to_base64(text: &[u16]) -> Vec<u16> {
    units(&java::base64_encode(&java::utf8_bytes(text)))
}

/// 공백, URL-safe 문자(-_), 빠진 '=' 패딩도 받아줌
pub fn from_base64(text: &[u16]) -> Option<Vec<u16>> {
    let mut s: Vec<u16> = text
        .iter()
        .filter(|&&c| !java::is_regex_space(c))
        .map(|&c| match c {
            0x2D => 0x2B, // '-' → '+'
            0x5F => 0x2F, // '_' → '/'
            _ => c,
        })
        .collect();
    while s.len() % 4 != 0 {
        s.push(b'=' as u16);
    }
    java::utf8_string_strict(&java::base64_decode(&s)?)
}

/// "Hi" → "48 69"
pub fn to_hex(text: &[u16]) -> Vec<u16> {
    let parts: Vec<String> = java::utf8_bytes(text).iter().map(|b| format!("{b:02X}")).collect();
    units(&parts.join(" "))
}

fn is_hex_digit(c: u16) -> bool {
    matches!(c, 0x30..=0x39 | 0x41..=0x46 | 0x61..=0x66)
}

/// "48 69", "4869", "0x48 0x69", "48:69" 모두 받아줌
pub fn from_hex(text: &[u16]) -> Option<Vec<u16>> {
    // "0x" / "0X" 를 지움 (C# Replace("0x", "", OrdinalIgnoreCase))
    let mut without_prefix = Vec::with_capacity(text.len());
    let mut i = 0;
    while i < text.len() {
        if text[i] == b'0' as u16 && i + 1 < text.len() && (text[i + 1] == b'x' as u16 || text[i + 1] == b'X' as u16) {
            i += 2;
        } else {
            without_prefix.push(text[i]);
            i += 1;
        }
    }
    let s: Vec<u16> = without_prefix
        .into_iter()
        .filter(|&c| !(java::is_regex_space(c) || c == b':' as u16 || c == b',' as u16 || c == b'-' as u16))
        .collect();
    if s.len() % 2 != 0 || !s.iter().all(|&c| is_hex_digit(c)) {
        return None;
    }
    let bytes: Vec<u8> = s
        .chunks(2)
        .map(|p| (java::parse_int(p, 16).unwrap()) as u8)
        .collect();
    java::utf8_string_strict(&bytes)
}

/// Java URLEncoder 와 같은 규칙. 공백은 %20
pub fn to_url(text: &[u16]) -> Vec<u16> {
    let encoded = java_url_encode(text);
    let mut out = Vec::with_capacity(encoded.len());
    for c in encoded {
        if c == b'+' as u16 {
            out.extend(units("%20"));
        } else {
            out.push(c);
        }
    }
    out
}

/// Java URLDecoder 와 같은 규칙 ('+' 는 공백, 잘못된 % 는 실패)
pub fn from_url(text: &[u16]) -> Option<Vec<u16>> {
    java_url_decode(text)
}

/// 영문자만 13칸 이동. 두 번 적용하면 원래대로
pub fn rot13(text: &[u16]) -> Vec<u16> {
    text.iter()
        .map(|&c| match c {
            0x61..=0x7A => 0x61 + (c - 0x61 + 13) % 26,
            0x41..=0x5A => 0x41 + (c - 0x41 + 13) % 26,
            _ => c,
        })
        .collect()
}

// ---- java.net.URLEncoder / URLDecoder 를 그대로 옮김 ----------------------------------

fn dont_need_encoding(c: u16) -> bool {
    matches!(c, 0x61..=0x7A | 0x41..=0x5A | 0x30..=0x39 | 0x20 | 0x2D | 0x5F | 0x2E | 0x2A)
}

fn java_url_encode(s: &[u16]) -> Vec<u16> {
    let mut out = Vec::with_capacity(s.len());
    let mut i = 0;
    while i < s.len() {
        let c = s[i];
        if dont_need_encoding(c) {
            out.push(if c == 0x20 { b'+' as u16 } else { c });
            i += 1;
            continue;
        }
        // 인코딩이 필요한 글자들을 이어서 모은 뒤 UTF-8 바이트로 바꿈
        let start = i;
        loop {
            let c = s[i];
            i += 1;
            if java::is_high_surrogate(c) && i < s.len() && java::is_low_surrogate(s[i]) {
                i += 1;
            }
            if i >= s.len() || dont_need_encoding(s[i]) {
                break;
            }
        }
        for b in java::utf8_bytes(&s[start..i]) {
            out.extend(units(&format!("%{b:02X}")));
        }
    }
    out
}

fn java_url_decode(s: &[u16]) -> Option<Vec<u16>> {
    let n = s.len();
    let mut out = Vec::with_capacity(n);
    let mut i = 0;
    while i < n {
        let mut c = s[i];
        if c == b'+' as u16 {
            out.push(0x20);
            i += 1;
        } else if c == b'%' as u16 {
            let mut bytes = Vec::new();
            while i + 2 < n && c == b'%' as u16 {
                let v = java::parse_int(&s[i + 1..i + 3], 16)?; // Java 는 "+1" 처럼 부호도 받아줌
                if v < 0 {
                    return None;
                }
                bytes.push(v as u8);
                i += 3;
                if i < n {
                    c = s[i];
                }
            }
            if i < n && c == b'%' as u16 {
                return None; // "Incomplete trailing escape (%) pattern"
            }
            out.extend(java::utf8_string(&bytes));
        } else {
            out.push(c);
            i += 1;
        }
    }
    Some(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn u(s: &str) -> Vec<u16> {
        units(s)
    }

    #[test]
    fn hex_and_url_examples() {
        assert_eq!(to_hex(&u("Hi")), u("48 69"));
        assert_eq!(from_hex(&u("0x48:0X69")), Some(u("Hi")));
        assert_eq!(from_hex(&u("486")), None);
        assert_eq!(to_url(&u("a b+한")), u("a%20b%2B%ED%95%9C"));
        assert_eq!(from_url(&u("a+b%20%ED%95%9C")), Some(u("a b 한")));
        assert_eq!(from_url(&u("%4")), None);
        assert_eq!(from_url(&u("%+1")), Some(vec![1])); // Java Integer.parseInt("+1", 16)
    }
}
